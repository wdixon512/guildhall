---
area: infra-scheduling-health
title: "Infrastructure, Private Networking, Durable Scheduling & Health"
summary: "Read-only SQL MCP and gateway, WireGuard/Tailscale private networking, SQL-backed durable cron scheduler, and 5-tier health auto-recovery."
primary_files:
  - apps/desktop/src/PrivateInfrastructurePanel.tsx
  - apps/desktop/src/ScheduledTasksPanel.tsx
  - apps/desktop/src/HealthTiersBadge.tsx
  - services/agent-host/Api/SqlMcpEndpoints.cs
  - services/agent-host/Api/McpGatewayEndpoints.cs
  - services/agent-host/Api/McpEndpoints.cs
  - services/agent-host/Api/ScheduleEndpoints.cs
  - services/agent-host/Api/HealthEndpoints.cs
  - services/agent-host/Mcp/McpRegistry.cs
  - services/agent-host/Mcp/McpJsonRpcClient.cs
  - services/agent-host/Mcp/PrivateNetworkProbeService.cs
  - services/agent-host/Mcp/Providers/SqlReadOnlyGuard.cs
  - services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs
  - services/agent-host/Security/PrivateNetworkGuard.cs
  - services/agent-host/Scheduling/CronScheduleParser.cs
  - services/agent-host/Scheduling/DurableTaskSchedulerService.cs
  - services/agent-host/Health/FiveTierHealthSupervisor.cs
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 5/private-networking.md"
  - "docs/masterplan/phase 5/verification.md"
  - "docs/masterplan/phase 6/phase-6.md"
  - "docs/masterplan/phase 6/verification.md"
---

# Infrastructure, Private Networking, Durable Scheduling & Health

Covers read-only SQL Server MCP profiles and gateway exposure, WireGuard/Tailscale private network posture and remote Streamable HTTP MCP servers, SQL-backed durable cron scheduling, and 5-tier health supervision.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `apps/desktop/src/PrivateInfrastructurePanel.tsx` | Connections tab UI for read-only SQL MCP profiles, tunnel/port posture cards, and remote Streamable HTTP MCP servers | React component state, `AbortController` in `refreshAll` | `PrivateInfrastructurePanel`, `refreshAll`, `handleCreateSqlProfile`, `handleCreateRemoteMcp` |
| `apps/desktop/src/ScheduledTasksPanel.tsx` | Scheduled tasks tab UI for cron cadence CRUD, pause/resume, run-now, run history, and live SSE progress modal | `runsAbort` / `streamAbort` refs, `selectedScheduleIdRef` | `ScheduledTasksPanel`, `loadRuns`, `attachRunStream`, `handleCreate`, `handleTogglePause`, `handleRunNow` |
| `apps/desktop/src/HealthTiersBadge.tsx` | Header 5-tier health badge, 15s UI heartbeat + snapshot sync, and per-tier Retry recovery popover | 15s polling interval, `AbortController` | `HealthTiersBadge`, `handleRecover` |
| `services/agent-host/Api/SqlMcpEndpoints.cs` | HTTP CRUD & probe endpoints for read-only SQL Server MCP profiles | `GuildhallDbContext.SqlMcpProfiles`, DPAPI key `sql-mcp-{id:N}`, `Revision` concurrency token (no PATCH/OCC delete check) | `MapSqlMcpEndpoints` (`/mcp/sql/profiles`, `/mcp/sql/profiles/{id}/probe`) |
| `services/agent-host/Api/McpGatewayEndpoints.cs` | Authenticated JSON-RPC 2.0 MCP gateway endpoint exposing only `sql-server-mcp` Tier 1 tools | `GatewayAllowedServerIds` (`sql-server-mcp` only) | `MapMcpGatewayEndpoints` (`POST /mcp/gateway`) |
| `services/agent-host/Api/McpEndpoints.cs` | HTTP endpoints for custom MCP servers, DPAPI bearer tokens, server probes, and private network posture | `GuildhallDbContext.McpServerRegistrations`, DPAPI key `mcp:{id}`, OCC `Revision` | `MapMcpEndpoints` (`/mcp/servers`, `/mcp/servers/{id}/token`, `/mcp/servers/{id}/probe`, `/infrastructure/network-status`) |
| `services/agent-host/Api/ScheduleEndpoints.cs` | HTTP CRUD, run-now, run history, and SSE streaming endpoints for scheduled automations | `GuildhallDbContext.ScheduledAutomations` & `DurableTaskRuns`, OCC `Revision` | `MapScheduleEndpoints` (`/schedules`, `/schedules/{id}/run-now`, `/schedules/{id}/runs`, `/schedules/runs/{taskRunId}/stream`) |
| `services/agent-host/Api/HealthEndpoints.cs` | HTTP endpoints for 5-tier health snapshots, UI heartbeats, and manual tier recovery probes | Delegates to `FiveTierHealthSupervisor` | `MapHealthEndpoints` (`/health/tiers`, `/health/ui-heartbeat`, `/health/tiers/{tierId}/recover`) |
| `services/agent-host/Mcp/McpRegistry.cs` | Unified catalog, probe, and invocation router across built-in providers and custom MCP servers | `_externalToolsCache` `ConcurrentDictionary`, `McpHealthSupervisor` Tier 5 circuit | `ListServersAsync`, `GetEnabledToolsForMemberAsync`, `ProbeServerAsync`, `InvokeToolAsync` |
| `services/agent-host/Mcp/McpJsonRpcClient.cs` | Streamable HTTP (JSON + SSE) and scrubbed stdio JSON-RPC 2.0 client for custom MCP servers | `_sessionIds` `ConcurrentDictionary` (`Mcp-Session-Id`), DPAPI key `mcp:{id}` | `ListToolsAsync`, `CallToolAsync`, `SendHttpAsync`, `SendStdioAsync` |
| `services/agent-host/Mcp/PrivateNetworkProbeService.cs` | Best-effort inspection of active WireGuard/Tailscale NICs and TCP 1433/11434 listener exposure posture | Stateless OS `NetworkInterface` & `IPGlobalProperties` probe | `Inspect`, `CheckTcpPort` |
| `services/agent-host/Mcp/Providers/SqlReadOnlyGuard.cs` | Static SQL comment-stripper & regex/token guard blocking `GuildhallDev`, multi-statements, non-SELECT/WITH, DDL/DML/EXEC, `xp_`/`sp_`, and security views | `BlockedKeywords`, `BlockedDatabases` (`GuildhallDev`), `IdentifierRegex` | `ValidateDatabaseTarget`, `ValidateQuery`, `StripCommentsAndLiterals` |
| `services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs` | Built-in Tier 1 `sql-server-mcp` provider with schema/object allowlists, `READ UNCOMMITTED` + `ROWCOUNT`, and DPAPI `sql_login` auth | `GuildhallDbContext.SqlMcpProfiles` (`AsNoTracking`), DPAPI `sql-mcp-{profileId:N}`, `Pooling=false` | `InvokeAsync`, `ProbeProfileAsync`, `ExecuteQueryAsync`, `BuildConnectionStringAsync`, `GetCredentialKey` |
| `services/agent-host/Security/PrivateNetworkGuard.cs` | URI & IP guard allowing HTTPS, loopback HTTP, or opted-in RFC1918 / `100.64.0.0/10` / `.ts.net` / `fc00::/7` HTTP | Stateless guard | `ValidateMcpEndpoint`, `IsPrivateTunnelHost`, `IsPrivateTunnelIp` |
| `services/agent-host/Scheduling/CronScheduleParser.cs` | 5-field cron & preset parser with timezone-aware next-occurrence and bounded slot enumeration | Immutable `CronSchedule` bitmasks | `CronScheduleParser.TryParse`, `CronSchedule.GetNextOccurrenceUtc`, `CronSchedule.EnumerateOccurrencesUtc` |
| `services/agent-host/Scheduling/DurableTaskSchedulerService.cs` | `BackgroundService` polling every 15s + `_wake` channel for restart reconciliation, misfire evaluation, budget/gate dispatch, and status sync | `_wake` `Channel<bool>(1)`, `ChatRunner.MutationGate`, `Serializable` tx | `ExecuteAsync`, `ReconcileAsync`, `TickAsync`, `Wake`, `ResolveTimezone` |
| `services/agent-host/Health/FiveTierHealthSupervisor.cs` | `BackgroundService` supervising Desktop UI, Agent Host, SQL Server, Ollama (+ `/api/ps` VRAM), and MCP servers with exponential backoff | `_gate` lock protecting `_lastUiHeartbeatUtc`, `_lastVram`, `_sqlState`, `_ollamaState`, `_mcpState`; `BackoffSteps` `[15s, 30s, 60s]` | `ExecuteAsync`, `GetSnapshot`, `RecordUiHeartbeat`, `ProbeAllAsync`, `RecoverTierAsync` |

## Workflows

- [`readonly-sql-mcp-and-gateway`](../workflows/readonly-sql-mcp-and-gateway.md) — Manages read-only SQL Server MCP profiles and executes Tier 1 read-only queries via `sql-server-mcp` or `POST /mcp/gateway` while blocking `GuildhallDev` and mutating SQL.
- [`private-networking-and-remote-mcp`](../workflows/private-networking-and-remote-mcp.md) — Probes WireGuard/Tailscale interfaces and port 1433/11434 exposure posture, and registers/probes remote Streamable HTTP MCP servers with `PrivateNetworkGuard` and DPAPI bearer tokens.
- [`durable-scheduler-and-five-tier-health`](../workflows/durable-scheduler-and-five-tier-health.md) — Runs SQL-backed cron automations across restarts/sleep gaps via `DurableTaskSchedulerService` and monitors Desktop UI, Agent Host, SQL Server, Ollama VRAM, and MCP servers in `FiveTierHealthSupervisor`.

## Contracts & References

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards: [coding-standards.md](../../../docs/coding-standards.md)
- Phase 5 specs: [private-networking.md](<../../../docs/masterplan/phase 5/private-networking.md>), [verification.md](<../../../docs/masterplan/phase 5/verification.md>)
- Phase 6 specs: [phase-6.md](<../../../docs/masterplan/phase 6/phase-6.md>), [verification.md](<../../../docs/masterplan/phase 6/verification.md>)
