---
id: mcp-registry-health-and-jsonrpc
title: "MCP Registry, Tier-5 Health Circuit Breaker & JSON-RPC Transport"
area: mcp-tools-security-gate
triggers:
  - McpRegistry
  - McpHealthSupervisor
  - McpJsonRpcClient
  - "GET /mcp/tools"
  - "POST /mcp/servers"
  - "POST /mcp/servers/{id}/probe"
  - McpServerRegistrations
primary_files:
  - services/agent-host/Api/McpEndpoints.cs
  - services/agent-host/Mcp/McpRegistry.cs
  - services/agent-host/Mcp/McpHealthSupervisor.cs
  - services/agent-host/Mcp/McpJsonRpcClient.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 3/phase-3.md"
  - "docs/masterplan/phase 3/phase-3-contracts.md"
  - "docs/masterplan/phase 3/verification.md"
  - "docs/masterplan/phase 5/private-networking.md"
uncertainties:
  - boundary: "Local SQL Server registrations and DPAPI bearer tokens"
    location: "services/agent-host/Mcp/McpJsonRpcClient.cs::McpJsonRpcClient"
    note: "Custom McpServerRegistrations rows live in local SQL Server (GuildhallDev) and mcp:<id> bearer tokens live in local DPAPI files under %LOCALAPPDATA%\\Guildhall\\credentials outside git."
    static_or_runtime: "runtime"
---

# MCP Registry, Tier-5 Health Circuit Breaker & JSON-RPC Transport

Registers custom and built-in MCP servers, enforces Tier-5 2s/5s/15s backoff and 3-failure circuit breaking, and runs Streamable HTTP and stdio JSON-RPC 2.0.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host | `services/agent-host/Api/McpEndpoints.cs` | `McpEndpoints.MapMcpEndpoints` | `~L12-L45` | `GET /mcp/tools -> McpCatalogResponse (McpServerHealthView[])` | Joins `McpRegistry` built-in/custom tool lists with `McpHealthSupervisor` Tier-5 health states for UI catalog rendering |
| 2 | Agent Host / SQL | `services/agent-host/Api/McpEndpoints.cs` | `PrivateNetworkGuard.ValidateMcpEndpoint` | `~L47-L221` | `POST/DELETE /mcp/servers, PUT/DELETE /mcp/servers/{id}/token, POST /mcp/servers/{id}/probe` | Enforces `PrivateNetworkGuard` on `streamable_http` URIs, stores bearer tokens under DPAPI key `mcp:{id}`, checks OCC `Revision` on delete |
| 3 | Agent Host / MCP | `services/agent-host/Mcp/McpRegistry.cs` | `McpRegistry.GetEnabledToolsForMemberAsync` | `~L18-L112` | `McpServerRegistrations + _externalToolsCache ConcurrentDictionary` | Lazy-probes uncached external servers via `ProbeServerAsync`, caches tool descriptors in `_externalToolsCache`, skips open circuits |
| 4 | Agent Host / MCP | `services/agent-host/Mcp/McpRegistry.cs` | `McpRegistry.InvokeToolAsync` | `~L114-L153` | `IMcpToolProvider / IMcpJsonRpcClient -> ExfiltrationGuard.TruncateAndRedact` | Checks `CanAttempt` before invoking built-in or DB-registered server, updates Tier-5 health, redacts outputs and error messages |
| 5 | MCP | `services/agent-host/Mcp/McpHealthSupervisor.cs` | `McpHealthSupervisor.RecordFailure` | `~L5-L104` | `ConcurrentDictionary<string, McpServerHealthState> + BackoffSchedule (2s, 5s, 15s)` | Transitions `Degraded` (2s/5s/15s `NextRetryUtc`) to `Failed` (circuit open) on 3 consecutive failures until `ResetForProbe`/`RecordSuccess` |
| 6 | MCP | `services/agent-host/Mcp/McpJsonRpcClient.cs` | `McpJsonRpcClient.SendHttpAsync` | `~L87-L146` | `Streamable HTTP (POST JSON-RPC 2.0, Mcp-Session-Id, DPAPI Bearer, SSE/JSON)` | Re-validates endpoint via `PrivateNetworkGuard`, attaches DPAPI Bearer token and `Mcp-Session-Id`, parses JSON or `text/event-stream` |
| 7 | MCP | `services/agent-host/Mcp/McpJsonRpcClient.cs` | `McpJsonRpcClient.SendStdioAsync` | `~L148-L240` | `Stdio JSON-RPC 2.0 (EnvironmentScrubber, 15s CallTimeout, 256 KiB MaxLineLength)` | Clears env and injects `EnvironmentScrubber` vars, enforces 15s timeout and 256 KiB stdout line cap, kills process tree in `finally` |

## Contracts & Guardrails

- Follow [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md), and [coding-standards.md](../../../docs/coding-standards.md).
- Registry, Tier-5 health, JSON-RPC 2.0, and SSRF protection: [phase-3.md](<../../../docs/masterplan/phase 3/phase-3.md>), [phase-3-contracts.md](<../../../docs/masterplan/phase 3/phase-3-contracts.md>), [verification.md](<../../../docs/masterplan/phase 3/verification.md>), and [private-networking.md](<../../../docs/masterplan/phase 5/private-networking.md>).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~McpHealthSupervisorTests|FullyQualifiedName~McpExternalProvidersTests|FullyQualifiedName~PrivateNetworkGuardTests"`
