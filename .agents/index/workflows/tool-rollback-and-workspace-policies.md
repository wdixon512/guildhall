---
id: tool-rollback-and-workspace-policies
title: "Tier 2 Tool Rollbacks & Workspace Access/Worktree Policies"
area: projects-worktrees-rollback
triggers:
  - ToolRollbackService
  - CapturePreStateAsync
  - RollbackEventAsync
  - "POST /runs/{runId}/events/{eventId}/rollback"
  - "GET/PUT /settings/access-policy"
  - "GET/PUT /settings/worktree-root"
  - RunTraceList
  - WorkspaceSettingsPanel
primary_files:
  - apps/desktop/src/RunTraceList.tsx
  - apps/desktop/src/WorkspaceSettingsPanel.tsx
  - services/agent-host/Api/ProjectEndpoints.cs
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Security/ToolPermissionGate.cs
  - services/agent-host/Workspaces/ToolRollbackService.cs
  - apps/desktop/src/App.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Security/WorkspacePathGuard.cs
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
  - boundary: "Local GuildhallDev SQL rows, workspace disk & Git stash/HEAD state"
    location: "services/agent-host/Workspaces/ToolRollbackService.cs::ToolRollbackService"
    note: "Actual rollback snapshots and GuildhallBehaviorSettings overrides live in the local GuildhallDev database alongside live workspace filesystem and git stash/HEAD state at runtime"
    static_or_runtime: "runtime"
---

# Tier 2 Tool Rollbacks & Workspace Access/Worktree Policies

Configures workspace-wide agent access policies and default worktree roots with OCC, gates Tier 2 workspace mutations, captures `FileBackup`/`GitTree` pre-states, and executes automatic or two-step manual rollbacks.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/RunTraceList.tsx` | `RunTraceList` | `~L14-L89` | `confirmRollbackId` two-step confirmation -> `onRollback(runId, eventId)` | Renders Rollback only when `ev.canRollback`, `runId`, and `onRollback` are present; requires explicit Confirm click before calling `App.rollbackToolEvent` |
| 2 | Desktop / UI | `apps/desktop/src/WorkspaceSettingsPanel.tsx` | `WorkspaceSettingsPanel` | `~L28-L146` | `GET/PUT /settings/access-policy`, `GET/PUT /settings/worktree-root` | Synchronizes shared `GuildhallBehaviorSettings.Revision` across both settings requests and requires confirmation before enabling `unrestricted` |
| 3 | Agent Host / SQL | `services/agent-host/Api/ProjectEndpoints.cs` | `ProjectEndpoints.MapProjectEndpoints` | `~L10-L147` | `POST /runs/{runId}/events/{eventId}/rollback` (L68-L74), `GET/PUT /settings/access-policy` & `/settings/worktree-root` (L76-L146) | Protected by `LocalApiBoundary`; delegates rollback to `ToolRollbackService.RollbackEventAsync`, enforces `expectedRevision` OCC (`409 revision_conflict`), and validates rooted existing `worktree-root` dirs |
| 4 | Agent Host / Security | `services/agent-host/Chat/ChatRunner.cs` | `ChatRunner.ExecuteToolCallWithGateAsync` | `~L347-L460` | Tier 2 `CapturePreStateAsync` before `InvokeToolAsync`; auto-rollback on exception or `IsError` | Only Tier 2 tools capture snapshots; failed or throwing Tier 2 tool invocations trigger `RollbackEventAsync(CancellationToken.None)` automatically |
| 5 | Security | `services/agent-host/Security/ToolPermissionGate.cs` | `ToolPermissionGate.Evaluate` | `~L24-L105` | `PermissionPolicies.ResolveEffective(workspaceOverride, conversationOverride, memberPolicy)` | `ExfiltrationGuard` denies secrets even in `unrestricted` mode; Tier 3 external tools always require human approval |
| 6 | Agent Host / SQL | `services/agent-host/Workspaces/ToolRollbackService.cs` | `ToolRollbackService.CapturePreStateAsync` | `~L22-L139` | `ToolRollbackSnapshots` (`FileBackup` <=512 KiB Base64, `NonReversible`, or `GitTree` stash/HEAD) | Validates `fs_*` paths with `WorkspacePathGuard.ValidateWritePath`; files >512 KiB record `NotReversible`; `workspace_run_command` creates git stash ref |
| 7 | Agent Host / SQL | `services/agent-host/Workspaces/ToolRollbackService.cs` | `ToolRollbackService.RollbackEventAsync` | `~L141-L200` | `ToolRollbackSnapshots.Status` `Available` -> `RolledBack`; `git reset --hard` + `clean -fd` + `stash apply` | Idempotent if already `RolledBack`; rejects `NotReversible`; deletes created files or restores Base64 bytes / `GitTree` stash |

## Contracts & Guardrails

- Follow [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md) when updating tool rollback, gate, or settings surfaces.
- Enforce [coding-standards.md](../../../docs/coding-standards.md) OCC `Revision` concurrency and strict `LocalApiBoundary` / `WorkspacePathGuard` backend guardrails.
- Preserve Tier 2 snapshot caps, `ExfiltrationGuard` overrides, and Tier 3 approval boundaries per [phase-4-passoff.md](<../../../docs/masterplan/phase 4/phase-4-passoff.md>) and [verification.md](<../../../docs/masterplan/phase 4/verification.md>).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~Phase4WorkspaceAndRollbackTests"`
- `npm.cmd --prefix apps/desktop test -- --run src/RunTraceList.test.tsx`
- `npm.cmd --prefix apps/desktop run build`
