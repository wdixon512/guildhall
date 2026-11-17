# Phase 6: Durable Background Tasks and Local Scheduled Automations

**Status:** Authorized / In Progress  
**Baseline:** `main` at `6ae2542` · Migration `20261117152949_Phase5SqlMcpAndRemoteGateways`

## Scope
1. **Independently Running Background Host Mode:** Detached `Guildhall.AgentHost` lifecycle (`Start-Dev.ps1 -Detached` / `Start-BackgroundHost.ps1`) with Tauri attach-to-running-host support and optional Windows Task Scheduler logon registration.
2. **SQL-Backed Durable Task Queue & Missed-Run/Sleep Handling:** `dbo.ScheduledAutomations` and `dbo.DurableTaskRuns` tables in `GuildhallDev` (with DML-only `guildhall_runtime` grants), built-in 5-field cron parser, sleep/wake gap detection (`Skip`, `RunOnce` default, `CatchUpBounded`), member permission policy enforcement (`NeedsApproval` pause on Tier 2/3 under `always_ask`/`ask_unsafe`), and cross-turn budget enforcement.
3. **5-Tier Health Monitoring & Auto-Recovery:** `FiveTierHealthSupervisor` (`BackgroundService`) supervising Tauri UI, ASP.NET Core Agent Host, SQL Server, Ollama (including `/api/ps` VRAM residency), and active MCP tool servers with bounded exponential-backoff recovery and `GET /health/tiers` + `POST /health/tiers/{tierId}/recover`.
4. **Local Cron / Schedule Manager (`Scheduled tasks` tab):** Live `ScheduledTasksPanel` replacing the Phase 1 placeholder, with schedule CRUD, pause/resume, run-now, audit history, and reconnectable SSE progress streams (`GET /schedules/runs/{taskRunId}/stream`), plus `HealthTiersBadge` in the header.

## Deferred Items
- `WorktreeDiffDrawer` redesign remains deferred per [product-roadmap-amendments.md](../product-roadmap-amendments.md).
