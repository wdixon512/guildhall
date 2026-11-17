using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;
using Guildhall.AgentHost.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Api;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects", async (ProjectWorkspaceService projects, CancellationToken ct) =>
            Results.Ok(await projects.ListProjectsAsync(ct)));

        app.MapPost("/projects", async (RegisterProjectRequest request, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (project, errCode, errMsg) = await projects.RegisterProjectAsync(request, ct);
            return project is not null
                ? Results.Ok(project)
                : Results.BadRequest(new ErrorResponse(errCode ?? "project_register_failed", errMsg ?? "Could not register project."));
        });

        app.MapDelete("/projects/{projectId:guid}", async (Guid projectId, ProjectWorkspaceService projects, CancellationToken ct) =>
            await projects.DeleteProjectAsync(projectId, ct)
                ? Results.NoContent()
                : Results.NotFound(new ErrorResponse("project_not_found", "Project not found.")));

        app.MapGet("/projects/{projectId:guid}/branches", async (Guid projectId, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (result, errCode, errMsg) = await projects.ListBranchesAsync(projectId, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.BadRequest(new ErrorResponse(errCode ?? "branches_list_failed", errMsg ?? "Could not list project branches."));
        });

        app.MapPost("/conversations/{conversationId:guid}/worktree", async (Guid conversationId, CreateWorktreeRequest? request, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (worktree, errCode, errMsg) = await projects.CreateOrGetWorktreeAsync(conversationId, request?.BaseBranch, ct);
            return worktree is not null
                ? Results.Ok(worktree)
                : Results.BadRequest(new ErrorResponse(errCode ?? "worktree_create_failed", errMsg ?? "Could not create worktree."));
        });

        app.MapGet("/conversations/{conversationId:guid}/worktree/diff", async (Guid conversationId, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (diff, errCode, errMsg) = await projects.GetWorktreeDiffAsync(conversationId, ct);
            return diff is not null
                ? Results.Ok(diff)
                : Results.BadRequest(new ErrorResponse(errCode ?? "worktree_diff_failed", errMsg ?? "Could not compute worktree diff."));
        });

        app.MapPost("/conversations/{conversationId:guid}/worktree/merge", async (Guid conversationId, WorktreeMergeRequest request, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (result, errCode, errMsg) = await projects.MergeWorktreeAsync(conversationId, request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.Conflict(new ErrorResponse(errCode ?? "worktree_merge_failed", errMsg ?? "Could not merge worktree."));
        });

        app.MapPost("/conversations/{conversationId:guid}/worktree/discard", async (Guid conversationId, ProjectWorkspaceService projects, CancellationToken ct) =>
        {
            var (discarded, errCode, errMsg) = await projects.DiscardWorktreeAsync(conversationId, ct);
            return discarded
                ? Results.NoContent()
                : Results.BadRequest(new ErrorResponse(errCode ?? "worktree_discard_failed", errMsg ?? "Could not discard worktree."));
        });

        app.MapPost("/runs/{runId:guid}/events/{eventId:guid}/rollback", async (Guid runId, Guid eventId, ToolRollbackService rollbacks, CancellationToken ct) =>
        {
            var (rolledBack, errCode, errMsg) = await rollbacks.RollbackEventAsync(runId, eventId, ct);
            return rolledBack
                ? Results.Ok(new { rolledBack = true })
                : Results.BadRequest(new ErrorResponse(errCode ?? "rollback_failed", errMsg ?? "Could not roll back tool execution."));
        });

        app.MapGet("/settings/access-policy", async (IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            return settings is null
                ? Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings not found."))
                : Results.Ok(new WorkspaceAccessPolicyResponse(settings.WorkspacePolicyOverride, settings.Revision));
        });

        app.MapPut("/settings/access-policy", async (UpdateWorkspaceAccessPolicyRequest request, IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            string? normalized = null;
            if (!string.IsNullOrWhiteSpace(request.PolicyOverride))
                normalized = PermissionPolicies.Normalize(request.PolicyOverride);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (settings is null)
                return Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings not found."));

            if (settings.Revision != request.ExpectedRevision)
                return Results.Conflict(new ErrorResponse("revision_conflict", "Settings were modified concurrently; refresh and retry."));

            settings.WorkspacePolicyOverride = normalized;
            settings.Revision++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new WorkspaceAccessPolicyResponse(settings.WorkspacePolicyOverride, settings.Revision));
        });

        app.MapGet("/settings/worktree-root", async (IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            return settings is null
                ? Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings not found."))
                : Results.Ok(new WorktreeSettingsResponse(settings.DefaultWorktreeRootPath, settings.Revision));
        });

        app.MapPut("/settings/worktree-root", async (UpdateWorktreeSettingsRequest request, IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            string? normalized = null;
            if (!string.IsNullOrWhiteSpace(request.DefaultWorktreeRootPath))
            {
                var trimmed = request.DefaultWorktreeRootPath.Trim();
                if (!Path.IsPathRooted(trimmed))
                    return Results.BadRequest(new ErrorResponse("invalid_worktree_root", "Default worktree folder must be an absolute path."));
                try
                {
                    normalized = Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return Results.BadRequest(new ErrorResponse("invalid_worktree_root", "Default worktree folder path is invalid."));
                }
                if (!Directory.Exists(normalized))
                    return Results.BadRequest(new ErrorResponse("invalid_worktree_root", "Selected folder does not exist on disk."));
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (settings is null)
                return Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings not found."));

            if (settings.Revision != request.ExpectedRevision)
                return Results.Conflict(new ErrorResponse("revision_conflict", "Settings were modified concurrently; refresh and retry."));

            settings.DefaultWorktreeRootPath = normalized;
            settings.Revision++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new WorktreeSettingsResponse(settings.DefaultWorktreeRootPath, settings.Revision));
        });
    }
}
