---
area: mcp-tools-security-gate
title: "MCP Tools, Security Gate & Execution Loop"
summary: "Multi-round tool loop, Tier 0-3 permission gating, exfiltration redaction, workspace/external MCP providers, Tier-5 health, and JSON-RPC transport."
primary_files:
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Security/ToolPermissionGate.cs
  - services/agent-host/Security/ExfiltrationGuard.cs
  - services/agent-host/Security/WorkspacePathGuard.cs
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Api/McpEndpoints.cs
  - services/agent-host/Mcp/McpRegistry.cs
  - services/agent-host/Mcp/McpHealthSupervisor.cs
  - services/agent-host/Mcp/McpJsonRpcClient.cs
  - services/agent-host/Mcp/Providers/LocalFilesystemMcpProvider.cs
  - services/agent-host/Mcp/Providers/LocalGitMcpProvider.cs
  - services/agent-host/Mcp/Providers/LocalCommandMcpProvider.cs
  - services/agent-host/Mcp/Providers/GitHubMcpProvider.cs
  - services/agent-host/Mcp/Providers/GoogleDriveMcpProvider.cs
  - services/agent-host/Mcp/Providers/GmailMcpProvider.cs
  - apps/desktop/src/RunTraceList.tsx
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 3/phase-3.md"
  - "docs/masterplan/phase 3/phase-3-contracts.md"
  - "docs/masterplan/phase 3/verification.md"
  - "docs/masterplan/phase 4/verification.md"
  - "docs/masterplan/phase 5/private-networking.md"
---

# MCP Tools, Security Gate & Execution Loop

Owns the multi-round model tool execution loop, Tier 0–3 HITL approval gating, secret redaction, workspace/external MCP providers, Tier-5 health circuit breaking, and JSON-RPC 2.0 transport.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `services/agent-host/Chat/ChatRunner.cs` | Runs MaxToolRounds=6 agentic tool loop, Tier-2 rollback capture, and approval TCS pause/resume | `ConcurrentDictionary<Guid, TaskCompletionSource<bool>>` `pendingApprovals`; `ChatRunEvents` SQL rows | `RunAsync`, `ExecuteToolCallWithGateAsync`, `ResolveEventDecisionAsync` |
| `services/agent-host/Security/ToolPermissionGate.cs` | Evaluates Tier0..Tier3 security policies and secret-bearing argument denials | Stateless; resolves workspace > conversation > member `PermissionPolicies` | `PermissionPolicies.ResolveEffective`, `ToolPermissionGate.Evaluate` |
| `services/agent-host/Security/ExfiltrationGuard.cs` | Detects and redacts PEM keys, provider tokens, Bearer headers, SQL passwords, and session tokens | Stateless compiled regexes | `RedactSecrets`, `ContainsSensitiveSecret`, `TruncateAndRedact` |
| `services/agent-host/Security/WorkspacePathGuard.cs` | Enforces workspace containment, symlink/junction checks, `.git` write protection, and sensitive-file blocking | Stateless path normalization and `FileAttributes.ReparsePoint` checks | `ValidateReadPath`, `ValidateWritePath`, `IsSensitivePath` |
| `services/agent-host/Api/ChatEndpoints.cs` | Exposes HITL approval decision endpoint for pending tool events | `LocalApiBoundary` bearer auth; forwards to `ChatRunner.ResolveEventDecisionAsync` | `POST /runs/{runId}/events/{eventId}/decision` |
| `apps/desktop/src/RunTraceList.tsx` | Renders run tool trace and inline Approve/Deny approval cards | Component `expandedIds`/`confirmRollbackId` UI state; reads `ArgumentsRedactedJson` | `RunTraceList` |
| `services/agent-host/Mcp/Providers/LocalFilesystemMcpProvider.cs` | Built-in `filesystem-mcp` (Tier 1 reads capped at 64 KiB; Tier 2 writes/edits/deletes) | Guarded by `WorkspacePathGuard` | `InvokeAsync` |
| `services/agent-host/Mcp/Providers/LocalGitMcpProvider.cs` | Built-in `git-mcp` (Tier 1 `git_status`/`log`/`diff`/`show`) | `EnvironmentScrubber` child env; 32 KiB output cap | `InvokeAsync`, `ProbeAsync` |
| `services/agent-host/Mcp/Providers/LocalCommandMcpProvider.cs` | Built-in `workspace-exec-mcp` (Tier 2 allowlisted binaries without shell, 60s timeout) | `EnvironmentScrubber` child env; `ExfiltrationGuard` stdout/stderr redaction | `InvokeAsync` |
| `services/agent-host/Mcp/Providers/GitHubMcpProvider.cs` | Built-in `github-mcp` (Tier 1 repo/PR/issue reads; Tier 3 issue/comment writes) | DPAPI token via `ICredentialStore` (`github-mcp`) | `InvokeAsync`, `ProbeAsync` |
| `services/agent-host/Mcp/Providers/GoogleDriveMcpProvider.cs` | Built-in `google-drive-mcp` (Tier 1 search and text/Doc export capped at 64 KiB) | DPAPI token via `ICredentialStore` (`google-drive-mcp`) | `InvokeAsync`, `ProbeAsync` |
| `services/agent-host/Mcp/Providers/GmailMcpProvider.cs` | Built-in `gmail-mcp` (Tier 1 message search/read; Tier 3 draft creation only) | DPAPI token via `ICredentialStore` (`gmail-mcp`) | `InvokeAsync`, `ProbeAsync` |
| `services/agent-host/Mcp/McpRegistry.cs` | Unifies built-in `IMcpToolProvider` instances and DB `McpServerRegistrations` with lazy tool discovery | `ConcurrentDictionary<string, IReadOnlyList<McpToolDescriptor>>` `_externalToolsCache`; `McpServerRegistrations` | `ListServersAsync`, `GetEnabledToolsForMemberAsync`, `ProbeServerAsync`, `InvokeToolAsync` |
| `services/agent-host/Mcp/McpHealthSupervisor.cs` | Tracks Tier-5 per-server health states with 2s/5s/15s backoff and 3-failure circuit breaker | `ConcurrentDictionary<string, McpServerHealthState>` `_states` | `CanAttempt`, `RecordSuccess`, `RecordFailure`, `ResetForProbe`, `MarkDisconnected` |
| `services/agent-host/Mcp/McpJsonRpcClient.cs` | Executes JSON-RPC 2.0 over Streamable HTTP (`Mcp-Session-Id`, SSE, DPAPI Bearer) and stdio | `ConcurrentDictionary<string, string>` `_sessionIds`; 15s `CallTimeout`; 256 KiB `MaxLineLength` | `ListToolsAsync`, `CallToolAsync` |
| `services/agent-host/Api/McpEndpoints.cs` | HTTP catalog, custom server registration, DPAPI token management, delete with OCC, and health probe endpoints | `McpServerRegistrations` OCC `Revision`; DPAPI keys `mcp:{id}` | `GET /mcp/tools`, `POST /mcp/servers`, `PUT/DELETE /mcp/servers/{id}/token`, `DELETE /mcp/servers/{id}`, `POST /mcp/servers/{id}/probe` |

## Workflows

- [`tool-loop-approval-and-exfiltration-gate`](../workflows/tool-loop-approval-and-exfiltration-gate.md) — Multi-round tool loop, Tier 0–3 policy gating, HITL TCS pause/resume, Tier-2 rollback, and secret redaction.
- [`builtin-workspace-and-external-mcp`](../workflows/builtin-workspace-and-external-mcp.md) — Workspace path containment across local providers and tier-separated GitHub, Google Drive, and Gmail providers.
- [`mcp-registry-health-and-jsonrpc`](../workflows/mcp-registry-health-and-jsonrpc.md) — Custom/built-in MCP catalog, Tier-5 health backoff and circuit breaker, and Streamable HTTP/stdio JSON-RPC 2.0 transport.

## Contracts & References

- [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [phase-3.md](<../../../docs/masterplan/phase 3/phase-3.md>), [phase-3-contracts.md](<../../../docs/masterplan/phase 3/phase-3-contracts.md>), and [verification.md](<../../../docs/masterplan/phase 3/verification.md>)
- [phase-4 verification.md](<../../../docs/masterplan/phase 4/verification.md>) and [private-networking.md](<../../../docs/masterplan/phase 5/private-networking.md>)
