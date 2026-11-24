---
id: local-api-boundary-bootstrap
title: "Local API Boundary & Tauri Bootstrap"
area: host-security-lifecycle
triggers:
  - bootstrap
  - LocalApiBoundary.UseLocalBoundary
  - LocalConfiguration.LoopbackUri
  - GUILDHALL_SESSION_TOKEN
  - GUILDHALL_HOST_URL
  - request
primary_files:
  - apps/desktop/src-tauri/tauri.conf.json
  - apps/desktop/src-tauri/capabilities/main.json
  - apps/desktop/src-tauri/permissions/bootstrap.toml
  - apps/desktop/src-tauri/src/main.rs
  - apps/desktop/src/api.ts
  - apps/desktop/src/App.tsx
  - services/agent-host/Configuration/LocalConfiguration.cs
  - services/agent-host/Program.cs
  - services/agent-host/Api/LocalApiBoundary.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0001-phase-0.md
  - "docs/masterplan/phase 0/phase-0.md"
uncertainties:
  - boundary: "Ephemeral session bootstrap env"
    location: "apps/desktop/src-tauri/src/main.rs::bootstrap"
    note: "Per-session 32-byte hex GUILDHALL_SESSION_TOKEN and loopback GUILDHALL_HOST_URL are generated at runtime by Start-Dev.ps1 or Start-BackgroundHost.ps1 and live only in process environment."
    static_or_runtime: "runtime"
---

# Local API Boundary & Tauri Bootstrap

Enforces Tauri webview/CSP/capability confinement, bootstraps the per-session loopback URL and Bearer token into the React client, and validates Host, Origin, and fixed-time SHA-256 Bearer auth on every Agent Host request.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Tauri / Rust | `apps/desktop/src-tauri/tauri.conf.json` | `capabilities` | `~L13-L18` | `app.security.csp`, `devCsp`, and `capabilities` `["main"]` (`allow-bootstrap`) | CSP restricts `connect-src` to `ipc:`, `http://ipc.localhost`, and `127.0.0.1:*`; `object-src`, `base-uri`, and `frame-src` are `'none'`; `capabilities/main.json` grants only `allow-bootstrap` |
| 2 | Tauri / Rust | `apps/desktop/src-tauri/src/main.rs` | `main` | `~L196-L225` | `tauri::Builder::invoke_handler` + `WebviewWindowBuilder::on_navigation` | Webview navigation permits only `http://tauri.localhost/`, `tauri://localhost/`, or `is_loopback_http_origin`; `GUILDHALL_WINDOW_TITLE` is sanitized to <=120 non-control chars |
| 3 | Tauri / Rust | `apps/desktop/src-tauri/src/main.rs` | `bootstrap` | `~L33-L64` | IPC command `bootstrap` -> `Bootstrap { hostUrl, token, windowTitle, instanceTag }` | `validate()` requires `http://127.0.0.1:<port>/` with no path/query/fragment/userinfo and token length 32..128 with zero whitespace |
| 4 | Desktop / Tauri | `apps/desktop/src/api.ts` | `request` | `~L10-L40` | `invoke<Bootstrap>('bootstrap')` -> `fetch(new URL(path, config.hostUrl))` with Bearer header | Uses `redirect: 'error'` and `cache: 'no-store'`; passes `/ready` HTTP 503 payloads through while throwing `HostRequestError` on other non-OK statuses; `App.tsx` caches config in a ref only |
| 5 | Agent Host | `services/agent-host/Configuration/LocalConfiguration.cs` | `LocalConfiguration.LoopbackUri` | `~L13-L30` | `Origins` allowlist, `AllowedOrigins(frontendUrl)`, and numeric loopback URI parser | Rejects non-HTTP, non-numeric-loopback (e.g. `localhost` hostname), non-root path, query, fragment, or userinfo URIs; `Program.cs` L41-L43 binds CORS to `AllowedOrigins` |
| 6 | Agent Host | `services/agent-host/Program.cs` | `UseLocalBoundary` | `~L103-L113` | `GUILDHALL_SESSION_TOKEN` + `GUILDHALL_HOST_URL` -> `app.Urls` + `app.UseLocalBoundary` | Fails startup if `GUILDHALL_SESSION_TOKEN` is missing, <32 or >128 chars, or contains whitespace; binds Kestrel strictly to parsed loopback `hostUri` |
| 7 | Agent Host / Security | `services/agent-host/Api/LocalApiBoundary.cs` | `LocalApiBoundary.UseLocalBoundary` | `~L9-L33` | Host/Port (400) -> Origin (403) -> `UseCors()` -> SHA-256 Bearer auth (401) | Compares SHA-256 digests with `CryptographicOperations.FixedTimeEquals` (rejects `Authorization` >256 chars) and sets `Cache-Control: no-store` and `X-Content-Type-Options: nosniff` |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Decisions: [coding-standards.md](../../../docs/coding-standards.md), [0001-phase-0.md](../../../docs/decisions/0001-phase-0.md)
- Masterplan: [phase-0.md](<../../../docs/masterplan/phase 0/phase-0.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests --filter "FullyQualifiedName~HealthAndLocalAuthTests|FullyQualifiedName~ConfigurationTests"`
- `npm.cmd --prefix apps/desktop run test -- --run src/api.test.ts`
- `cargo test --locked --manifest-path apps/desktop/src-tauri/Cargo.toml`
