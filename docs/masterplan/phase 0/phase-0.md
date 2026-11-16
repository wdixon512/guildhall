# Phase 0 implementation plan — locally implemented and verified

The nine presented design choices are accepted in [the decision log](../../decisions/0001-phase-0.md). The user subsequently authorized building Phase 0 and explicitly approved the concrete SQL, Node, and Ollama setup actions. See [the verification record](verification.md) for actual implementation and check results; the task descriptions below retain the agreed acceptance contract.

## Outcome and scope

A Windows Tauri 2 window shows whether its authenticated local ASP.NET Core host is reachable, whether the dedicated SQL Server database has the expected schema, and which locally installed Ollama models are discoverable. No external account is required.

Included: repository conventions, buildable shell/host, local API authentication, SQL Server migration pipeline, model discovery, deterministic tests, and development startup/shutdown.

Excluded: chat, inference/streaming, agent registry, conversation tables, provider switching, cloud APIs, MCP, command execution tools, permission UI, background jobs, remote access, packaging/sidecars, deployment, and system/network changes. The source master plan's section 16 is not the Phase 0 assignment.

## Task execution contract

Each task should fit one focused coding-model session and one reviewable change. Read `AGENTS.md`, this task, its API contract, and only relevant files. Do not load the entire master plan for every task. Give the model explicit allowed files, fixtures, acceptance criteria, and commands. If a task crosses more boundaries than expected, split it before coding.

Use a lightweight model by default. A stronger reasoning model is warranted for P03's authentication/bootstrap design review and P05's migration/permission/failure review, then for unresolved integration faults that cross process or trust boundaries. Routine scaffolding, mappings, UI, documentation, and test fixtures do not need it. Escalate with a small reproducer and failing output rather than repeatedly enlarging context. No parallel agents are required.

Commands run from the repository root in PowerShell; use `npm.cmd` to avoid changing execution policy. The verification record, rather than these task descriptions, determines which checks have actually passed.

### P00 — Review decisions and verify prerequisites

- Files: `docs/decisions/0001-phase-0.md`, prerequisite notes in this document; no application files.
- Depends on: user review of this proposal.
- Work: follow the recorded design choices. SQL version/authentication and existing nvm have been checked. Select an exact compatible Node release for the approved system-upgrade approach through nvm; review the concrete install/switch action before execution. Verify Windows SDK/WebView2 and locate Ollama. Read-only inspection first; present concrete prerequisite changes for approval if needed.
- Acceptance: each prerequisite is pass/fail/unknown with evidence; any missing installation is explicitly unresolved. Select an exact compatible toolchain before scaffolding. Identify the intended database instance without querying unrelated user data.
- Verify: `node --version`, `npm.cmd --version`, `rustc --version`, `cargo --version`, `dotnet --list-sdks`, `Get-Command sqlcmd`, and a bounded read-only Ollama `/api/tags` probe. SQL connection/version verification uses only the chosen instance and authorized identity.
- Model: lightweight; user decisions cannot be filled in by a stronger model.

### P01 — Repository and toolchain baseline

- Files: `.gitignore`, `.editorconfig`, `global.json`, solution, README setup section, local tool manifest and package lockfiles as projects are introduced.
- Depends on: P00; this task must not create host/UI feature code.
- Work: initialize local Git, pin the verified SDK and local `dotnet-ef`, and ignore build outputs, `.env*` secrets, local state, database backups, and logs. Permit sanitized example configuration. No remote repository setup.
- Acceptance: source paths and naming match the proposal; restore prerequisites and commands are documented; secret/local-state examples are ignored.
- Verify: `git status --short`, `git check-ignore .env.local .local/session.json`, `dotnet --version`, `dotnet tool restore`.
- Model: lightweight.

### P02 — Loopback host and liveness

- Files: host project, `Api/Health*`, configuration, backend test project, solution entry.
- Depends on: P01.
- Work: build the smallest ASP.NET Core host on `127.0.0.1:43120`; establish explicit DTOs and a test-host factory. No database/provider dependency at startup. It is not yet launched by the desktop.
- Acceptance: `/health` returns the agreed DTO when dependencies are absent; actual socket binds loopback only; occupied-port failure is clear and does not kill the owner.
- Verify: `dotnet build Guildhall.slnx`, `dotnet test tests/Guildhall.AgentHost.Tests --filter Health` and, after configuration is available, a local socket inspection.
- Model: lightweight. P03 must land before connecting the desktop or declaring the host ready for use.

### P03 — Authenticated local API boundary

- Files: host authentication/origin configuration, backend `LocalAuth*` tests, accepted bootstrap decision. No UI or SQL implementation.
- Depends on: P02.
- Work: validate a required process-supplied bearer token, constrain accepted Host headers and CORS origins, and define the process-to-Tauri token handoff for P04/P09. Refuse startup if the token is absent. Never log authorization headers. Permit valid browser preflights without granting endpoint access.
- Acceptance: missing/wrong token gives 401, correct token succeeds; unrelated origins are denied, valid preflight works, unapproved Host values fail; missing configuration fails safely; token is absent from logs. Liveness and discovery are authenticated consistently.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter LocalAuth` with real HTTP boundary cases, not just comparisons to implementation constants.
- Model: stronger reasoning for design/review; lightweight implementation after the contract is fixed.

### P04 — Desktop shell and bounded bootstrap command

- Files: `apps/desktop/` only, including capabilities/CSP and tests for config parsing.
- Depends on: P01 and P03's agreed contract.
- Work: scaffold React/strict TypeScript/Vite/Tauri 2; add one narrow native command returning the configured loopback host URL and process-inherited session token. No filesystem or shell API. Show a static status screen first; P08 handles requests.
- Acceptance: native window launches; no secrets in built assets, URLs, or browser persistence; native command validates loopback config and is scoped to the main window. Remote content navigation and broad capabilities are absent.
- Verify: `npm.cmd --prefix apps/desktop run typecheck`, `npm.cmd --prefix apps/desktop run build`, `cargo check --manifest-path apps/desktop/src-tauri/Cargo.toml`, and manual `npm.cmd --prefix apps/desktop run tauri -- dev` with session configuration supplied by the later launcher.
- Model: lightweight; focused review of the boundary against P03.

### P05 — SQL Server schema and migration pipeline

- Files: host `Persistence/`, EF registrations, local migration tool configuration, backend `Persistence*` tests, `scripts/database/Migrate-Database.ps1`.
- Depends on: P02 and approved dedicated development/test databases and identities. Provisioning scripts must be reviewed before execution; never infer authorization to modify an existing SQL instance from plan approval alone.
- Work: use EF Core SQL Server and its local tooling; add one bootstrap metadata table and first migration. Migration is an explicit command using the existing Windows identity; runtime uses a dedicated restricted SQL login and never upgrades schema. Store its credentials in OS-protected local storage and inject only into the host environment. The wrapper prints a sanitized target summary and refuses system databases or an unexpected configured database name. Require separate DDL and runtime identities. No automatic create/drop, rollback-to-zero, or destructive reset command.
- Acceptance: empty approved database upgrades once; second invocation changes nothing; record survives host restart; failed transactional migration leaves no falsely applied history; runtime identity can read metadata but cannot alter schema. No conversation/agent schema. Tests use real SQL Server, uniquely named `GuildhallTest_*` databases, and clean up only databases created by that test run.
- Verify: `dotnet ef migrations script --project services/agent-host --startup-project services/agent-host --idempotent --output .local/migrations.sql`, review the generated SQL, then `./scripts/database/Migrate-Database.ps1 -DatabaseName GuildhallDev` only against the approved target; `dotnet test tests/Guildhall.AgentHost.Tests --filter Persistence` with the explicit test connection configured. Verify runtime schema changes are denied using the dedicated runtime SQL login; shared-identity execution does not satisfy the accepted separation requirement.
- Model: stronger reasoning for schema/permissions/failure review; bounded implementation with lightweight model.

### P06 — Database readiness endpoint

- Files: `Api/Ready*`, persistence readiness query, backend `Readiness*` tests.
- Depends on: P03, P05.
- Work: implement `/ready` with bounded SQL timeout and expected migration check, without schema mutation. Return stable sanitized codes such as `database_unavailable` and `migration_required`.
- Acceptance: ready SQL gives 200; absent/unreachable database and outdated schema give 503; `/health` still works; logs and payloads never reveal the connection string. Verify failure using invalid test configuration, without stopping shared SQL services.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter Readiness`; real SQL cases require the dedicated test target.
- Model: lightweight.

### P07 — Ollama discovery adapter and endpoint

- Files: `Providers/Ollama/`, normalized model DTOs and `/models`, backend `ModelDiscovery*` tests.
- Depends on: P03.
- Work: GET the configured local Ollama `/api/tags`; normalize only model identity/display fields. Bound timeout, response bytes, and cancellation; reject redirects and non-loopback configuration. No inference SDK or generation contract.
- Acceptance: fake HTTP tests cover models present, empty list, refusal, timeout, malformed/oversized response, cancellation, and redirects. Failure does not crash the host or impair SQL readiness. No automatic installation, pull, download, or inference. DTO tests show Ollama response details do not leak into frontend contracts.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter ModelDiscovery`; optional bounded live `/api/tags` check on an already running local Ollama instance.
- Model: lightweight.

### P08 — Host client and status screen

- Files: desktop `src/api/`, status view and colocated tests; package changes only for Vitest and React Testing Library if needed.
- Depends on: P04, P06, P07.
- Work: use native fetch with in-memory token; render host connectivity, SQL readiness, and local model availability as separate states. Add refresh and bounded request cancellation. No routing framework or state library.
- Acceptance: loading, host disconnected, SQL unavailable/migration required, no models, model list, malformed host response, and Ollama unavailable are visible and recoverable. UI never calls SQL or Ollama directly; tests mock only the project-owned API. Render model names as text, including names containing markup.
- Verify: `npm.cmd --prefix apps/desktop run test -- --run`, `npm.cmd --prefix apps/desktop run typecheck`, `npm.cmd --prefix apps/desktop run build`.
- Model: lightweight.

### P09 — Development launcher and prerequisite checker

- Files: `scripts/startup/Check-Prerequisites.ps1`, `scripts/startup/Start-Dev.ps1`, setup documentation; configuration plumbing only if required by the agreed contract.
- Depends on: P03, P04, P06, P07.
- Work: read-only prerequisite checker reports actionable gaps. Launcher generates a fresh cryptographic token per session, passes it through child environments, starts host, awaits authenticated health, then starts Tauri dev. Keep logs sanitized. Launch background helpers hidden on Windows.
- Acceptance: host and Vite bind loopback; occupied ports fail without process termination; early host exit/timeout is reported; closing launcher/Ctrl+C cleans only owned child process trees. Restart recovers connectivity without changing SQL metadata. SQL and Ollama are checked but never started, stopped, installed, or reconfigured by these scripts. No migration on launch. No token in process command-line arguments or persistent state.
- Verify: `./scripts/startup/Check-Prerequisites.ps1`, `./scripts/startup/Start-Dev.ps1`; manual failure scenarios with a temporary port occupant and invalid dependency endpoints. Inspect owned process cleanup and logs. Do not stop shared services to simulate failure.
- Model: lightweight; escalate only unresolved Windows lifecycle or cross-process authentication faults.

### P10 — Verification entry point and Phase 0 review

- Files: `scripts/misc/Verify.ps1`, README, verification record; fixes stay within Phase 0.
- Depends on: P05–P09.
- Work: aggregate required checks, document environment variables and failures, and complete the native-window smoke checklist below. Avoid introducing a WebDriver/CI platform for a single status screen.
- Acceptance: commands propagate failures; deterministic checks run without Ollama/cloud accounts; SQL integration checks use explicit isolated targets. Report skipped integration prerequisites separately. Full Phase 0 verification must fail if required SQL/live-desktop checks are skipped or unresolved.
- Verify: `./scripts/misc/Verify.ps1` for deterministic checks, `./scripts/misc/Verify.ps1 -IncludeSql` for all automated checks, then the manual native-window checks. Required SQL configuration is described below. No pass claim based solely on a browser-rendered React test.
- Model: lightweight; stronger model only for a specific unresolved architectural defect.

## Proposed configuration and command contract

Use process-only variables in development. No token, SQL credential, or connection string belongs in a frontend environment file or command argument.

- `GUILDHALL_HOST_URL`: launcher-chosen loopback base URL; default `http://127.0.0.1:43120`.
- `GUILDHALL_SESSION_TOKEN`: cryptographically random session token injected by launcher, read by host and narrow native bootstrap command.
- `ConnectionStrings__Guildhall`: approved dedicated application database on `localhost`; restricted SQL runtime login, with credentials loaded from OS-protected storage into the host environment only.
- `ConnectionStrings__GuildhallMigrations`: Windows-integrated DDL identity connection used only by migration wrapper; never inherited by host/desktop runtime processes.
- `GUILDHALL_TEST_SQL_CONNECTION`: test administrative connection scoped to the approved test instance; tests create uniquely named scratch databases and never reuse user databases. Never pass this to the desktop.
- `GUILDHALL_OLLAMA_URL`: default `http://127.0.0.1:11434`; local-only for Phase 0.

Launcher constructs a minimal environment for each child: desktop receives the session bootstrap values and necessary build environment, but no database connection or migration credentials. Document that a token-bearing frontend remains trusted UI code and requires safe output rendering.

Retain encryption for both SQL connections. The accepted certificate-validation exception applies only to the selected local development instance; remote targets must validate certificates. No machine-wide trust or SQL service changes are needed for this exception.

Following commands are verification targets for future implementation, not executable setup instructions today:

```powershell
dotnet tool restore
dotnet restore Guildhall.slnx
npm.cmd --prefix apps/desktop ci
dotnet build Guildhall.slnx --no-restore
dotnet test tests/Guildhall.AgentHost.Tests --filter 'Category!=SqlIntegration'
npm.cmd --prefix apps/desktop run typecheck
npm.cmd --prefix apps/desktop run test -- --run
npm.cmd --prefix apps/desktop run build
cargo check --manifest-path apps/desktop/src-tauri/Cargo.toml
./scripts/misc/Verify.ps1 -IncludeSql
./scripts/startup/Start-Dev.ps1
```

`Verify.ps1` must run the listed build/typecheck/test/Cargo checks and propagate nonzero exits. SQL tests carry `Category=SqlIntegration`; `-IncludeSql` validates the explicit test connection then runs them. Without that switch it reports SQL checks as not run. External downloads may be needed for initial dependency restoration; restored application runtime must operate with external networking unavailable. This does not require a global package installation or mandatory Docker.

## Phase 0 exit checklist

- [x] Reviewed decisions and exact remaining prerequisite/provisioning actions are recorded.
- [x] One documented command opens a native Tauri window connected to the authenticated loopback host.
- [x] Missing/invalid auth and unrelated web origins cannot invoke the host API; native permissions/CSP are reviewed.
- [x] SQL migration applies to the approved dedicated database, is repeatable, and survives restart. Runtime readiness does not perform DDL.
- [x] SQL outage or outdated schema is distinguished from host failure without exposing secrets.
- [x] Real local Ollama with at least one already installed model appears by normalized name; this check is required for completion, even though most tests use fakes.
- [x] Empty/missing/unreachable Ollama shows a clear state; refresh recovers when the configured endpoint becomes available.
- [x] Failure simulation uses isolated endpoints/databases and does not stop shared services or modify existing user data.
- [x] Restored application launches without external accounts/network access; no cloud service is contacted.
- [x] Launcher exit/restart handles owned processes correctly and preserves SQL metadata.
- [x] Required automated checks and native-window checks have recorded results; unresolved prerequisites/skips prevent a Phase 0 completion claim.
- [x] No chat, inference, agent/tool execution, remote exposure, deployment, or unapproved system change was added.

## State at handoff

Phase 0 source, bootstrap migration, tests, development scripts, approved local setup, and native/live acceptance checks are complete. Read `verification.md` for the actual results and the precise simulated-offline test limits. Later phases remain unimplemented.
