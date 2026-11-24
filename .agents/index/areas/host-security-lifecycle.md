---
area: host-security-lifecycle
title: "Host Security, Credentials, & Dev Lifecycle"
summary: "Loopback API boundary, Tauri IPC isolation, DPAPI credential storage, env scrubbing, multi-instance dev sessions, and background host lifecycle."
primary_files:
  - apps/desktop/src-tauri/tauri.conf.json
  - apps/desktop/src-tauri/capabilities/main.json
  - apps/desktop/src-tauri/permissions/bootstrap.toml
  - apps/desktop/src-tauri/src/main.rs
  - apps/desktop/src/api.ts
  - apps/desktop/src/App.tsx
  - services/agent-host/Program.cs
  - services/agent-host/Configuration/LocalConfiguration.cs
  - services/agent-host/Api/LocalApiBoundary.cs
  - services/agent-host/Configuration/DpapiCredentialStore.cs
  - services/agent-host/Configuration/ICredentialStore.cs
  - services/agent-host/Configuration/EnvironmentScrubber.cs
  - services/agent-host/Api/ConnectionEndpoints.cs
  - services/agent-host/Providers/ConnectionCatalogService.cs
  - services/agent-host/Providers/Cli/CliProcessRunner.cs
  - scripts/misc/Common.ps1
  - scripts/startup/Start-Dev.ps1
  - scripts/startup/Get-DevStatus.ps1
  - scripts/startup/Stop-Dev.ps1
  - scripts/startup/Start-BackgroundHost.ps1
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0001-phase-0.md
  - docs/decisions/0005-codebase-workflow-index.md
  - "docs/masterplan/phase 0/phase-0.md"
  - "docs/masterplan/phase 6/phase-6.md"
---

# Host Security, Credentials, & Dev Lifecycle

Enforces Guildhall's loopback API boundary, Tauri IPC isolation, DPAPI credential storage, child environment scrubbing, multi-instance worktree dev sessions, and detached background host lifecycle.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `apps/desktop/src-tauri/src/main.rs` | Tauri desktop shell setup, IPC commands, webview navigation allowlist, and window title sanitization. | Process env `GUILDHALL_HOST_URL`, `GUILDHALL_SESSION_TOKEN`, `GUILDHALL_WINDOW_TITLE`, `GUILDHALL_INSTANCE_TAG` | `main()`, `bootstrap()`, `open_external_url()`, `pick_workspace_folder()` |
| `apps/desktop/src/api.ts` | Frontend Tauri bootstrap wrapper and authenticated host HTTP client. | In-memory `Bootstrap` config (`hostUrl`, `token`) via `App.tsx` ref; no `localStorage` persistence | `bootstrap()`, `request()`, `requestJson()`, `requestBlob()` |
| `services/agent-host/Api/LocalApiBoundary.cs` | ASP.NET Core loopback boundary middleware enforcing Host/Port, Origin, CORS, SHA-256 Bearer auth, and security headers. | SHA-256 hash of expected Bearer token; `LocalConfiguration.AllowedOrigins` | `LocalApiBoundary.UseLocalBoundary` |
| `services/agent-host/Configuration/LocalConfiguration.cs` | Loopback URI, frontend CORS origin allowlist, and SQL Server runtime connection validation. | Static `Origins` allowlist; `SqlConnectionStringBuilder` mandatory encryption settings | `LocalConfiguration.AllowedOrigins`, `LocalConfiguration.LoopbackUri`, `LocalConfiguration.RuntimeConnection` |
| `services/agent-host/Configuration/DpapiCredentialStore.cs` | Windows DPAPI (`CryptProtectData`/`CryptUnprotectData`) encrypted API key storage with atomic `.tmp` writes. | `%LOCALAPPDATA%\Guildhall\credentials\*.dpapi` (`CurrentUser` DPAPI, entropy `Guildhall-Phase2-ApiKey-v1`) | `GetApiKeyAsync()`, `SetApiKeyAsync()`, `DeleteApiKeyAsync()`, `GetStatusAsync()` |
| `services/agent-host/Configuration/EnvironmentScrubber.cs` | Removes host tokens, SQL connection strings, and provider secrets before spawning child processes. | `BlockedPrefixes` and `BlockedSubstrings` static allowlist-by-exclusion filters | `BuildScrubbedEnvironment()`, `IsSensitiveKey()` |
| `services/agent-host/Api/ConnectionEndpoints.cs` | HTTP endpoints for provider API key CRUD, connection probing, and interactive CLI login launch. | `CloudApiIds` allowlist; delegates persistence to `ICredentialStore` | `MapConnections()` (`PUT`/`DELETE /connections/{id}/api-key`, `POST /connections/{id}/sign-in`) |
| `scripts/misc/Common.ps1` | Shared PowerShell helpers for DPAPI SQL credential loading, free port selection, child env scrubbing, session manifests, and port listener verification. | `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml`, `%LOCALAPPDATA%\Guildhall\dev\sessions\*.json` | `Get-InstanceWindowMetadata`, `Get-RegisteredDevSessions`, `Stop-RegisteredDevSession`, `Get-VerifiedGuildhallPortListeners`, `New-ChildEnvironment` |
| `scripts/startup/Start-Dev.ps1` | Multi-instance local dev orchestrator for host, Vite dev server, and Tauri desktop with port/window tagging and cleanup. | `.local/dev-sessions/<id>/session.json` and `%LOCALAPPDATA%\Guildhall\dev\sessions\<id>.json` | `Start-Dev.ps1` (`-AutoPort`, `-Replace`, `-WindowTag`, `-AttachBackground`, `-HostOnly`, `-BuiltDesktop`) |
| `scripts/startup/Get-DevStatus.ps1` | Cross-worktree dev session, source fingerprint, binary SHA-256, and port listener status inspector. | Inspects `%LOCALAPPDATA%\Guildhall\dev\sessions` (pruning exited manifests) and `Get-NetTCPConnection` | `Get-DevStatus.ps1` (`-Json`, `-CurrentCheckoutOnly`) |
| `scripts/startup/Stop-Dev.ps1` | Targeted or global shutdown for registered dev sessions and verified Guildhall port listeners. | Removes session manifests after reverse-order child-to-parent termination | `Stop-Dev.ps1` (`-SessionId`, `-Checkout`, `-Port`, `-All`) |
| `scripts/startup/Start-BackgroundHost.ps1` | Detached background Agent Host runner, DPAPI-encrypted manifest, status/stop controls, and Windows logon task registration. | `%LOCALAPPDATA%\Guildhall\dev\background-host.json`, Windows Scheduled Task `GuildhallAgentHostBackground` | `Start-BackgroundHost.ps1` (`-Status`, `-Stop`, `-RegisterLogonTask`, `-UnregisterLogonTask`) |

## Workflows

- [`local-api-boundary-bootstrap`](../workflows/local-api-boundary-bootstrap.md) — Tauri CSP/capability confinement, IPC bootstrap into React, and Host/Origin/SHA-256 Bearer auth on the Agent Host.
- [`multi-instance-dev-orchestration`](../workflows/multi-instance-dev-orchestration.md) — Worktree-isolated dev orchestration with dynamic ports, window/header tags, scrubbed child envs, and safe session teardown.
- [`detached-background-host`](../workflows/detached-background-host.md) — Detached background Agent Host with DPAPI-encrypted manifest, Task Scheduler logon task, and `Start-Dev.ps1 -AttachBackground` handoff.
- [`dpapi-secrets-and-env-scrubbing`](../workflows/dpapi-secrets-and-env-scrubbing.md) — Atomic DPAPI provider key storage, isolated CLI login launch, and environment variable scrubbing for child processes.

## Contracts & References

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Decisions: [coding-standards.md](../../../docs/coding-standards.md), [0001-phase-0.md](../../../docs/decisions/0001-phase-0.md), [0005-codebase-workflow-index.md](../../../docs/decisions/0005-codebase-workflow-index.md)
- Masterplan: [phase-0.md](<../../../docs/masterplan/phase 0/phase-0.md>), [phase-6.md](<../../../docs/masterplan/phase 6/phase-6.md>)
