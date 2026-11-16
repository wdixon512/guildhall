# Phase 0 verification

Verified locally on Windows on November 15, 2026. Phase 0 is implemented and the local acceptance checks below passed. No later-phase application features were added.

## Implemented

- Tauri 2 native desktop with React/strict TypeScript status screen and explicit capability/CSP configuration.
- One ASP.NET Core host with authenticated loopback health, SQL readiness, and normalized Ollama model discovery endpoints.
- EF Core SQL Server bootstrap migration with one persistent installation identifier; no chat, agent, run, or tool schema.
- Dedicated `GuildhallDev` database and restricted `guildhall_runtime` SQL login, provisioned after explicit approval. Windows identity performs migrations; runtime can only read the Phase 0 metadata/history tables.
- Windows DPAPI-protected runtime password shared by local worktrees at `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml`; existing `.local/runtime.credential.xml` files remain a fallback. No credentials or tokens in frontend files, URLs, browser storage, or command arguments.
- Development launcher owns host, Vite, and Tauri children; embedded-desktop mode needs no Vite server. Explicit migration/provisioning/verification and live host smoke commands.

## Automated results

`./scripts/misc/Verify.ps1 -IncludeSql` passed:

- .NET solution builds with zero warnings/errors.
- 33 deterministic backend tests pass: local authentication, allowed origins/Host, health/readiness, configuration boundaries, and discovery failure/timeout/cancellation/size/JSON handling.
- 1 real SQL Server integration test passes with assertions covering empty-schema readiness, repeatable migration, durable installation identity, restricted user reads, denied writes/DDL, and rollback/no history entry after a deliberate transactional migration failure.
- TypeScript typecheck and Vite production build pass.
- 11 frontend tests pass: independent dependency states, recovery, safe model-name rendering, request cancellation, header authentication, and response validation.
- 1 Rust bootstrap validation test passes.

Total: 46 tests passed. SQL integration tests leave no scratch databases behind; verified after the completed run. `dotnet restore Guildhall.slnx --locked-mode` passes. Dependency audit reports no known vulnerable host packages; npm install audit reported no vulnerabilities. PowerShell scripts parse successfully.

`./scripts/misc/Smoke-Host.ps1` passed twice across host restarts with the real runtime SQL login and local Ollama. It verifies authenticated health, SQL readiness, local model discovery, rejection of unauthenticated/unrelated-origin requests, preserved installation identity, and listener cleanup.

## Native and lifecycle checks

- Launched the native Tauri development window. It initially showed host connected, SQL not configured, and Ollama unavailable, with readable recovery instructions.
- After approved setup, built embedded frontend assets with `npm.cmd --prefix apps/desktop run tauri -- build --debug --no-bundle` and launched `./scripts/startup/Start-Dev.ps1 -BuiltDesktop`.
- Inspected the native window showing **Connected**, SQL **Ready**, and local model **smollm2:135m**. No Vite listener was required in this mode.
- Closing the native window normally stops the owned host. Bounded development-launcher checks leave both API/Vite ports free. The initial orphaned Vite issue was fixed by making the launcher own Vite directly rather than relying on Tauri's shell command.
- An occupied-port check rejects startup without stopping the existing listener. Child-environment checks exclude arbitrary secrets and migration credentials; Vite receives no session token.
- Reopening host/desktop preserves the SQL installation identifier. A second explicit migration applies no changes. Real runtime-login attempts to alter schema or update metadata are denied.
- A simulated offline check used process-local WebView external DNS failures with exceptions for local hostnames. Inspected the native page through temporary local debugging: host connected, SQL ready, model available. Assets were embedded, and no external account was used. OS adapters/firewall/routes were not changed; this was a simulation, not a physically disconnected-network test.
- Temporary desktop, API, Vite, and debug processes/listeners were stopped after checks. The separately installed Ollama app remains available on `127.0.0.1:11434` as an independent local prerequisite.

## Approved environment actions completed

- Existing nvm installation reused; active Node selection switched from 20.11.0 to already installed 24.13.0.
- Official per-user Ollama 0.40.16 installed using `winget` with verified installer hash. Downloaded `smollm2:135m` (270,898,672 bytes reported by discovery). No inference/generation was performed and this is not a permanent model choice.
- Created only the approved development database/login and temporary test databases. No existing application/business database, server authentication mode, SQL/Ollama network exposure, firewall, router, or VPN configuration changed. No deployment occurred.

## Practical limits

This is a connectivity foundation. It has no chat, inference, tools, cloud provider, sidecar packaging, background task service, or remote mode. Local bearer authentication mitigates unrelated web origins; it is not an OS sandbox against malicious code running as the same Windows user. The accepted SQL certificate exception is restricted to the selected local instance. DPAPI credentials require the same Windows user/machine; establish backup/restore and secret-recovery procedures before meaningful Phase 1 data accumulates.

Local QA screenshots and temporary inspection scripts are under ignored `.local/`; they are not application runtime dependencies. Git was initialized locally; no commit, push, remote repository creation, or deployment was performed.
