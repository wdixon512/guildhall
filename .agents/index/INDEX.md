# Guildhall Codebase Workflow Index (Tier 0 Router)

Centralized 3-tier navigation router ([SCHEMA.md](SCHEMA.md), [manifest.json](manifest.json), [Decision 0005](../../docs/decisions/0005-codebase-workflow-index.md)). Consult this router or `manifest.json` **before** running broad repo `Grep` scans or reading whole large files (`App.tsx`, `ChatEndpoints.cs`, `ChatRunner.cs`, `GuildhallDbContext.cs`, `ConnectionCatalogService.cs`, `ScheduledTasksPanel.tsx`).

## Lookup Protocol (<2,000 Tokens)

1. **Forward (by task, HTTP route, Tauri command, EF entity, or symbol):** Match **High-Signal Intent Triggers** or the router tables below -> read 1 Workflow Card (`workflows/<id>.md`), plus 1 Area Map (`areas/<area>.md`) only when module ownership or shared locks/entities matter -> call bounded `read_file(Path, StartLine, EndLine)` on each hop's `~Lstart-Lend` hint.
2. **Reverse (editing or reviewing a known file or symbol):** Search `"<repo-relative-path>"` in [manifest.json](manifest.json) (`file_to_workflows` / `symbol_to_workflows`) and open every listed Workflow Card before changing locks, schemas, API contracts, security guards, or symbol anchors.
3. **Drift & PR checks:** Run `python scripts/index_doc.py --dirty` after source edits, `python scripts/index_doc.py --write` after card edits, and `python scripts/index_doc.py --release-ready` before opening a PR (see [`codebase-index` skill](../skills/codebase-index/SKILL.md)).

## Area Routing Hints

- **[`host-security-lifecycle`](areas/host-security-lifecycle.md):** Tauri `bootstrap` IPC/CSP, `LocalApiBoundary`, DPAPI secrets, `EnvironmentScrubber`, `Start-Dev.ps1` / `Start-BackgroundHost.ps1`.
- **[`sql-persistence-migrations`](areas/sql-persistence-migrations.md):** `GuildhallDbContext` (16 entities, `ILazyLoader`, deferred `ChatAttachment`), `guildhall_runtime` DML split, `Verify-SqlBackupRestore.ps1`.
- **[`council-memory-context`](areas/council-memory-context.md):** `/agents` CRUD & archive/delete, `PreambleCacheService` (50-100 MB LRU + `FileSystemWatcher`), 3-scope `MemoryNotes`, `ContextBuilder` & `/vram`.
- **[`chat-runtime-engines`](areas/chat-runtime-engines.md):** `POST /conversations/{id}/messages` (`queue`/`send_now`/OCC/idempotency), `ChatRunner`, Ollama/Cloud SSE/5 CLI harnesses, `ConnectionCatalogService`.
- **[`mcp-tools-security-gate`](areas/mcp-tools-security-gate.md):** 6-round tool loop, `ToolPermissionGate` (`Tier0-3` HITL), `ExfiltrationGuard`, `WorkspacePathGuard`, `McpRegistry` & Tier-5 circuit breaker.
- **[`projects-worktrees-rollback`](areas/projects-worktrees-rollback.md):** `ProjectWorkspaceService` (`guildhall/<shortId>` worktrees, diff/merge/discard), `ToolRollbackService` (`FileBackup`/`GitTree`), access policy.
- **[`infra-scheduling-health`](areas/infra-scheduling-health.md):** `SqlServerMcpProvider` + `SqlReadOnlyGuard`, `POST /mcp/gateway`, `PrivateNetworkGuard`, `DurableTaskSchedulerService` SSE, `FiveTierHealthSupervisor`.

## High-Signal Intent Triggers

| Task / Invariant Trap / Route / Symbol | Workflow Card(s) |
| :--- | :--- |
| Loopback host auth, Origin check, `GUILDHALL_SESSION_TOKEN`, Tauri CSP/capabilities | [`local-api-boundary-bootstrap`](workflows/local-api-boundary-bootstrap.md) |
| Multi-worktree `-AutoPort`/`-Replace`/`-WindowTag`, `%LOCALAPPDATA%\Guildhall\dev\sessions`, Task Scheduler host | [`multi-instance-dev-orchestration`](workflows/multi-instance-dev-orchestration.md), [`detached-background-host`](workflows/detached-background-host.md) |
| DPAPI `*.dpapi` secrets, `EnvironmentScrubber` child-process stripping, `/connections/{id}/api-key` | [`dpapi-secrets-and-env-scrubbing`](workflows/dpapi-secrets-and-env-scrubbing.md) |
| `ILazyLoader` (no proxies), deferred `ChatAttachments` `varbinary(max)`, `IDbContextFactory`, `DatabaseReadiness` | [`ef-core-schema-and-lazy-loading`](workflows/ef-core-schema-and-lazy-loading.md) |
| `guildhall_runtime` DDL-free split, `Invoke-AutoDatabaseMigration` gate, `COPY_ONLY` restore verify | [`sql-provisioning-and-migrations`](workflows/sql-provisioning-and-migrations.md), [`sql-backup-restore-verification`](workflows/sql-backup-restore-verification.md) |
| `PreambleCacheService` 50-100 MB LRU cap & hot-reload, `MemoryNotes`, `CrossTurnBudgetOptions`, `/vram` | [`preamble-cache-hot-reload`](workflows/preamble-cache-hot-reload.md), [`three-scope-memory-and-context-budget`](workflows/three-scope-memory-and-context-budget.md) |
| Council member profile/portrait/archive/delete & dirty-draft guard | [`council-member-lifecycle`](workflows/council-member-lifecycle.md) |
| `ChatRunner.MutationGate`, `clientRequestId` idempotency, OCC `Revision`, `/promote`, `ChatRecoveryService` | [`chat-queue-send-now-and-recovery`](workflows/chat-queue-send-now-and-recovery.md) |
| Ollama `<think>`/`/api/show`, OpenAI/Gemini/Anthropic SSE, 5 CLI harnesses, `ConnectionCatalogService` | [`backing-engine-streaming-adapters`](workflows/backing-engine-streaming-adapters.md), [`connections-catalog-and-discovery`](workflows/connections-catalog-and-discovery.md) |
| `ToolPermissionGate` (`Tier3` always HITL), `ExfiltrationGuard`, `WorkspacePathGuard`, `McpHealthSupervisor` | [`tool-loop-approval-and-exfiltration-gate`](workflows/tool-loop-approval-and-exfiltration-gate.md), [`builtin-workspace-and-external-mcp`](workflows/builtin-workspace-and-external-mcp.md), [`mcp-registry-health-and-jsonrpc`](workflows/mcp-registry-health-and-jsonrpc.md) |
| `ProjectWorkspaceService` worktree diff/`--no-ff` merge/discard, `ToolRollbackService` (`FileBackup`/`GitTree`) | [`project-registration-and-worktree-isolation`](workflows/project-registration-and-worktree-isolation.md), [`tool-rollback-and-workspace-policies`](workflows/tool-rollback-and-workspace-policies.md) |
| `SqlReadOnlyGuard` (blocks `GuildhallDev`), `POST /mcp/gateway`, WireGuard/Tailscale `PrivateNetworkGuard` | [`readonly-sql-mcp-and-gateway`](workflows/readonly-sql-mcp-and-gateway.md), [`private-networking-and-remote-mcp`](workflows/private-networking-and-remote-mcp.md) |
| `DurableTaskSchedulerService` misfire/budget, SSE `/schedules/runs/{taskRunId}/stream`, `FiveTierHealthSupervisor` | [`durable-scheduler-and-five-tier-health`](workflows/durable-scheduler-and-five-tier-health.md) |

## Registered Areas & Workflows

<!-- index-router-start -->
### Domain Areas (7)

| Area | Summary | Workflows |
| :--- | :--- | :--- |
| [`chat-runtime-engines`](areas/chat-runtime-engines.md) | Chat queuing, send-now interruption, streaming checkpoints, recovery, and local Ollama, Cloud SSE, and CLI harness engine adapters plus connection discovery. | 3 |
| [`council-memory-context`](areas/council-memory-context.md) | Council member profiles and lifecycle, preamble cache hot-reload, three-scope memory notes, and cross-turn context budgets with live VRAM. | 3 |
| [`host-security-lifecycle`](areas/host-security-lifecycle.md) | Loopback API boundary, Tauri IPC isolation, DPAPI credential storage, env scrubbing, multi-instance dev sessions, and background host lifecycle. | 4 |
| [`infra-scheduling-health`](areas/infra-scheduling-health.md) | Read-only SQL MCP and gateway, WireGuard/Tailscale private networking, SQL-backed durable cron scheduler, and 5-tier health auto-recovery. | 3 |
| [`mcp-tools-security-gate`](areas/mcp-tools-security-gate.md) | Multi-round tool loop, Tier 0-3 permission gating, exfiltration redaction, workspace/external MCP providers, Tier-5 health, and JSON-RPC transport. | 3 |
| [`projects-worktrees-rollback`](areas/projects-worktrees-rollback.md) | Local project registration, isolated Git worktrees, diff review/merge/discard, Tier 2 tool rollbacks, and workspace access/worktree policies. | 2 |
| [`sql-persistence-migrations`](areas/sql-persistence-migrations.md) | EF Core 10 schema (16 entities, ILazyLoader, OCC), DPAPI/Windows migration split, and backup/restore verification. | 3 |

### Workflows (21)

| Workflow | Area | Primary Triggers |
| :--- | :--- | :--- |
| [`backing-engine-streaming-adapters`](workflows/backing-engine-streaming-adapters.md) | [`chat-runtime-engines`](areas/chat-runtime-engines.md) | `IBackingEngine`, `BackingEngineRegistry`, `OllamaBackingEngine`, `OpenAiCloudEngine` |
| [`builtin-workspace-and-external-mcp`](workflows/builtin-workspace-and-external-mcp.md) | [`mcp-tools-security-gate`](areas/mcp-tools-security-gate.md) | `WorkspacePathGuard`, `filesystem-mcp`, `git-mcp`, `workspace-exec-mcp` |
| [`chat-queue-send-now-and-recovery`](workflows/chat-queue-send-now-and-recovery.md) | [`chat-runtime-engines`](areas/chat-runtime-engines.md) | `POST /conversations/{id}/messages`, `POST /runs/{id}/promote`, `POST /runs/{id}/cancel`, `GET /attachments/{id}` |
| [`connections-catalog-and-discovery`](workflows/connections-catalog-and-discovery.md) | [`chat-runtime-engines`](areas/chat-runtime-engines.md) | `GET /connections`, `POST /connections/{id}/probe`, `PUT /connections/{id}/api-key`, `POST /connections/{id}/sign-in` |
| [`council-member-lifecycle`](workflows/council-member-lifecycle.md) | [`council-memory-context`](areas/council-memory-context.md) | `/agents`, `/agents/{id}/lifecycle`, `CouncilSidebar`, `CouncilMember` |
| [`detached-background-host`](workflows/detached-background-host.md) | [`host-security-lifecycle`](areas/host-security-lifecycle.md) | `Start-BackgroundHost.ps1`, `AttachBackground`, `background-host.json`, `GuildhallAgentHostBackground` |
| [`dpapi-secrets-and-env-scrubbing`](workflows/dpapi-secrets-and-env-scrubbing.md) | [`host-security-lifecycle`](areas/host-security-lifecycle.md) | `DpapiCredentialStore`, `EnvironmentScrubber.BuildScrubbedEnvironment`, `PUT /connections/{id}/api-key`, `DELETE /connections/{id}/api-key` |
| [`durable-scheduler-and-five-tier-health`](workflows/durable-scheduler-and-five-tier-health.md) | [`infra-scheduling-health`](areas/infra-scheduling-health.md) | `/schedules`, `/schedules/runs/{taskRunId}/stream`, `/health/tiers`, `/health/ui-heartbeat` |
| [`ef-core-schema-and-lazy-loading`](workflows/ef-core-schema-and-lazy-loading.md) | [`sql-persistence-migrations`](areas/sql-persistence-migrations.md) | `GuildhallDbContext`, `IDbContextFactory`, `ILazyLoader`, `ChatAttachment` |
| [`local-api-boundary-bootstrap`](workflows/local-api-boundary-bootstrap.md) | [`host-security-lifecycle`](areas/host-security-lifecycle.md) | `bootstrap`, `LocalApiBoundary.UseLocalBoundary`, `LocalConfiguration.LoopbackUri`, `GUILDHALL_SESSION_TOKEN` |
| [`mcp-registry-health-and-jsonrpc`](workflows/mcp-registry-health-and-jsonrpc.md) | [`mcp-tools-security-gate`](areas/mcp-tools-security-gate.md) | `McpRegistry`, `McpHealthSupervisor`, `McpJsonRpcClient`, `GET /mcp/tools` |
| [`multi-instance-dev-orchestration`](workflows/multi-instance-dev-orchestration.md) | [`host-security-lifecycle`](areas/host-security-lifecycle.md) | `Start-Dev.ps1`, `Get-DevStatus.ps1`, `Stop-Dev.ps1`, `New-ChildEnvironment` |
| [`preamble-cache-hot-reload`](workflows/preamble-cache-hot-reload.md) | [`council-memory-context`](areas/council-memory-context.md) | `/preambles`, `PreambleCacheService`, `PreambleCacheOptions`, `global-guidelines.md` |
| [`private-networking-and-remote-mcp`](workflows/private-networking-and-remote-mcp.md) | [`infra-scheduling-health`](areas/infra-scheduling-health.md) | `/infrastructure/network-status`, `/mcp/servers`, `PrivateNetworkGuard`, `PrivateNetworkProbeService` |
| [`project-registration-and-worktree-isolation`](workflows/project-registration-and-worktree-isolation.md) | [`projects-worktrees-rollback`](areas/projects-worktrees-rollback.md) | `ProjectWorkspaceBar`, `pick_workspace_folder`, `POST /projects`, `POST /conversations/{conversationId}/worktree` |
| [`readonly-sql-mcp-and-gateway`](workflows/readonly-sql-mcp-and-gateway.md) | [`infra-scheduling-health`](areas/infra-scheduling-health.md) | `/mcp/sql/profiles`, `POST /mcp/gateway`, `sql-server-mcp`, `sql_query` |
| [`sql-backup-restore-verification`](workflows/sql-backup-restore-verification.md) | [`sql-persistence-migrations`](areas/sql-persistence-migrations.md) | `Verify-SqlBackupRestore.ps1`, `SqlBackupRestoreTests`, `GuildhallRestoreVerify_`, `COPY_ONLY` |
| [`sql-provisioning-and-migrations`](workflows/sql-provisioning-and-migrations.md) | [`sql-persistence-migrations`](areas/sql-persistence-migrations.md) | `Provision-Database.ps1`, `Migrate-Database.ps1`, `Invoke-AutoDatabaseMigration`, `Get-LocalSqlConnection` |
| [`three-scope-memory-and-context-budget`](workflows/three-scope-memory-and-context-budget.md) | [`council-memory-context`](areas/council-memory-context.md) | `/memory-notes`, `/guildhall-behavior`, `/conversations/{id}/context`, `/vram` |
| [`tool-loop-approval-and-exfiltration-gate`](workflows/tool-loop-approval-and-exfiltration-gate.md) | [`mcp-tools-security-gate`](areas/mcp-tools-security-gate.md) | `MaxToolRounds`, `pendingApprovals`, `ToolPermissionGate.Evaluate`, `ExfiltrationGuard` |
| [`tool-rollback-and-workspace-policies`](workflows/tool-rollback-and-workspace-policies.md) | [`projects-worktrees-rollback`](areas/projects-worktrees-rollback.md) | `ToolRollbackService`, `CapturePreStateAsync`, `RollbackEventAsync`, `POST /runs/{runId}/events/{eventId}/rollback` |
<!-- index-router-end -->
