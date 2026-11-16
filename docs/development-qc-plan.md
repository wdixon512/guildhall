# Development and functional QC track

Status: approved companion work track, November 15, 2026. It preserves the master plan and Phase 1 product scope. Complete this track before declaring Phase 1 ready for owner QC or starting Phase 2. Work proceeds in the order below; each task remains bounded to its acceptance criteria.

## Why this is needed

Current automated tests cover Phase 0 status behavior but did not exercise the new chat workflow. Passing those tests did not establish functional chat. The owner received a checklist for features that were still incomplete.

Observed gaps in the current source:

- `App.tsx` reloads history once after Send, with no polling or stream subscription. The displayed Running state can remain forever after the database changes.
- `ChatRunner.cs` buffers the HTTP response/text until completion, has no progressive persistence, and treats EOF without a terminal marker as completion. It sends only instructions and the current prompt, not prior completed turns.
- History ordering uses the persisted role string, which places Assistant before User. The screenshot demonstrates the resulting reversed turn display.
- Cancellation, startup recovery, host admission control, export, and complete settings/history UI remain unfinished. An abandoned Running row can survive a host restart.
- Fixed desktop builds can lag the source. The existing watch launcher has not yet passed a real edit/restart/reconnect/cleanup test.

These are implementation gaps, not reasons to change the master architecture. Database state and a live Ollama request have not been inspected in this review; the screenshot alone does not establish whether its run is still executing on the host.

## Patterns adapted from ahprice

Reviewed `C:/Users/isagn/source/repos/AHPrice/AGENTS.md`, `.agents/skills/local-testing/SKILL.md`, `.agents/skills/gotchas/SKILL.md`, `.agents/memory/site-dev-server.md`, `docs/regression-tests.md`, and the development server entry points. Those documents are reference material; their GitHub, release, credential, and deployment rules do not govern Guildhall.

Adopt status-before-action, explicit checkout/build identity, an owned persistent dev session, isolated fixture data, short user-workflow browser checks, and shared gotchas tied to reproducible failures. Keep agent instructions short and route to a focused local-testing runbook. Retain our existing Luna/Sol task assignments. No new agent hierarchy, release infrastructure, external test service, or production data copy is needed.

## Recommended harness

Use one TypeScript Playwright harness, initially targeting the real Windows Tauri WebView2 through CDP. Drive actual buttons, inputs, text, downloads, and navigation through accessible selectors. Use the existing Tauri bootstrap/authentication and real ASP.NET API; do not mock native bootstrap or intercept successful chat responses in the acceptance suite.

Playwright documents WebView2 attachment using `chromium.connectOverCDP`. Prove compatibility with this app in a small spike before committing to the approach. Debugging flags apply only to the owned QC process, with a unique WebView profile and a checked loopback debug port. Never set global WebView environment variables. The debugging endpoint can inspect app secrets: exclude it from ordinary/final builds and reject non-loopback targets. No tokens in status JSON, URLs, traces, or screenshots.

If CDP cannot reliably exercise this app, document the failed spike and use Tauri's supported Windows WebDriver path instead. Do not install two automation stacks in advance. Browser-only Vite checks may supplement fast layout work but cannot certify native bootstrap, CSP, or save behavior. CDP also does not prove native OS dialogs; verify export by observing a real downloaded file, and document any native dialog requiring separate coverage.

Use a deterministic Ollama-compatible fixture HTTP server as a separate process. The real Ollama adapter must parse its discovery/chat NDJSON over HTTP. Scenarios include delayed chunks, stalled body, truncated stream, malformed record, HTTP failure, error inside HTTP 200, unsupported output, and terminal success. Fixture controls live on its isolated test endpoint and are never added to production API routes. Real Ollama remains a separate final smoke, with an already approved/selected model; no implicit downloads.

## Small implementation tasks

### Q01 — Honest status and source identity

Scope: launcher/status script, development metadata, runbook. Report absolute checkout, mode, PIDs, ports, source/build identity and dirty state, readiness, and schema state. Keep credentials/session tokens absent. Show a dev-only identity badge so stale embedded assets are obvious. A fixed binary needs a build manifest/hash; a Git SHA alone is insufficient in the currently untracked worktree.

Accept: identify a stale build, detect occupied ports without stopping their owner, and distinguish working source from embedded assets. Verify through script output and a native screenshot. The launcher may retain its session token only as a Windows user protected DPAPI value for the status command; never print or expose it in status JSON, URLs, traces, or screenshots, and remove it when the owned session exits.

### Q02 — Prove the rapid development loop

Scope: `Start-Dev.ps1`, `Common.ps1`, development configuration/docs. Keep Vite HMR and dotnet watch under one session owner. Enable noninteractive restart for unsupported hot edits; give initial builds enough bounded startup time. Preserve authentication across host restarts inside the same session. Display reconnecting state and recover reads after restart without resubmitting mutations. Do not hold a Running chat through development restarts without interrupted-run reconciliation.

Accept: agent launches the app, changes React text, sees it update, changes a C# behavior, sees it update/restart, and keeps the desktop open. Restore probe edits. Close session and prove all owned descendants/ports released, shared services untouched. A live session may be retained across turns with an explicit session handle/status.

### Q03 — Native automation spike

Scope: one project-local Playwright development dependency and a small Node/TypeScript driver. No browser download should be needed to attach to installed WebView2. Add launch/attach, semantic inspect, click, fill, wait, screenshot, and close operations usable by an agent through terminal scripts. Keep the authenticated connection inside the owned session.

Accept: launch the real Tauri window, read connection status, open chat, type a fixture prompt, capture a screenshot, and clean up without user clicks. A concrete failure selects the WebDriver fallback before building a suite. Sol reviews lifecycle/secrets; Luna implements the bounded driver after the spike contract is settled.

### Q04 — Isolated SQL and provider fixtures

Scope: QC launcher, fixture server, SQL fixture setup/cleanup. Each run owns a uniquely named GuildhallTest_* database, migrated under the migration identity and used through a restricted runtime test identity. Never use GuildhallDev for destructive fixture/reset workflows. Reuse existing isolated SQL test patterns; present any additional login/provisioning authority needed as concrete SQL before executing it. Do not weaken LocalConfiguration to allow arbitrary databases.

Accept: fresh run is repeatable, realistic permissions expose missing grants, two runs cannot collide, and cleanup touches only recorded owned resources. Failure retains sanitized artifacts and reports any leftover owned database. No resetting an owner's conversation or stopping shared Ollama/SQL.

### Q05 — First full chat regression and failure evidence

Scope: functional scenarios, missing production fixes within Phase 1, artifact collection. Start with a test that reproduces the current stale Generating display and wrong message order. It must fail on current code before fixing the flow.

Accept: using the actual UI, create/open chat, explicitly select a model, send once, observe user before assistant, observe committed partial output before completion, then Completed without another click. Reopen and confirm persistence. Selecting a conversation always scrolls to its latest message; new messages auto-follow only while the reader is already near the bottom. Assert exactly one run/provider request. Verify real run state through a read-only test observer independently of UI assertions.

Failures record scenario, step, source/build identity, elapsed time, screenshot, DOM summary, and sanitized error codes. Full network traces/HAR are off by default because they can contain bearer headers and conversation text. Use synthetic prompts in artifacts. Every wait has a deadline; an indefinite Generating display fails the test rather than hangs the suite.

### Q06 — Phase 1 functional gate

Add independent scenarios as the existing Phase 1 tasks are finished: instructions/model scopes; completed-turn context; cancel/complete races; restart recovery; lost POST/idempotency; provider outage/stall/truncation; SQL failure; history pagination; context/output limits; safe text; native JSON export. Use controlled barriers for race tests, not arbitrary long sleeps. Repeat checkpoint/recovery assertions against real SQL.

Accept: agent runs native deterministic workflows from a clean fixture session, fixes failures, and checks the fixed embedded build too. Run one separately recorded live-model smoke once authorized. Existing unit/contract tests remain agent-owned supporting checks. Owner QC is the final usability pass after these gates, with a tested launch command and only working feature steps.

### Q07 — Agent runbook and handoff

Scope: concise AGENTS routing and a focused local-QC skill/runbook plus a short gotchas file. Document exact start/status/attach/check/stop commands, artifact paths, test identities, known limitations, and cleanup. Keep automation scripts as the implementation; do not duplicate them into prompts.

Accept: a new coding session can identify and drive the correct application, run one scenario, inspect a failure, apply a small change, and verify it without asking the owner to rebuild/relaunch. Each bug fix adds a focused regression where useful. Small tasks go to Luna; integration/state/recovery and harness lifecycle review go to Sol.

## Planned interface and completion evidence

Proposed commands, not yet implemented:

```powershell
pwsh -NoProfile -File .\scripts\Start-Qc.ps1
pwsh -NoProfile -File .\scripts\Get-DevStatus.ps1 -Json
npm.cmd --prefix apps/desktop run qc -- --scenario chat-basic
pwsh -NoProfile -File .\scripts\Stop-Qc.ps1
```

Implementation may consolidate commands into one lifecycle script. It must expose a stable session handle and use ignored `.local/qc/<session>/` for ownership records and sanitized evidence. The owner receives a single tested development launch command and a short functional checklist; the agent runs automation.

Q01 status output now supports `-Json` for automation. The JSON includes the registered session/process states, listener ownership, source fingerprint, checkout, and working-tree state; it never includes session tokens or credentials. Default output remains the existing human-readable format.

Completion record: Q01–Q07 outcomes, dependency versions, exact commands run, failed/skipped checks, native vs browser scope, live vs fixture provider, cleanup proof, and the screenshot/artifact for chat progressing to completion. Phase 1 remains incomplete until its existing product acceptance criteria also pass. Phase 2 does not start merely because the harness passes.

## Primary references

Startup backend investigation (same session): an isolated Release host on owned loopback port 43129 reached authenticated health in 665 ms with SQL unconfigured; readiness returned the expected 503 in 24 ms. The owned process was stopped and the port released. This is a single backend probe, not a native launch benchmark. The launcher already waits for host health before creating the desktop; React renders the workspace before its async health/SQL/model reads. `index.html` contains an empty root until JavaScript loads, so a pre-React/WebView stall can remain white independently of backend readiness. The screenshot's exact delay was not reproduced. Frontend startup/loading recovery and native timing still need the frontend session; the existing app was left untouched.

Archive backend checkpoint (November 15, 2026, America/New_York): Q04/Q06 supporting checks passed 2 isolated SQL tests and 33 deterministic backend tests. Added a populated CouncilProfiles-to-CouncilMemberArchive upgrade check with exact pre-existing data comparison, repeat migration, readiness and restricted permissions. Existing SQL/API checks cover archive guards/history retention. [GuildhallDev application](council-member-archive-migration-approval.md) completed after explicit approval; runtime reads and the existing authenticated launcher report SQL ready, counts unchanged. Migration preview mode was verified with a pending migration; execution and subsequent no-op were verified. The first Debug build failed because the running host locked its executable; using Release output avoided a restart. Native UI and blank-window startup checks remain separate and incomplete; no phase gate is marked complete by these backend results.

Q02 ownership checkpoint (November 16, 2026, America/New_York): read-only inspection of the registered session `4e2d5c8387e748c289a1a21547e44408` found launcher PID `38620` and desktop PID `27812` exited while the Guildhall-owned `dotnet watch` PID `9528`, host PID `41008`, and Vite PID `17680` remained on ports `43120` and `1420`. `Start-Dev.ps1` already has a `finally` cleanup path using recursive process-tree termination, and `Get-DevStatus.ps1` exposes the orphaned ownership state. No script change was justified and no existing process was stopped. Q02 remains unverified until the confirmed Guildhall listeners are intentionally released and a fresh bounded normal-close run proves host/Vite/desktop cleanup and port release. Safe verification commands are the bounded `Start-Dev.ps1 -HostOnly -RunForSeconds 5` check followed by `Get-DevStatus.ps1` and a listener query, then the equivalent full development-session close check.

- Playwright WebView2: https://playwright.dev/docs/webview2
- Tauri WebDriver: https://v2.tauri.app/develop/tests/webdriver/

The master plan remains the product roadmap; this document supplies the development workflow and verification gate used across phases.
