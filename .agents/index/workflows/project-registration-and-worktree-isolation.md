---
id: project-registration-and-worktree-isolation
title: "Project Registration & Isolated Git Worktree Lifecycle"
area: projects-worktrees-rollback
triggers:
  - ProjectWorkspaceBar
  - pick_workspace_folder
  - "POST /projects"
  - "POST /conversations/{conversationId}/worktree"
  - "GET /conversations/{conversationId}/worktree/diff"
  - "POST /conversations/{conversationId}/worktree/merge"
  - "POST /conversations/{conversationId}/worktree/discard"
  - ProjectWorkspaceService
  - WorktreeDiffDrawer
primary_files:
  - apps/desktop/src/ProjectWorkspaceBar.tsx
  - apps/desktop/src-tauri/src/main.rs
  - services/agent-host/Api/ProjectEndpoints.cs
  - services/agent-host/Workspaces/ProjectWorkspaceService.cs
  - apps/desktop/src/WorktreeDiffDrawer.tsx
  - apps/desktop/src/SessionToolsPane.tsx
  - services/agent-host/Persistence/GuildhallDbContext.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 4/phase-4-passoff.md"
  - "docs/masterplan/phase 4/verification.md"
uncertainties:
  - boundary: "Host Git CLI, repository disk state & Windows COM IFileDialog"
    location: "services/agent-host/Workspaces/ProjectWorkspaceService.cs::ProjectWorkspaceService"
    note: "Depends on local machine git availability on PATH, target repository commit/branch/worktree filesystem state, and interactive Windows Shell COM IFileDialog at runtime"
    static_or_runtime: "runtime"
---

# Project Registration & Isolated Git Worktree Lifecycle

Registers local project folders via Tauri Windows folder picker or path input, isolates conversation edits into per-session Git worktrees, and reviews, merges (`--no-ff`), or discards changes.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / Tauri | `apps/desktop/src/ProjectWorkspaceBar.tsx` | `ProjectWorkspaceBar` | `~L4-L95` | `pickWorkspaceFolder()`, `POST /projects`, `POST /conversations/{id}/worktree` | Requires `activeConversation` and bootstrap config; falls back to manual path input if native folder picker fails |
| 2 | Tauri / Rust | `apps/desktop/src-tauri/src/main.rs` | `pick_workspace_folder` | `~L101-L194` | Tauri IPC `pick_workspace_folder` -> `Option<String>` | Windows spawns hidden STA PowerShell COM `IFileDialog` (`CREATE_NO_WINDOW`); non-Windows returns `Ok(None)` |
| 3 | Agent Host | `services/agent-host/Api/ProjectEndpoints.cs` | `ProjectEndpoints.MapProjectEndpoints` | `~L10-L66` | `GET/POST /projects`, `DELETE /projects/{id}`, `GET /projects/{id}/branches`, worktree diff/merge/discard | Guarded by `LocalApiBoundary` bearer token; merge endpoint returns `409 Conflict` when git merge conflicts |
| 4 | Agent Host / SQL | `services/agent-host/Workspaces/ProjectWorkspaceService.cs` | `ProjectWorkspaceService.RegisterProjectAsync` | `~L27-L148` | `RegisteredProjects` table (unique `RootPath`), `git rev-parse` / `branch --format` | Blocks UNC/network paths and bare drive roots; `DeleteProjectAsync` clears `Conversations.ProjectId` and `ActiveWorktreeId` |
| 5 | Agent Host / SQL | `services/agent-host/Workspaces/ProjectWorkspaceService.cs` | `ProjectWorkspaceService.CreateOrGetWorktreeAsync` | `~L150-L250` | `git worktree add -b guildhall/<shortId>`, `ProjectWorktrees` + `Conversations.ActiveWorktreeId` | Resolves base from `GuildhallBehaviorSettings.DefaultWorktreeRootPath` or `<project-parent>/guildhall-worktrees`; bumps `Conversation.Revision` |
| 6 | Agent Host / SQL | `services/agent-host/Workspaces/ProjectWorkspaceService.cs` | `ProjectWorkspaceService.MergeWorktreeAsync` | `~L252-L416` | `git status/diff`, `git merge --no-ff`, `git worktree remove --force`, `git branch -D` | Caps unified diff at 128 KiB and untracked files at 16 KiB; aborts conflicted merges cleanly and marks `Status` `Merged` or `Discarded` |
| 7 | Desktop / UI | `apps/desktop/src/WorktreeDiffDrawer.tsx` | `WorktreeDiffDrawer` | `~L4-L104` | `GET /conversations/{id}/worktree/diff`, `POST .../worktree/merge`, `POST .../worktree/discard` | Lazy-loaded in `SessionToolsPane` (Git changes tab); enforces two-step Confirm Discard and disables merge when `!diff?.hasChanges` |

## Contracts & Guardrails

- Follow [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md) when modifying project/worktree flows or refreshing index cards.
- Adhere to [coding-standards.md](../../../docs/coding-standards.md) for Tauri 2 IPC isolation, EF Core `GuildhallDbContext` entities, and React 19 lazy drawer loading.
- Enforce local workspace boundaries and Git worktree isolation contracts in [phase-4-passoff.md](<../../../docs/masterplan/phase 4/phase-4-passoff.md>) and [verification.md](<../../../docs/masterplan/phase 4/verification.md>).

## Targeted Verification

- `cargo test --manifest-path apps/desktop/src-tauri/Cargo.toml --locked`
- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~Phase4WorkspaceAndRollbackTests"`
- `npm.cmd --prefix apps/desktop run build`
