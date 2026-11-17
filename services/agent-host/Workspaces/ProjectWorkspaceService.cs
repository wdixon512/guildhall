using System.Text;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers.Cli;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Workspaces;

public sealed class ProjectWorkspaceService(
    IDbContextFactory<GuildhallDbContext> dbFactory,
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber,
    TimeProvider timeProvider)
{
    private const int MaxDiffChars = 128 * 1024;

    public async Task<IReadOnlyList<RegisteredProjectResponse>> ListProjectsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.RegisteredProjects.AsNoTracking()
            .OrderByDescending(x => x.LastOpenedUtc)
            .ToListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    public async Task<(RegisteredProjectResponse? Project, string? ErrorCode, string? ErrorMessage)> RegisterProjectAsync(
        RegisterProjectRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
            return (null, "invalid_path", "Project folder path is required.");

        var raw = request.RootPath.Trim();
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
            return (null, "invalid_path", "UNC or network paths are not supported for local project registration.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return (null, "invalid_path", "The specified folder path is invalid.");
        }

        if (!Directory.Exists(fullPath))
            return (null, "directory_not_found", $"Folder '{fullPath}' does not exist on this machine.");

        if (Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar) == fullPath)
            return (null, "invalid_path", "Registering an entire drive root is not permitted; select a project subfolder.");

        var (isGit, defaultBranch) = await ProbeGitRepositoryAsync(fullPath, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var projectName = string.IsNullOrWhiteSpace(request.Name)
            ? Path.GetFileName(fullPath)
            : request.Name.Trim();
        if (string.IsNullOrWhiteSpace(projectName)) projectName = "Local Project";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.RootPath == fullPath, ct);
        if (existing is not null)
        {
            existing.Name = projectName;
            existing.IsGitRepository = isGit;
            existing.DefaultBranch = defaultBranch;
            existing.LastOpenedUtc = now;
            existing.UpdatedUtc = now;
            existing.Revision++;
            await db.SaveChangesAsync(ct);
            return (ToResponse(existing), null, null);
        }

        var created = new RegisteredProject
        {
            Id = Guid.NewGuid(),
            Name = projectName[..Math.Min(projectName.Length, 160)],
            RootPath = fullPath,
            IsGitRepository = isGit,
            DefaultBranch = defaultBranch,
            CreatedUtc = now,
            UpdatedUtc = now,
            LastOpenedUtc = now,
            Revision = 1,
        };
        db.RegisteredProjects.Add(created);
        await db.SaveChangesAsync(ct);
        return (ToResponse(created), null, null);
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (project is null) return false;

        await db.Conversations
            .Where(c => c.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ProjectId, (Guid?)null)
                .SetProperty(c => c.ActiveWorktreeId, (Guid?)null), ct);

        db.RegisteredProjects.Remove(project);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(ProjectBranchesResponse? Result, string? ErrorCode, string? ErrorMessage)> ListBranchesAsync(
        Guid projectId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.RegisteredProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (project is null)
            return (null, "project_not_found", "Project not found.");
        if (!project.IsGitRepository)
            return (new ProjectBranchesResponse([], null), null, null);

        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null)
            return (null, "git_not_installed", "git CLI was not found on PATH.");

        var res = await RunGitAsync(gitPath, project.RootPath, ["branch", "--format=%(refname:short)"], ct);
        var all = res.ExitCode == 0
            ? res.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b) && !b.StartsWith("guildhall/", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var defaultCandidates = new[] { project.DefaultBranch, "main", "master" }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        var ordered = all
            .OrderBy(b =>
            {
                var idx = defaultCandidates.FindIndex(d => string.Equals(d, b, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (new ProjectBranchesResponse(ordered, project.DefaultBranch ?? ordered.FirstOrDefault()), null, null);
    }

    public async Task<(WorktreeResponse? Worktree, string? ErrorCode, string? ErrorMessage)> CreateOrGetWorktreeAsync(
        Guid conversationId,
        string? baseBranch,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null)
            return (null, "conversation_not_found", "Conversation not found.");

        if (conversation.ActiveWorktreeId is { } existingId)
        {
            var active = await db.ProjectWorktrees.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == existingId && x.Status == "Active", ct);
            if (active is not null && Directory.Exists(active.WorktreePath))
                return (ToWorktreeResponse(active), null, null);
        }

        RegisteredProject? project = null;
        if (conversation.ProjectId is { } pid)
            project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.Id == pid, ct);
        else if (!string.IsNullOrWhiteSpace(conversation.WorkspaceRootPath))
        {
            var normalized = Path.GetFullPath(conversation.WorkspaceRootPath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.RootPath == normalized, ct);
            if (project is null && Directory.Exists(normalized))
            {
                var (reg, errCode, errMsg) = await RegisterProjectAsync(new RegisterProjectRequest(normalized), ct);
                if (reg is null) return (null, errCode, errMsg);
                project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.Id == reg.Id, ct);
            }
        }

        if (project is null)
            return (null, "project_not_selected", "Select or register a local Git project before creating a worktree.");

        if (!project.IsGitRepository)
            return (null, "not_git_repository", "Worktree isolation requires a Git repository. Plain local folders can still be edited directly.");

        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null)
            return (null, "git_not_installed", "git CLI was not found on PATH.");

        var targetRef = string.IsNullOrWhiteSpace(baseBranch) ? "HEAD" : baseBranch.Trim();
        var headResult = await RunGitAsync(gitPath, project.RootPath, ["rev-parse", targetRef], ct);
        if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StdOut))
            return (null, "git_no_commits", $"Repository could not resolve '{targetRef}'. Ensure the branch has at least one commit.");

        var baseSha = headResult.StdOut.Trim();
        var worktreeId = Guid.NewGuid();
        var shortId = worktreeId.ToString("N")[..8];
        var branchName = $"guildhall/{shortId}";
        var safeProjectFolder = string.Concat(project.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (string.IsNullOrWhiteSpace(safeProjectFolder)) safeProjectFolder = "project";
        var settings = await db.GuildhallBehaviorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        string worktreesBase;
        if (!string.IsNullOrWhiteSpace(settings?.DefaultWorktreeRootPath))
        {
            worktreesBase = settings.DefaultWorktreeRootPath;
        }
        else
        {
            var parentDir = Directory.GetParent(project.RootPath)?.FullName
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guildhall");
            worktreesBase = Path.Combine(parentDir, "guildhall-worktrees");
        }
        var worktreesRoot = Path.Combine(worktreesBase, safeProjectFolder);
        Directory.CreateDirectory(worktreesRoot);
        var worktreePath = Path.Combine(worktreesRoot, shortId);

        var addResult = await RunGitAsync(gitPath, project.RootPath, ["worktree", "add", "-b", branchName, worktreePath, baseSha], ct);
        if (addResult.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(addResult.StdErr) ? addResult.StdOut : addResult.StdErr;
            return (null, "git_worktree_failed", $"Failed to create Git worktree: {err.Trim()}");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new ProjectWorktree
        {
            Id = worktreeId,
            ProjectId = project.Id,
            ConversationId = conversationId,
            BranchName = branchName,
            WorktreePath = worktreePath,
            BaseCommitSha = baseSha,
            Status = "Active",
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.ProjectWorktrees.Add(entity);
        conversation.ProjectId = project.Id;
        conversation.ActiveWorktreeId = entity.Id;
        conversation.WorkspaceRootPath = project.RootPath;
        conversation.UpdatedUtc = now;
        conversation.Revision++;
        await db.SaveChangesAsync(ct);

        return (ToWorktreeResponse(entity), null, null);
    }

    public async Task<(WorktreeDiffResponse? Diff, string? ErrorCode, string? ErrorMessage)> GetWorktreeDiffAsync(
        Guid conversationId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation?.ActiveWorktreeId is not { } worktreeId)
            return (null, "worktree_not_active", "No active worktree is attached to this conversation.");

        var worktree = await db.ProjectWorktrees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == worktreeId && x.Status == "Active", ct);
        if (worktree is null || !Directory.Exists(worktree.WorktreePath))
            return (null, "worktree_not_found", "Active worktree directory no longer exists.");

        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null)
            return (null, "git_not_installed", "git CLI was not found on PATH.");

        var statusRes = await RunGitAsync(gitPath, worktree.WorktreePath, ["status", "--porcelain"], ct);
        var diffRes = await RunGitAsync(gitPath, worktree.WorktreePath, ["diff", worktree.BaseCommitSha], ct);

        var statusLines = statusRes.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var changedFiles = statusLines
            .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
            sb.AppendLine(diffRes.StdOut.TrimEnd());

        // Also include committed diff if any commits were made on the worktree branch.
        var committedDiff = await RunGitAsync(gitPath, worktree.WorktreePath, ["diff", $"{worktree.BaseCommitSha}..HEAD"], ct);
        if (!string.IsNullOrWhiteSpace(committedDiff.StdOut) && sb.Length == 0)
            sb.AppendLine(committedDiff.StdOut.TrimEnd());

        // Include newly created untracked files so they appear in unified diff review.
        foreach (var line in statusLines)
        {
            if (!line.StartsWith("?? ", StringComparison.Ordinal) || line.Length <= 3) continue;
            var relPath = line[3..].Trim().Trim('"');
            var fullFile = Path.Combine(worktree.WorktreePath, relPath);
            if (!File.Exists(fullFile)) continue;
            try
            {
                var info = new FileInfo(fullFile);
                if (info.Length > 16 * 1024)
                {
                    sb.AppendLine($"--- /dev/null\n+++ b/{relPath}\n@@ -0,0 +1 @@\n+[new file: {info.Length} bytes]");
                    continue;
                }
                var fileLines = await File.ReadAllLinesAsync(fullFile, Encoding.UTF8, ct);
                sb.AppendLine($"--- /dev/null");
                sb.AppendLine($"+++ b/{relPath}");
                sb.AppendLine($"@@ -0,0 +1,{fileLines.Length} @@");
                foreach (var fl in fileLines)
                    sb.AppendLine($"+{fl}");
            }
            catch { }
        }

        var diffText = sb.ToString();
        if (diffText.Length > MaxDiffChars)
            diffText = diffText[..MaxDiffChars] + "\n… [diff truncated at 128 KiB]";

        var hasChanges = changedFiles.Count > 0 || !string.IsNullOrWhiteSpace(diffText);
        return (new WorktreeDiffResponse(worktree.Id, worktree.BranchName, worktree.BaseCommitSha, diffText, changedFiles, hasChanges), null, null);
    }

    public async Task<(WorktreeMergeResponse? Result, string? ErrorCode, string? ErrorMessage)> MergeWorktreeAsync(
        Guid conversationId,
        WorktreeMergeRequest request,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation?.ActiveWorktreeId is not { } worktreeId)
            return (null, "worktree_not_active", "No active worktree is attached to this conversation.");

        var worktree = await db.ProjectWorktrees.FirstOrDefaultAsync(x => x.Id == worktreeId && x.Status == "Active", ct);
        if (worktree is null)
            return (null, "worktree_not_found", "Active worktree record not found.");

        var project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.Id == worktree.ProjectId, ct);
        if (project is null)
            return (null, "project_not_found", "Registered project not found.");

        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null)
            return (null, "git_not_installed", "git CLI was not found on PATH.");

        if (Directory.Exists(worktree.WorktreePath))
        {
            var status = await RunGitAsync(gitPath, worktree.WorktreePath, ["status", "--porcelain"], ct);
            if (!string.IsNullOrWhiteSpace(status.StdOut))
            {
                await RunGitAsync(gitPath, worktree.WorktreePath, ["add", "-A"], ct);
                var msg = string.IsNullOrWhiteSpace(request.CommitMessage)
                    ? $"Guildhall: apply approved worktree changes ({worktree.BranchName})"
                    : request.CommitMessage.Trim();
                var commitRes = await RunGitAsync(gitPath, worktree.WorktreePath, ["commit", "-m", msg], ct);
                if (commitRes.ExitCode != 0)
                    return (null, "git_commit_failed", $"Could not commit worktree changes: {commitRes.StdErr.Trim()}");
            }
        }

        var mergeRes = await RunGitAsync(gitPath, project.RootPath, ["merge", "--no-ff", worktree.BranchName, "-m", $"Merge {worktree.BranchName}"], ct);
        if (mergeRes.ExitCode != 0)
        {
            await RunGitAsync(gitPath, project.RootPath, ["merge", "--abort"], ct);
            return (null, "git_merge_conflict", $"Merge into primary branch conflicted and was aborted: {mergeRes.StdErr.Trim()}");
        }

        var headSha = (await RunGitAsync(gitPath, project.RootPath, ["rev-parse", "HEAD"], ct)).StdOut.Trim();
        await CleanupWorktreeOnDiskAsync(gitPath, project.RootPath, worktree, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        worktree.Status = "Merged";
        worktree.UpdatedUtc = now;
        conversation.ActiveWorktreeId = null;
        conversation.UpdatedUtc = now;
        conversation.Revision++;
        await db.SaveChangesAsync(ct);

        return (new WorktreeMergeResponse(true, headSha, $"Merged '{worktree.BranchName}' into '{project.DefaultBranch ?? "current branch"}'."), null, null);
    }

    public async Task<(bool Discarded, string? ErrorCode, string? ErrorMessage)> DiscardWorktreeAsync(
        Guid conversationId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation?.ActiveWorktreeId is not { } worktreeId)
            return (false, "worktree_not_active", "No active worktree is attached to this conversation.");

        var worktree = await db.ProjectWorktrees.FirstOrDefaultAsync(x => x.Id == worktreeId && x.Status == "Active", ct);
        if (worktree is null)
            return (false, "worktree_not_found", "Active worktree record not found.");

        var project = await db.RegisteredProjects.FirstOrDefaultAsync(x => x.Id == worktree.ProjectId, ct);
        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is not null && project is not null)
            await CleanupWorktreeOnDiskAsync(gitPath, project.RootPath, worktree, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        worktree.Status = "Discarded";
        worktree.UpdatedUtc = now;
        conversation.ActiveWorktreeId = null;
        conversation.UpdatedUtc = now;
        conversation.Revision++;
        await db.SaveChangesAsync(ct);

        return (true, null, null);
    }

    private async Task CleanupWorktreeOnDiskAsync(string gitPath, string projectRoot, ProjectWorktree worktree, CancellationToken ct)
    {
        if (Directory.Exists(worktree.WorktreePath))
            await RunGitAsync(gitPath, projectRoot, ["worktree", "remove", "--force", worktree.WorktreePath], ct);

        await RunGitAsync(gitPath, projectRoot, ["worktree", "prune"], ct);
        await RunGitAsync(gitPath, projectRoot, ["branch", "-D", worktree.BranchName], ct);
    }

    private async Task<(bool IsGit, string? DefaultBranch)> ProbeGitRepositoryAsync(string rootPath, CancellationToken ct)
    {
        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null) return (false, null);

        var check = await RunGitAsync(gitPath, rootPath, ["rev-parse", "--is-inside-work-tree"], ct);
        if (check.ExitCode != 0 || !check.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            return (false, null);

        var branch = await RunGitAsync(gitPath, rootPath, ["rev-parse", "--abbrev-ref", "HEAD"], ct);
        return (true, branch.ExitCode == 0 ? branch.StdOut.Trim() : null);
    }

    private Task<CliProcessResult> RunGitAsync(string gitPath, string workingDir, IReadOnlyList<string> args, CancellationToken ct) =>
        processRunner.RunBufferedAsync(gitPath, args, workingDir, environmentScrubber.BuildScrubbedEnvironment(), ct);

    public static RegisteredProjectResponse ToResponse(RegisteredProject p) =>
        new(p.Id, p.Name, p.RootPath, p.IsGitRepository, p.DefaultBranch, p.CreatedUtc, p.UpdatedUtc, p.LastOpenedUtc, p.Revision);

    public static WorktreeResponse ToWorktreeResponse(ProjectWorktree w) =>
        new(w.Id, w.ProjectId, w.ConversationId, w.BranchName, w.WorktreePath, w.BaseCommitSha, w.Status, w.CreatedUtc, w.UpdatedUtc);
}
