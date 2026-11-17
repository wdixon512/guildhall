# Phase 6 Verification Record

**Branch / Worktree:** `phase_six_durable_tasks_plan`  
**Date:** November 17, 2026  
**Migration:** `20261117170659_Phase6DurableTasksAndSchedules` (additive `dbo.ScheduledAutomations` + `dbo.DurableTaskRuns` + DML-only `guildhall_runtime` grants)

---

## 1. Automated Verification (Run After Final Edits)

| Suite | Command | Result |
|---|---|---|
| Host Unit Tests (non-SQL) | `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-build --filter "Category!=SqlIntegration"` | **142 / 142 passed** (0 failed, 0 skipped) |
| Host SQL Integration Tests | `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-build --filter "Category=SqlIntegration"` | **8 / 8 passed** (0 failed, 0 skipped) |
| Desktop Frontend Unit Tests | `npm.cmd --prefix apps/desktop test -- --run` | **64 / 64 passed** across 14 test files |
| Desktop Frontend Typecheck & Build | `npm.cmd --prefix apps/desktop run build` (`tsc --noEmit && vite build`) | **Passed** |
| Tauri Rust Unit Tests | `cargo test --manifest-path apps/desktop/src-tauri/Cargo.toml --locked` | **2 / 2 passed** |

---

## 2. UI Review Status

- **Static Code & Accessibility Review:** Completed via `.cursor/agents/guildhall-ui-reviewer.md`; High & Medium findings resolved (`HealthTiersBadge` error alert + Escape/outside-click dismissal + narrow-viewport clamping, `ScheduledTasksPanel` CSS classes `.field-error`/`.muted`/`button.primary`/`.danger-button`, aborting stale `loadRuns`/stream requests + close buttons for history/stream, active-state styling on `.nav-rail>button[aria-pressed="true"]`, responsive `.schedule-form-row`, and `tabpanel` flex layout).
- **Live Render & Functional QC:** Pending owner interactive QC against `GuildhallDev`.

---

## 3. Manual QC Checklist

- [ ] Migration `20261117170659_Phase6DurableTasksAndSchedules` applied cleanly to `GuildhallDev`.
- [ ] Detached background host (`Start-BackgroundHost.ps1 -UseGpu -KeepModelWarmSeconds 900`) starts cleanly and `Start-Dev.ps1 -AttachBackground` attaches to it.
- [ ] `Scheduled tasks` tab creates, pauses/resumes, runs now, streams progress, and reconnects after closing/reopening the desktop window.
- [ ] 5-Tier Health popover (`5-Tier Healthy` / `Degraded`) reflects SQL, Ollama, and MCP server state and recovers on `Retry`.
