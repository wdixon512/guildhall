---
id: durable-scheduler-and-five-tier-health
title: "Durable Cron Scheduler & Five-Tier Health Supervision"
area: infra-scheduling-health
triggers:
  - "/schedules"
  - "/schedules/runs/{taskRunId}/stream"
  - "/health/tiers"
  - "/health/ui-heartbeat"
  - DurableTaskSchedulerService
  - CronScheduleParser
  - FiveTierHealthSupervisor
  - ScheduledAutomation
  - DurableTaskRun
primary_files:
  - apps/desktop/src/ScheduledTasksPanel.tsx
  - apps/desktop/src/HealthTiersBadge.tsx
  - services/agent-host/Api/ScheduleEndpoints.cs
  - services/agent-host/Api/HealthEndpoints.cs
  - services/agent-host/Scheduling/CronScheduleParser.cs
  - services/agent-host/Scheduling/DurableTaskSchedulerService.cs
  - services/agent-host/Health/FiveTierHealthSupervisor.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 6/phase-6.md"
  - "docs/masterplan/phase 6/verification.md"
uncertainties:
  - boundary: "Live SQL Server, Ollama /api/ps VRAM, and remote MCP health state"
    location: "services/agent-host/Health/FiveTierHealthSupervisor.cs::FiveTierHealthSupervisor"
    note: "SQL Server GuildhallDev state, local Ollama daemon model/VRAM state, and external MCP server reachability depend on live runtime processes outside git."
    static_or_runtime: "runtime"
---

# Durable Cron Scheduler & Five-Tier Health Supervision

Runs SQL-backed cron automations across restarts/sleep gaps via `DurableTaskSchedulerService` and monitors Desktop UI, Agent Host, SQL Server, Ollama VRAM, and MCP servers in `FiveTierHealthSupervisor`.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/ScheduledTasksPanel.tsx` | `ScheduledTasksPanel.handleCreate` | `~L374-L450` | `POST /schedules`, `PATCH /schedules/{id}` (`ExpectedRevision`), `POST /schedules/{id}/run-now`, `GET /schedules/runs/{taskRunId}/stream` | Sends OCC `expectedRevision` on PATCH; aborts stale run-history/SSE fetches on unmount or schedule switch. |
| 2 | Agent Host / SQL | `services/agent-host/Api/ScheduleEndpoints.cs` | `ScheduleEndpoints.MapScheduleEndpoints` | `~L12-L421` | `/schedules/*`, `/schedules/{id}/run-now` (calls `scheduler.Wake()`), SSE `/schedules/runs/{taskRunId}/stream`; `ScheduledAutomations` & `DurableTaskRuns` | `LocalApiBoundary` bearer token; validates cron via `CronScheduleParser`; enforces OCC `Revision` on PATCH; rejects archived agents. |
| 3 | Scheduling | `services/agent-host/Scheduling/CronScheduleParser.cs` | `CronScheduleParser.TryParse` | `~L45-L145` | `CronSchedule.GetNextOccurrenceUtc`, `CronSchedule.EnumerateOccurrencesUtc` | Parses 5-field cron and presets (`@hourly`, `@daily`, `@weekly`, `@every_15m`, `@every_30m`); normalizes Sunday `7->0`; evaluates timezone-aware UTC slots. |
| 4 | Scheduling | `services/agent-host/Scheduling/DurableTaskSchedulerService.cs` | `DurableTaskSchedulerService.ExecuteAsync` | `~L30-L225` | 15s `PollInterval` + `_wake` `Channel<bool>(1)`, `ReconcileAsync`, `EvaluateDueSchedulesAsync` -> `DurableTaskRuns` | Reconciles stale `Running`/`NeedsApproval` rows on startup; enforces `MisfirePolicy` (`RunOnce` default, `Skip`, `CatchUpBounded` capped `1..10`); increments `Revision`. |
| 5 | Scheduling | `services/agent-host/Scheduling/DurableTaskSchedulerService.cs` | `DurableTaskSchedulerService.DispatchQueuedRunsAsync` | `~L227-L459` | `ChatRunner.MutationGate`, `Serializable` tx, `ContextBuilder.InspectAsync`, `ChatRunner.Start`, `SyncRunningTasksAsync` | Enforces `MaxBudgetTokens` before queuing `ChatRun`; records `NeedsApproval` when Tier 2/3 HITL gates pause execution; strips `<think>` and flags `<error>` tags. |
| 6 | Desktop / UI | `apps/desktop/src/HealthTiersBadge.tsx` | `HealthTiersBadge` | `~L10-L80` | 15s interval: `POST /health/ui-heartbeat` -> `GET /health/tiers`; Retry button -> `POST /health/tiers/{tierId}/recover` | Keeps background host aware of UI attachment; supports Escape and outside-click dismissal with focus return. |
| 7 | Agent Host | `services/agent-host/Api/HealthEndpoints.cs` | `HealthEndpoints.MapHealthEndpoints` | `~L7-L28` | `GET /health/tiers`, `POST /health/ui-heartbeat`, `POST /health/tiers/{tierId}/recover` | `LocalApiBoundary` bearer token; delegates snapshot, heartbeat timestamping, and forced tier recovery to `FiveTierHealthSupervisor`. |
| 8 | Agent Host | `services/agent-host/Health/FiveTierHealthSupervisor.cs` | `FiveTierHealthSupervisor.ExecuteAsync` | `~L118-L293` | 30s `MonitorInterval`, `IDatabaseReadiness`, `OllamaDiscovery` (`/api/ps` VRAM), `McpRegistry.ProbeServerAsync`, `_gate` lock | Marks Desktop UI `Idle` after 90s without heartbeat; bounds SQL/Ollama/MCP probes with `[15s, 30s, 60s]` exponential backoff unless `force=true`. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) (`BackgroundService` DI boundaries, `Serializable` concurrency safety, bounded polling, no fabricated health state).
- Enforce Phase 6 durable scheduling and 5-tier health contracts per [phase-6.md](<../../../docs/masterplan/phase 6/phase-6.md>) and [verification.md](<../../../docs/masterplan/phase 6/verification.md>).
- Index maintenance and workflow execution follow [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~Phase6SchedulerAndHealthTests"`
- `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~Phase6SqlIntegrationTests"`
- `npm.cmd --prefix apps/desktop test -- --run src/ScheduledTasksPanel.test.tsx src/HealthTiersBadge.test.tsx`
