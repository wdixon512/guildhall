---
area: projects-worktrees-rollback
title: "Projects, Git Worktrees & Tool Rollback"
summary: "Local project registration, isolated Git worktrees, diff review/merge/discard, Tier 2 tool rollbacks, and workspace access/worktree policies."
primary_files:
  - apps/desktop/src/ProjectWorkspaceBar.tsx
  - apps/desktop/src-tauri/src/main.rs
  - apps/desktop/src/WorktreeDiffDrawer.tsx
  - apps/desktop/src/SessionToolsPane.tsx
  - apps/desktop/src/RunTraceList.tsx
  - apps/desktop/src/WorkspaceSettingsPanel.tsx
  - apps/desktop/src/App.tsx
  - services/agent-host/Api/ProjectEndpoints.cs
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Workspaces/ProjectWorkspaceService.cs
  - services/agent-host/Workspaces/ToolRollbackService.cs
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Security/ToolPermissionGate.cs
  - services/agent-host/Security/WorkspacePathGuard.cs
  - services/agent-host/Persistence/GuildhallDbContext.cs
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 4/phase-4-passoff.md"
  - "docs/masterplan/phase 4/verification.md"
---

# Projects, Git Worktrees & Tool Rollback

Covers local project registration, per-conversation isolated Git worktree creation/review/merge/discard, Tier 2 tool pre-state snapshots and rollbacks, and workspace access/worktree root policy enforcement.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `apps/desktop/src/ProjectWorkspaceBar.tsx` | Project selector, Windows browse trigger, base branch picker, worktree creation & diff review trigger | Local React busy/manualOpen/error state; calls `pickWorkspaceFolder()` & `/projects`, `/worktree` | `ProjectWorkspaceBar` |
| `apps/desktop/src-tauri/src/main.rs` | Tauri IPC folder picker via hidden STA PowerShell COM IFileDialog | Stateless `spawn_blocking` command | `pick_workspace_folder` |
| `apps/desktop/src/WorktreeDiffDrawer.tsx` | Unified diff review, optional merge commit message, Approve & Merge, and two-step Discard Worktree | `AbortController` fetch; `confirmDiscard` state | `WorktreeDiffDrawer` |
| `apps/desktop/src/RunTraceList.tsx` | Tool execution trace list with Tier 3/2 approval buttons and two-step Rollback confirmation | `confirmRollbackId` and `expandedIds` local state | `RunTraceList` |
| `apps/desktop/src/WorkspaceSettingsPanel.tsx` | Workspace access policy override (with unrestricted confirmation) and default worktree root folder settings | Shared `GuildhallBehaviorSettings` `Revision` OCC token across `accessPolicy` & `worktreeSettings` | `WorkspaceSettingsPanel` |
| `services/agent-host/Api/ProjectEndpoints.cs` | Minimal API routes for projects, branches, worktree lifecycle, tool event rollback, and workspace settings | `LocalApiBoundary` auth; OCC `Revision` check returning `409 revision_conflict` | `ProjectEndpoints.MapProjectEndpoints` |
| `services/agent-host/Workspaces/ProjectWorkspaceService.cs` | Local project registration/deletion, Git branch listing, and isolated worktree create/diff/merge/discard | `RegisteredProjects`, `ProjectWorktrees`, `Conversations.ActiveWorktreeId`, `GuildhallBehaviorSettings` | `RegisterProjectAsync`, `CreateOrGetWorktreeAsync`, `GetWorktreeDiffAsync`, `MergeWorktreeAsync`, `DiscardWorktreeAsync` |
| `services/agent-host/Workspaces/ToolRollbackService.cs` | Pre-execution Tier 2 snapshot capture (`FileBackup` <=512 KiB Base64 or `GitTree` stash) and restoration | `ToolRollbackSnapshots` table; `WorkspacePathGuard.ValidateWritePath` | `CapturePreStateAsync`, `RollbackEventAsync` |
| `services/agent-host/Chat/ChatRunner.cs` | Resolves active worktree `workingDirectory` & effective permission policy, captures Tier 2 snapshots, auto-rolls back on error | `ChatRunner.MutationGate` `SemaphoreSlim(1,1)`; `pendingApprovals` `ConcurrentDictionary` | `RunAsync`, `ExecuteToolCallWithGateAsync` |
| `services/agent-host/Security/ToolPermissionGate.cs` | Enforces effective policy matrix (`always_ask`, `ask_unsafe`, `unrestricted`) and `ExfiltrationGuard` | Stateless singleton policy evaluator | `PermissionPolicies.ResolveEffective`, `ToolPermissionGate.Evaluate` |

## Workflows

- [`project-registration-and-worktree-isolation`](../workflows/project-registration-and-worktree-isolation.md) — Registers local project folders via Tauri Windows folder picker or path input, isolates conversation edits into per-session Git worktrees, and reviews, merges (`--no-ff`), or discards changes.
- [`tool-rollback-and-workspace-policies`](../workflows/tool-rollback-and-workspace-policies.md) — Configures workspace-wide agent access policies and default worktree roots with OCC, gates Tier 2 workspace mutations, captures FileBackup/GitTree pre-states, and executes automatic or two-step manual rollbacks.

## Contracts & References

- [codebase-index](../../skills/codebase-index/SKILL.md) — Workflow index maintenance and verification rules.
- [work-work](../../skills/work-work/SKILL.md) — Orchestrated feature and bugfix workflow.
- [coding-standards.md](../../../docs/coding-standards.md) — `.NET 10`, `EF Core 10`, `Tauri 2`, and `React 19` implementation rules.
- [phase-4-passoff.md](<../../../docs/masterplan/phase 4/phase-4-passoff.md>) — Phase 4 workspaces, worktrees, and rollback architecture contract.
- [verification.md](<../../../docs/masterplan/phase 4/verification.md>) — Phase 4 verification record.
