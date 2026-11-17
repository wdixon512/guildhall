# Phase 4 Passoff Document: Controlled Coding Workspace, Worktrees, Rollbacks & Enforced Access Modes

**Handoff From:** Phase 3 (`phase_three_implementation_plan`, merged to `main` at `f61fa95`)  
**Date:** November 17, 2026  
**Database Baseline:** `GuildhallDev` at migration `20261117024435_Phase3McpToolsAndTraces` (`AgentDefinitions`, `Conversations`, `ChatRunEvents`, `McpServerRegistrations`)

---

## 1. Phase 3 Baseline Already in Place

Phase 3 (`P3-00`–`P3-05`) delivered:
- **Security Guardrails (`services/agent-host/Security/`):**
  - `WorkspacePathGuard`: canonical path resolution, traversal & symlink/reparse escape rejection, secret file blocklist.
  - `ExfiltrationGuard`: automatic secret redaction in tool args/results/logs and outbound Tier 3 checks.
  - `ToolPermissionGate`: enforces per-member `EnabledToolIds` and `PermissionPolicy` (`ask_unsafe`, `always_ask`); currently rejects `Tier2_WorkspaceWrite` with `tier2_planned_phase4`.
- **MCP Runtime & Tier 5 Health (`services/agent-host/Mcp/`):**
  - Built-in JSON-RPC 2.0 client (`McpJsonRpcClient`), `McpHealthSupervisor` (`Connected`/`Degraded`/`Failed`/`Disconnected`, `2s`/`5s`/`15s` backoff), and `McpRegistry` (`stdio` + `Streamable HTTP`).
  - Built-in providers: `LocalFilesystemMcpProvider` (`filesystem-mcp`), `LocalGitMcpProvider` (`git-mcp`), `GitHubMcpProvider` (`github-mcp`), `GoogleDriveMcpProvider` (`google-drive-mcp`), `GmailMcpProvider` (`gmail-mcp`).
- **Bounded Tool Loop & CLI Split:**
  - `ChatRunner`: 6-round tool loop, `ChatRunEvents` persistence, and `PendingApproval` pause/resume via `POST /runs/{runId}/events/{eventId}/decision`.
  - `CliHarnessAdapters.cs`: already split `CliHarnessEngine` into `AgyCliEngine`, `CodexCliEngine`, `GeminiCliEngine`, `ClaudeCliEngine`, and `CursorCliEngine` (note: `GeminiCliEngine` and interactive ACP/`app-server`/`stream-json` approval callbacks remain to be wired in Phase 4).
- **Frontend UI (`apps/desktop/src/`):**
  - Modularized `App.tsx` (`CouncilSidebar`, `ChatTranscript`, `ChatComposer`, `WorkspaceSettingsPanel`, `ConfirmationDialogs`).
  - `RunTraceList.tsx` (inline tool traces + `Approve`/`Reject`), `ContextInspector.tsx` (Tier 5 MCP health + `Reconnect` + tool schema tokens), `CouncilMember.tsx` (allowed MCP tools + permission policy with `Unrestricted` disabled for Phase 4), and session workspace root bar.

---

## 2. Immediate Carryover Task (Include Early in Phase 4): `github_list_repositories`

**Why:** `GitHubMcpProvider.cs` currently requires `owner` and `repo` on every tool (`github_get_repository`, `github_list_issues`, `github_list_pull_requests`, `github_get_pull_request_files`, `github_create_issue`, `github_create_issue_comment`). Asking a Council Member *"list all repos on my GitHub account"* has no matching tool even when `GitHub (Tier 1/3)` is checked in the member's profile.

### Implementation Plan
1. **Descriptor (`services/agent-host/Mcp/Providers/GitHubMcpProvider.cs`):**
   - Add `github_list_repositories` (`ToolSecurityTiers.Tier1`, read-only) to `Tools`:
     - Optional properties: `visibility` (`"all"` | `"public"` | `"private"`, default `"all"`) and `perPage` (integer, clamped `1..50`, default `30`).
2. **Handler (`GitHubMcpProvider.InvokeAsync`):**
   - Handle `request.ToolName == "github_list_repositories"` **before** the `owner`/`repo` required-argument guard.
   - Call `GET /user/repos?visibility={visibility}&sort=updated&per_page={perPage}` via `ExecuteJsonAsync`.
   - Consider projecting/trimming the returned JSON array to compact fields (`full_name`, `private`, `description`, `html_url`, `default_branch`, `updated_at`) so large accounts do not blow small local-model context windows.
3. **Tests (`tests/Guildhall.AgentHost.Tests/McpExternalProvidersTests.cs`):**
   - Extend `GitHubProviderProbesAndInvokesReadAndWriteTools` (or add a dedicated `[Fact]`) to verify `github_list_repositories` hits `/user/repos?...` without `owner`/`repo` and returns expected repo names.
4. **QC Note:** Remind users in docs/UI that **GitHub (Tier 1/3)** must also be enabled per Council Member under **Allowed MCP tools** in `CouncilMember`.

---

## 3. Phase 4 Core Scope (from `docs/masterplan/personal_ai_ecosystem_master_plan.md`)

1. **Local Project Registration & Isolated Git Worktree Management:**
   - Replace/upgrade the raw path bar with a formal local project/repository registry (working 100% offline without GitHub).
   - Manage isolated Git worktrees per coding task/session so edits never pollute the primary checkout until reviewed and approved.
2. **Tier 2 Workspace Writes (`filesystem-mcp` / coding tools) & Tool Execution Rollbacks:**
   - Enable `ToolSecurityTiers.Tier2_WorkspaceWrite` in `ToolPermissionGate` (removing the `tier2_planned_phase4` hard block once sandboxing + rollback are wired).
   - Implement transactional / compensating **Tool Execution Rollbacks** (git/file snapshots or patch reversal) for failed, aborted, or human-rejected edits and commands.
3. **Host-Enforced Command Sandboxing & Approval Gates:**
   - Bounded command execution inside the assigned project/worktree with `WorkspacePathGuard` + `ExfiltrationGuard` + explicit approval rules.
4. **Enforced Access Modes (Workspace + Member + CLI Harnesses):**
   - Wire workspace-wide override controls in `WorkspaceSettingsPanel.tsx` (`Always ask`, `Ask for unsafe actions`, `Unrestricted · danger`) to complement per-member policies.
   - Enable `Unrestricted / autopilot` under strict workspace/worktree containment.
   - Wire interactive approval callbacks across the split CLI harness adapters (`agy acp`, `codex app-server`, `claude` bidirectional `stream-json`, `agent`) so wrapped CLIs obey the exact same host `ToolPermissionGate`.
5. **Native Diff Preview, Review Loop & Project-Scoped Memory:**
   - Add a native diff preview & review/merge-approval loop in Session Tools (`apps/desktop/src/`).
   - Add **Project-scoped memory notes** alongside Global and Member memory scopes.

---

## 4. Non-Negotiables & Guardrails

- **Architecture & Standards:** Follow `AGENTS.md` and `docs/coding-standards.md` (`.NET 10` DI with `sealed` primary-constructor services, `ILazyLoader`, `Lazy<T>`, `React.lazy`, DPAPI secrets, zero unapproved NuGet/npm/Cargo packages, locked restores).
- **SQL Migration Split:** Keep `guildhall_runtime` strictly DDL-free; any additive schema migrations in Phase 4 must run via `Start-Dev.ps1` (pass `-ApproveMigrations` if `GRANT` statements are present).
- **Test Discipline:**
  - Never target `GuildhallDev` in automated tests; SQL integration tests must use isolated `GuildhallTest_<guid>` databases.
  - On Windows, run `dotnet test` with `BypassSandbox: true` and `-c Release` if a Debug host is running.
  - Always run `.cursor/agents/guildhall-ui-reviewer.md` before presenting UI changes, and report static review separately from live render checks.

---

## 5. Quick Start for Phase 4 Branch

```powershell
# From G:\repos\guildhall (clean main)
git checkout -b phase_four_implementation
pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -UseGpu -KeepModelWarmSeconds 900 -Replace
```
