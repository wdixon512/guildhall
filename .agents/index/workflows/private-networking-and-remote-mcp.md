---
id: private-networking-and-remote-mcp
title: "Private Networking & Remote Streamable HTTP MCP"
area: infra-scheduling-health
triggers:
  - "/infrastructure/network-status"
  - "/mcp/servers"
  - PrivateNetworkGuard
  - PrivateNetworkProbeService
  - PrivateInfrastructurePanel
  - McpServerRegistration
  - streamable_http
primary_files:
  - apps/desktop/src/PrivateInfrastructurePanel.tsx
  - services/agent-host/Api/McpEndpoints.cs
  - services/agent-host/Mcp/PrivateNetworkProbeService.cs
  - services/agent-host/Mcp/McpRegistry.cs
  - services/agent-host/Mcp/McpJsonRpcClient.cs
  - services/agent-host/Security/PrivateNetworkGuard.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 5/private-networking.md"
  - "docs/masterplan/phase 5/verification.md"
uncertainties:
  - boundary: "Host WireGuard/Tailscale NICs, TCP listeners, and DPAPI MCP tokens"
    location: "services/agent-host/Mcp/PrivateNetworkProbeService.cs::PrivateNetworkProbeService"
    note: "Tunnel NICs, Windows Firewall rules, active TCP listeners (1433/11434), and mcp*.dpapi tokens reside in the local OS and %LOCALAPPDATA%\\Guildhall\\credentials outside git."
    static_or_runtime: "runtime"
---

# Private Networking & Remote Streamable HTTP MCP

Probes WireGuard/Tailscale interfaces and port 1433/11434 exposure posture, and registers/probes remote Streamable HTTP MCP servers with `PrivateNetworkGuard` and DPAPI bearer tokens.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/PrivateInfrastructurePanel.tsx` | `PrivateInfrastructurePanel.refreshAll` | `~L51-L81` | `GET /mcp/sql/profiles`, `GET /mcp/servers/custom`, `GET /infrastructure/network-status` | `AbortController` cleanup on unmount; surfaces tunnel detection and 1433/11434 Port Exposure Posture pills. |
| 2 | Agent Host / MCP | `services/agent-host/Api/McpEndpoints.cs` | `McpEndpoints.MapMcpEndpoints` | `~L12-L224` | `GET /infrastructure/network-status`, `POST/DELETE /mcp/servers`, `PUT/DELETE /mcp/servers/{id}/token`, `POST /mcp/servers/{id}/probe`, `GuildhallDbContext.McpServerRegistrations` | `LocalApiBoundary` bearer token; validates `streamable_http` URIs via `PrivateNetworkGuard`; stores bearer tokens under DPAPI key `mcp:{id}`; OCC `Revision` check on delete. |
| 3 | MCP | `services/agent-host/Mcp/PrivateNetworkProbeService.cs` | `PrivateNetworkProbeService.Inspect` | `~L30-L138` | `NetworkInterface.GetAllNetworkInterfaces`, `IPGlobalProperties.GetActiveTcpListeners` (ports 1433, 11434) | Best-effort non-throwing OS probe flagging wildcard (`0.0.0.0`/`::`) or public IP bindings vs loopback/private-tunnel-only listeners. |
| 4 | Security | `services/agent-host/Security/PrivateNetworkGuard.cs` | `PrivateNetworkGuard.ValidateMcpEndpoint` | `~L10-L81` | `ValidateMcpEndpoint`, `IsPrivateTunnelHost`, `IsPrivateTunnelIp` | Permits HTTPS, loopback HTTP, or HTTP only when `AllowPrivateTunnelHttp=true` and host is `.ts.net`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), `100.64.0.0/10`, or `fc00::/7`. |
| 5 | Desktop / UI | `apps/desktop/src/PrivateInfrastructurePanel.tsx` | `PrivateInfrastructurePanel.handleCreateRemoteMcp` | `~L184-L250` | `POST /mcp/servers` -> `PUT /mcp/servers/{id}/token` -> `POST /mcp/servers/{id}/probe` | Enforces >=8-char token validation client-side, defaults new remote servers to `Tier3`, clears secret state on toggle/cancel. |
| 6 | MCP | `services/agent-host/Mcp/McpRegistry.cs` | `McpRegistry.ProbeServerAsync` | `~L79-L112` | `_externalToolsCache[reg.Id]`, `McpHealthSupervisor.ResetForProbe` / `RecordSuccess` / `RecordFailure` | Caches discovered tool schemas in `_externalToolsCache` and updates Tier 5 `McpHealthSupervisor` circuit state. |
| 7 | Agent Host / Security | `services/agent-host/Mcp/McpJsonRpcClient.cs` | `McpJsonRpcClient.SendHttpAsync` | `~L87-L146` | `HttpClient("mcp-http")`, `Mcp-Session-Id` (`_sessionIds`), DPAPI Bearer `mcp:{id}`, `application/json` & `text/event-stream` | Re-validates endpoint with `PrivateNetworkGuard` on every outbound HTTP call; hardened `SocketsHttpHandler` disables redirects and proxies; 15s `CallTimeout`. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) (loopback host boundary, DPAPI credentials, hardened `HttpClient`, `AbortController` UI cleanup).
- Enforce WireGuard/Tailscale private network rules and remote MCP registration contracts per [private-networking.md](<../../../docs/masterplan/phase 5/private-networking.md>) and [verification.md](<../../../docs/masterplan/phase 5/verification.md>).
- Index maintenance and workflow execution follow [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~PrivateNetworkGuardTests|FullyQualifiedName~Phase5SqlAndGatewayTests"`
- `npm.cmd --prefix apps/desktop test -- --run src/PrivateInfrastructurePanel.test.tsx`
