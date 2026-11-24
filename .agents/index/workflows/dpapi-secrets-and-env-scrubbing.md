---
id: dpapi-secrets-and-env-scrubbing
title: "DPAPI Secrets Storage & Child Env Scrubbing"
area: host-security-lifecycle
triggers:
  - DpapiCredentialStore
  - EnvironmentScrubber.BuildScrubbedEnvironment
  - "PUT /connections/{id}/api-key"
  - "DELETE /connections/{id}/api-key"
  - "POST /connections/{id}/sign-in"
primary_files:
  - services/agent-host/Api/ConnectionEndpoints.cs
  - services/agent-host/Configuration/DpapiCredentialStore.cs
  - services/agent-host/Configuration/ICredentialStore.cs
  - services/agent-host/Configuration/EnvironmentScrubber.cs
  - services/agent-host/Providers/ConnectionCatalogService.cs
  - services/agent-host/Providers/Cli/CliProcessRunner.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 0/phase-0.md"
uncertainties:
  - boundary: "Local DPAPI credential store"
    location: "services/agent-host/Configuration/DpapiCredentialStore.cs::DpapiCredentialStore"
    note: "Encrypted *.dpapi files under %LOCALAPPDATA%\\Guildhall\\credentials\\ and runtime.credential.xml under %LOCALAPPDATA%\\Guildhall\\dev\\ are user-scoped DPAPI blobs outside git."
    static_or_runtime: "runtime"
---

# DPAPI Secrets Storage & Child Env Scrubbing

Encrypts provider and MCP credentials at rest via Windows DPAPI with atomic temp-file replacement, isolates interactive CLI sign-ins, and scrubs sensitive environment variables before spawning child processes.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host / Security | `services/agent-host/Api/ConnectionEndpoints.cs` | `ConnectionEndpoints.MapConnections` | `~L19-L45` | `PUT /connections/{id}/api-key` and `DELETE /connections/{id}/api-key` -> `ICredentialStore` + `ConnectionCatalogService.ProbeAsync` | Restricts API key configuration to `CloudApiIds` allowlist and checks trimmed length >= 8 and raw length <= 512; never returns raw keys in API responses |
| 2 | Agent Host / Security | `services/agent-host/Configuration/DpapiCredentialStore.cs` | `DpapiCredentialStore.SetApiKeyAsync` | `~L43-L68` | Writes `%LOCALAPPDATA%\Guildhall\credentials\<provider>.dpapi.tmp` -> `File.Move(overwrite: true)` | Sanitizes `providerId` to `[a-z0-9_-]`, writes atomically via `.tmp` rename, pins plaintext bytes in `Protect`, and clears `plainBytes` via `Array.Clear` in `finally`; `DeleteApiKeyAsync` removes `.dpapi` |
| 3 | Agent Host / Security | `services/agent-host/Configuration/DpapiCredentialStore.cs` | `DpapiCredentialStore.CryptProtectData` | `~L92-L156` | Win32 `crypt32.dll` `CryptProtectData` / `CryptUnprotectData` + `kernel32.dll` `LocalFree` | Uses `CRYPTPROTECT_UI_FORBIDDEN` with `Guildhall-Phase2-ApiKey-v1` entropy on Windows; `GetApiKeyAsync` (L22-L41) fails closed returning `null` on corrupt ciphertexts and `GetStatusAsync` (L70-L78) returns `••••<last4>` (or `Configured` if <8 chars) |
| 4 | Agent Host / Providers | `services/agent-host/Providers/ConnectionCatalogService.cs` | `ConnectionCatalogService.ProbeCloudKeyAsync` | `~L185-L214` | `ICredentialStore.GetApiKeyAsync` -> outbound HTTPS probe to OpenAI/Gemini/Anthropic | Decrypts API keys only inside the host process for outbound HTTPS requests; maps 401/403 to `NeedsSignIn` without exposing key material |
| 5 | Agent Host / Security | `services/agent-host/Api/ConnectionEndpoints.cs` | `ICliProcessRunner.LaunchInteractiveLogin` | `~L55-L67` | `POST /connections/{id}/sign-in` -> `CliHarnessDescriptors.Find` -> 202 Accepted | Resolves CLI executable from descriptor allowlist (404 unknown, 409 not on PATH) and passes only `descriptor.LoginArgs` to `CliProcessRunner.LaunchInteractiveLogin` (trap: `LaunchInteractiveLogin` uses `UseShellExecute = false` without clearing `Environment`) |
| 6 | Agent Host / Security | `services/agent-host/Configuration/EnvironmentScrubber.cs` | `EnvironmentScrubber.BuildScrubbedEnvironment` | `~L9-L62` | `Environment.GetEnvironmentVariables()` -> case-insensitive scrubbed `Dictionary<string, string?>` | Strips `GUILDHALL_`, `ConnectionStrings__`, `ASPNETCORE_`, `DOTNET_` prefixes and `API_KEY`, `SECRET`, `TOKEN`, `PASSWORD`, `CREDENTIAL` substrings for `CliProcessRunner.CreateProcess` and stdio `McpJsonRpcClient.SendStdioAsync` spawns |
| 7 | Agent Host / Providers | `services/agent-host/Providers/Cli/CliProcessRunner.cs` | `CliProcessRunner.CreateProcess` | `~L140-L171` | `ProcessStartInfo.Environment.Clear()` + scrubbed env dictionary | Always starts child CLI processes with `UseShellExecute = false`, `CreateNoWindow = true`, and cleared `Environment` populated only from the scrubbed dictionary |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards: [coding-standards.md](../../../docs/coding-standards.md)
- Masterplan: [phase-0.md](<../../../docs/masterplan/phase 0/phase-0.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests --filter "FullyQualifiedName~CredentialStoreTests|FullyQualifiedName~ConnectionCatalogTests"`
