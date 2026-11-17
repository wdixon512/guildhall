namespace Guildhall.AgentHost.Api;

public sealed record RegisteredProjectResponse(
    Guid Id,
    string Name,
    string RootPath,
    bool IsGitRepository,
    string? DefaultBranch,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime LastOpenedUtc,
    long Revision);

public sealed record RegisterProjectRequest(string RootPath, string? Name = null);

public sealed record ProjectBranchesResponse(
    IReadOnlyList<string> Branches,
    string? DefaultBranch);

public sealed record CreateWorktreeRequest(string? BaseBranch = null);

public sealed record WorktreeResponse(
    Guid Id,
    Guid ProjectId,
    Guid ConversationId,
    string BranchName,
    string WorktreePath,
    string BaseCommitSha,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record WorktreeDiffResponse(
    Guid WorktreeId,
    string BranchName,
    string BaseCommitSha,
    string DiffText,
    IReadOnlyList<string> ChangedFiles,
    bool HasChanges);

public sealed record WorktreeMergeRequest(string? CommitMessage = null);

public sealed record WorktreeMergeResponse(
    bool Merged,
    string? MergedCommitSha,
    string Message);

public sealed record RollbackSnapshotView(
    Guid Id,
    Guid RunId,
    Guid RunEventId,
    string Strategy,
    string? TargetPath,
    string Status,
    DateTime CreatedUtc,
    DateTime? RolledBackUtc);

public sealed record WorkspaceAccessPolicyResponse(
    string? PolicyOverride,
    long Revision);

public sealed record UpdateWorkspaceAccessPolicyRequest(
    string? PolicyOverride,
    long ExpectedRevision);

public sealed record WorktreeSettingsResponse(
    string? DefaultWorktreeRootPath,
    long Revision);

public sealed record UpdateWorktreeSettingsRequest(
    string? DefaultWorktreeRootPath,
    long ExpectedRevision);
