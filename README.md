# Guildhall

A personal, self-hosted AI workspace. Phase 0 connectivity is complete; Phase 1 local chat is under active development.

- [Master architecture and implementation plan](docs/masterplan/personal_ai_ecosystem_master_plan.md)
- [Owner-requested roadmap amendments](docs/masterplan/product-roadmap-amendments.md)
- [Tech stack & coding standards (.NET 10, EF Core 10, Tauri 2, React 19)](docs/coding-standards.md)
- [Agent roster & worktree orchestration guide](.agents/roster.md)
- [Architecture review and proposed decisions](docs/masterplan/phase%200/architecture-review.md)
- [Phase 0 tasks, acceptance criteria, and verification](docs/masterplan/phase%200/phase-0.md)
- [Engineering principles](AGENTS.md)
- [Accepted design decisions](docs/decisions/0001-phase-0.md)
- [Phase 1 design decisions](docs/decisions/0002-phase-1.md), [conversation lifecycle](docs/decisions/0003-conversation-lifecycle.md), [chat attachments](docs/decisions/0004-chat-attachments.md)
- [Concrete setup proposal](docs/masterplan/phase%200/setup-approval.md)
- [Verification record](docs/masterplan/phase%200/verification.md)
- [Phase 1 implementation plan](docs/masterplan/phase%201/phase-1.md)
- [Development and functional QC companion plan](docs/masterplan/phase%201/development-qc-plan.md)
- [Codex, Claude Code and Cursor UX gap review](docs/masterplan/phase%201/competitive-ux-review.md)

Stack: Tauri 2, React, strict TypeScript, ASP.NET Core (.NET 10), EF Core, and locally hosted SQL Server. One desktop app, one host project, one backend test project.

Phase 0 established desktop-to-host connectivity, SQL Server migrations, and local model discovery. Phase 1 adds local chat and model inference; several reliability and QC gates remain unfinished.

## Development on Windows

Use PowerShell 7, the .NET SDK pinned in `global.json`, Node 24.13+ within major 24, and Rust's Windows MSVC toolchain. Tauri requires C++ build tools, Windows SDK, and WebView2. Scripts never install prerequisites, start SQL/Ollama services, open firewall ports, or migrate on application startup.

All scripts explicitly require PowerShell 7. From a Windows PowerShell 5.1 terminal, launch the installed PowerShell 7 directly:

```powershell
pwsh -NoProfile -File 'G:\repos\guildhall\scripts\startup\Start-Dev.ps1' -BuiltDesktop
```

This machine now selects Node 24.13.0 through nvm, has the approved SQL database/runtime credential, and has local Ollama with one discovery-test model. To restore and develop:

```powershell
dotnet tool restore
dotnet restore Guildhall.slnx --locked-mode
npm.cmd --prefix apps/desktop ci
dotnet build Guildhall.slnx --no-restore
./scripts/misc/Verify.ps1
./scripts/startup/Start-Dev.ps1
```

The launcher opens a native window titled with the current version, Git branch/worktree, and port (`Guildhall v0.1.0 [branch | :43120]`), uses fresh in-memory session authentication, and owns the host, Vite, and desktop process trees. Closing the desktop or pressing Ctrl+C stops those owned processes. By default the host listens at `127.0.0.1:43120` and Vite listens at `127.0.0.1:1420`. To run multiple worktrees side-by-side on automatic free ports, pass `-AutoPort` (or explicit `-HostPort` / `-FrontendPort` and optional `-WindowTag`); to stop and replace an existing Guildhall instance on the target ports, pass `-Replace`. Do not run `tauri dev` separately when the launcher is running.

In another PowerShell 7 window, inspect all active Guildhall sessions across worktrees (including window title, branch, version, ports, and last authenticated host/SQL status) without exposing tokens, or stop active sessions:

```powershell
pwsh -NoProfile -File .\scripts\startup\Get-DevStatus.ps1
pwsh -NoProfile -File .\scripts\startup\Stop-Dev.ps1 -All
```

Normal development mode keeps the Tauri window open while Vite applies React edits through HMR and `dotnet watch` rebuilds/restarts the ASP.NET host after C# edits. Use `-BuiltDesktop` only for a fixed embedded build and final native QC.

Guildhall inference defaults to CPU only (4 threads, 4096 context) to leave the GPU available for games. Models unload after each reply, and thinking output is disabled. Add `-UseGpu` to `Start-Dev.ps1` for automatic GPU offload. Direct host launches can opt in with `GUILDHALL_USE_GPU=true`. These settings apply only to Guildhall requests; they do not reconfigure the shared Ollama service. CPU inference still shares CPU/memory resources with games and may be slower. Choose `qwen3:1.7b` in the model dropdown for the smaller downloaded chat model.

To build embedded assets and run the native desktop without a frontend development server:

To keep a model loaded between messages, use `./scripts/startup/Start-Dev.ps1 -KeepModelWarmSeconds 300` for five minutes after a request; optionally add `-UseGpu`. The supported range is 0–900 seconds; 0 retains the unload-after-reply default. Direct host launches use `GUILDHALL_MODEL_KEEP_ALIVE_SECONDS` (clamped to that range). This request-local setting keeps RAM/VRAM occupied during retention and does not guarantee residency under Ollama memory pressure or another client's requests. At an agreed restart, launch once, send two short messages with the same model within five minutes, and inspect `ollama ps` between messages. No automatic inference or permanent warming loop is added.

```powershell
npm.cmd --prefix apps/desktop run tauri -- build --debug --no-bundle
./scripts/startup/Start-Dev.ps1 -BuiltDesktop
```

Without SQL credentials or Ollama, the window still opens and reports those dependencies as unavailable/unconfigured. Run `./scripts/startup/Check-Prerequisites.ps1` for the complete prerequisite check; it deliberately fails until required live dependencies are ready.

## SQL Server setup and migrations

Read [the setup proposal](docs/masterplan/phase%200/setup-approval.md) before any database changes. `./scripts/database/Provision-Database.ps1` prints its plan without executing changes. The `-Execute` switch is reserved for separately approved provisioning.

Provisioning targets only `localhost` / `GuildhallDev`, creates a dedicated SQL runtime login, applies the bootstrap migration under the Windows identity, and saves a Windows DPAPI-protected credential under ignored `.local/`. Runtime can SELECT metadata/history; it cannot change schemas or write data. No SQL or provider credentials enter frontend environments or assets.

After approved provisioning, preview future schema upgrades first. Migration scripts default to preview; applying changes requires `-Execute` and approval of the displayed scope:

```powershell
./scripts/database/Migrate-Database.ps1 -DatabaseName GuildhallDev
# After approving the displayed SQL:
./scripts/database/Migrate-Database.ps1 -DatabaseName GuildhallDev -Execute
```

Connections remain encrypted with the explicitly accepted local certificate exception. This exception cannot be used against remote targets. The runtime has no migration or database-creation endpoint.

## Verification

`Verify.ps1` runs the .NET build and deterministic tests, frontend typecheck/build/tests, and Rust bootstrap tests. Nonzero check results stop verification. It explicitly reports SQL tests as not run unless requested.

After separate authorization for isolated SQL tests:

```powershell
$env:GUILDHALL_TEST_SQL_CONNECTION = 'Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
./scripts/misc/Verify.ps1 -IncludeSql
```

SQL tests create unique scratch databases and delete only databases they successfully created. They test migration repeatability, persisted installation identity, readiness, restricted permissions, and transactional migration failure. They never target `GuildhallDev` or existing user databases. This connection uses the approved Windows test identity; do not put passwords in command arguments.

Full Phase 0 acceptance also requires a native-window smoke check and discovery of a real local model. A successful deterministic verification alone is insufficient. Phase 0 never generates text or pulls models automatically.

`./scripts/misc/Smoke-Host.ps1` verifies the real host, runtime SQL login, and installed model across two host restarts without opening the desktop. It stops only its own host processes and does not run migrations or inference. See [the verification record](docs/masterplan/phase%200/verification.md) for completed native/offline checks.

## Configuration and troubleshooting

- `GUILDHALL_SESSION_TOKEN` is generated in launcher memory and passed only to host/desktop processes. It is not written to status files or frontend variables, browser storage, URLs, or logs.
- `GUILDHALL_HOST_URL` is fixed by the Phase 0 desktop contract to `http://127.0.0.1:43120`.
- `GUILDHALL_OLLAMA_URL` may select another numeric loopback HTTP endpoint for local development; default `http://127.0.0.1:11434`. Remote endpoints and redirects are rejected.
- `ConnectionStrings__Guildhall` is set only in the host environment by loading the OS-protected runtime credential.
- `ConnectionStrings__GuildhallMigrations` is set only by the explicit Windows-authenticated migration command. It is never forwarded to runtime processes.
- Host disconnected: check launcher output and ports, then Refresh. SQL/Ollama failures do not make `/health` fail.
- SQL migration required: review and run the explicit migration command. No automatic DDL occurs on refresh or startup.
- SQL unavailable: check the selected local instance and runtime permissions. Do not change firewall/service settings as a shortcut.
- Ollama unavailable/no models: start your separately installed local Ollama and install a model through an approved action, then Refresh. Model listing indicates discovery, not inference quality or GPU capacity.
- A provisioning failure leaves partial Guildhall objects and the protected credential for inspection. Do not delete/recreate them blindly or rerun with an existing database/login.

No signing, installer packaging, background service installation, telemetry backend, or deployment is included. Before storing meaningful Phase 1 data, document and verify native SQL backup/restore and logical exports; protected credentials require separate recovery handling.
