# Phase 7 Handoff: Cross-Member Context Handoff, Delegation Dependency Graphs & Multi-Agent Coordination

**Prepared:** November 17, 2026  
**Source Branch:** `phase_six_durable_tasks_plan` (merged to `main`)  
**Database Baseline:** `GuildhallDev` at migration `20261117170659_Phase6DurableTasksAndSchedules` (15 migrations applied)

---

## 1. Phase 6 Baseline & Verified State

- **Automated Verification (all green before merge):**
  - Host Unit Tests (`Category!=SqlIntegration`): **142 / 142 passed** (Debug & Release verified)
  - Host SQL Integration Tests (`Category=SqlIntegration`): **8 / 8 passed**
  - Desktop Vitest: **64 / 64 passed** across 14 test files
  - Desktop Typecheck & Build (`tsc --noEmit && vite build`): **Passed**
  - Tauri Rust Unit Tests (`cargo test --locked`): **2 / 2 passed**
- **Implemented in Phase 6:**
  - **Pre-Phase 6 Cleanup:** Updated `AGENTS.md`, `.agents/roster.md`, `docs/coding-standards.md`, `docs/masterplan/README.md`; converted `ChatRecovery` to DI `ChatRecoveryService`; replaced direct `DateTime.UtcNow` calls with `TimeProvider`; added `.AsNoTracking()` / `.Select()` projections; removed unreferenced `/assistant` & `/runs/{id}` endpoints; extracted `SessionToolsPane.tsx` and `requestJson<T>`.
  - **Detached Background Host Mode:** `scripts/startup/Start-BackgroundHost.ps1` (`-UseGpu`, `-Status`, `-Stop`, `-RegisterLogonTask`) + `Start-Dev.ps1 -AttachBackground`.
  - **SQL-Backed Durable Tasks & Cron:** `dbo.ScheduledAutomations` + `dbo.DurableTaskRuns` (DML-only `guildhall_runtime` grants), `CronScheduleParser`, `DurableTaskSchedulerService` (sleep/wake misfire policies `RunOnce`/`Skip`/`CatchUpBounded`, cross-turn budget & member permission policy enforcement, `<think>` stripping, and `<error>` failure detection).
  - **5-Tier Health Supervision:** `FiveTierHealthSupervisor` + `GET /health/tiers`, `POST /health/ui-heartbeat`, `POST /health/tiers/{tierId}/recover`, and `HealthTiersBadge.tsx`.
  - **Scheduled Tasks UI & Polish:** `ScheduledTasksPanel.tsx` (structured cadence builder for Every 15m/30m, Hourly, Daily, Weekly, Monthly, Custom cron; pause/resume, run-now, history, reconnectable SSE stream), unified `play` Start/Test icon, right-aligned red `danger-button` delete actions, and `StreamingMarkdown` callout tag formatting (`<note>`, `<error>`, `<warning>`, etc.).

---

## 2. Phase 7 Scope (`personal_ai_ecosystem_master_plan.md` & `product-roadmap-amendments.md`)

1. **Conversation Reassignment ("Assign to guild member…") & Detached Session Memory Transplant:**
   - Enable the disabled `Assign to guild member…` action in `apps/desktop/src/CouncilSidebar.tsx` (`L221-222`).
   - Build interactive handoff/edit mode to transfer a conversation or curated context packet (full transcript, reviewed summary, or pinned items) from one Council Member to another across different backing engines (Ollama, Cloud API, CLI Harness) with immutable SQL provenance.
   - Support detaching a message, scheduled run output, or curated memory slice from one session and pasting/attaching it directly into another active session without switching away.
2. **Bounded Multi-Agent Delegation & Delegation Dependency Graphs:**
   - Child-run delegation protocol (supervisor-to-specialist) with SQL-backed **Delegation Dependency Graphs** (`ParentRunId`, dependency edges, cycle/depth guards).
   - Per-agent tool and **Cross-Turn Budgets**, run graph visualization in `SessionToolsPane` (`Plan` / `Context` or dedicated graph view), and cascading cancellation/rollback propagation (`ToolRollbackService`).
3. **Post-Phase Prompt Engineering & Tool-Grounding Pass (if scheduled with Phase 7):**
   - Tune Council Member preambles (e.g., `Krug` / Developer) and `global-guidelines.md` for smaller local models (`qwen3:4b`) to enforce strict tool-failure reporting without fabricated outputs.

---

## 3. Architectural & Process Guardrails for Phase 7

- **Standards:** Follow [AGENTS.md](../../AGENTS.md) and [docs/coding-standards.md](../../coding-standards.md) (`sealed` DI services, `TimeProvider`, `IDbContextFactory<GuildhallDbContext>`, `ILazyLoader`, `.AsNoTracking()` + `.Select()`, `React.lazy` + `<Suspense>`, zero unapproved packages).
- **DDL-Free Runtime:** Any new Phase 7 SQL tables must include `GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.<Table> TO [guildhall_runtime]` in the EF Core migration.
- **Deferred (Do Not Pull Into Phase 7 Unless Requested):** `WorktreeDiffDrawer` redesign remains deferred per [product-roadmap-amendments.md](../product-roadmap-amendments.md).
- **Testing & Startup Reminder:** Always run `dotnet test` / `npm test` / `cargo test --locked` after final edits, and bake `cd <worktree-path>` into `Start-Dev.ps1` / `Start-BackgroundHost.ps1` commands.
