using System.Text.Json;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Security;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Workspaces;

public sealed class ToolRollbackService(
    IDbContextFactory<GuildhallDbContext> dbFactory,
    WorkspacePathGuard pathGuard,
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber,
    TimeProvider timeProvider)
{
    private const int MaxBackupBytes = 512 * 1024;

    private sealed record FileBackupPayload(bool Existed, string? ContentBase64);

    public async Task<ToolRollbackSnapshot?> CapturePreStateAsync(
        Guid runId,
        Guid runEventId,
        Guid conversationId,
        Guid? worktreeId,
        string? workspaceRoot,
        string toolName,
        string argumentsJson,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (toolName is "fs_write_file" or "fs_edit_file" or "fs_delete_file")
        {
            string? requestedPath = null;
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                if (doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                    requestedPath = p.GetString();
            }
            catch { }

            var check = pathGuard.ValidateWritePath(workspaceRoot, requestedPath);
            if (!check.IsAllowed || check.FullPath is null)
                return null;

            string strategy;
            string preState;
            var status = "Available";
            if (!File.Exists(check.FullPath))
            {
                strategy = "FileBackup";
                preState = JsonSerializer.Serialize(new FileBackupPayload(false, null));
            }
            else
            {
                var info = new FileInfo(check.FullPath);
                if (info.Length <= MaxBackupBytes)
                {
                    var bytes = await File.ReadAllBytesAsync(check.FullPath, ct);
                    strategy = "FileBackup";
                    preState = JsonSerializer.Serialize(new FileBackupPayload(true, Convert.ToBase64String(bytes)));
                }
                else
                {
                    strategy = "NonReversible";
                    preState = "{\"reason\":\"File exceeded 512 KiB inline backup cap.\"}";
                    status = "NotReversible";
                }
            }

            var snapshot = new ToolRollbackSnapshot
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                RunEventId = runEventId,
                ConversationId = conversationId,
                WorktreeId = worktreeId,
                Strategy = strategy,
                TargetPath = check.FullPath,
                PreStateRef = preState,
                Status = status,
                CreatedUtc = now,
            };

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.ToolRollbackSnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
            return snapshot;
        }

        if (toolName == "workspace_run_command" && !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
        {
            var gitPath = processRunner.ResolveExecutable("git");
            if (gitPath is not null)
            {
                var stashRes = await processRunner.RunBufferedAsync(
                    gitPath,
                    ["stash", "create"],
                    workspaceRoot,
                    environmentScrubber.BuildScrubbedEnvironment(),
                    ct);

                var stashRef = stashRes.ExitCode == 0 ? stashRes.StdOut.Trim() : string.Empty;
                var headRes = await processRunner.RunBufferedAsync(
                    gitPath,
                    ["rev-parse", "HEAD"],
                    workspaceRoot,
                    environmentScrubber.BuildScrubbedEnvironment(),
                    ct);
                var headRef = headRes.ExitCode == 0 ? headRes.StdOut.Trim() : string.Empty;

                if (!string.IsNullOrEmpty(headRef))
                {
                    var snapshot = new ToolRollbackSnapshot
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        RunEventId = runEventId,
                        ConversationId = conversationId,
                        WorktreeId = worktreeId,
                        Strategy = "GitTree",
                        TargetPath = workspaceRoot,
                        PreStateRef = string.IsNullOrEmpty(stashRef) ? headRef : stashRef,
                        Status = "Available",
                        CreatedUtc = now,
                    };
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    db.ToolRollbackSnapshots.Add(snapshot);
                    await db.SaveChangesAsync(ct);
                    return snapshot;
                }
            }
        }

        return null;
    }

    public async Task<(bool RolledBack, string? ErrorCode, string? ErrorMessage)> RollbackEventAsync(
        Guid runId,
        Guid eventId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshot = await db.ToolRollbackSnapshots
            .FirstOrDefaultAsync(x => x.RunId == runId && x.RunEventId == eventId, ct);
        if (snapshot is null)
            return (false, "rollback_not_found", "No rollback snapshot exists for this tool event.");

        if (snapshot.Status == "RolledBack")
            return (true, null, null);

        if (snapshot.Status != "Available" || snapshot.Strategy == "NonReversible")
            return (false, "rollback_not_reversible", "This tool execution is not reversible.");

        if (snapshot.Strategy == "FileBackup" && snapshot.TargetPath is not null && snapshot.PreStateRef is not null)
        {
            var payload = JsonSerializer.Deserialize<FileBackupPayload>(snapshot.PreStateRef);
            if (payload is null)
                return (false, "rollback_corrupt", "Rollback snapshot payload could not be read.");

            if (!payload.Existed)
            {
                if (File.Exists(snapshot.TargetPath))
                    File.Delete(snapshot.TargetPath);
            }
            else if (payload.ContentBase64 is not null)
            {
                var parent = Path.GetDirectoryName(snapshot.TargetPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                var bytes = Convert.FromBase64String(payload.ContentBase64);
                await File.WriteAllBytesAsync(snapshot.TargetPath, bytes, ct);
            }
        }
        else if (snapshot.Strategy == "GitTree" && snapshot.TargetPath is not null && snapshot.PreStateRef is not null)
        {
            var gitPath = processRunner.ResolveExecutable("git");
            if (gitPath is null)
                return (false, "git_not_installed", "git CLI was not found on PATH.");

            var env = environmentScrubber.BuildScrubbedEnvironment();
            await processRunner.RunBufferedAsync(gitPath, ["reset", "--hard", "HEAD"], snapshot.TargetPath, env, ct);
            await processRunner.RunBufferedAsync(gitPath, ["clean", "-fd"], snapshot.TargetPath, env, ct);
            if (snapshot.PreStateRef.Length >= 7)
            {
                // If PreStateRef was a stash commit rather than HEAD, apply it cleanly.
                var head = (await processRunner.RunBufferedAsync(gitPath, ["rev-parse", "HEAD"], snapshot.TargetPath, env, ct)).StdOut.Trim();
                if (!string.Equals(head, snapshot.PreStateRef, StringComparison.OrdinalIgnoreCase))
                    await processRunner.RunBufferedAsync(gitPath, ["stash", "apply", snapshot.PreStateRef], snapshot.TargetPath, env, ct);
            }
        }

        snapshot.Status = "RolledBack";
        snapshot.RolledBackUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return (true, null, null);
    }

    public static RollbackSnapshotView ToView(ToolRollbackSnapshot s) =>
        new(s.Id, s.RunId, s.RunEventId, s.Strategy, s.TargetPath, s.Status, s.CreatedUtc, s.RolledBackUtc);
}
