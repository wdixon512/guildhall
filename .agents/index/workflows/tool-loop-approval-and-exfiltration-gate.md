---
id: tool-loop-approval-and-exfiltration-gate
title: "Tool Loop, HITL Approval Gate & Exfiltration Redaction"
area: mcp-tools-security-gate
triggers:
  - MaxToolRounds
  - pendingApprovals
  - ToolPermissionGate.Evaluate
  - ExfiltrationGuard
  - "POST /runs/{runId}/events/{eventId}/decision"
  - RunTraceList
primary_files:
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Security/ToolPermissionGate.cs
  - services/agent-host/Security/ExfiltrationGuard.cs
  - services/agent-host/Api/ChatEndpoints.cs
  - apps/desktop/src/RunTraceList.tsx
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 3/phase-3.md"
  - "docs/masterplan/phase 3/phase-3-contracts.md"
  - "docs/masterplan/phase 3/verification.md"
  - "docs/masterplan/phase 4/verification.md"
uncertainties: []
---

# Tool Loop, HITL Approval Gate & Exfiltration Redaction

Runs up to 6 model/tool rounds, gates every tool call by policy and tier, pauses on HITL approval via TCS, and redacts secrets from args and outputs.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host / Security | `services/agent-host/Chat/ChatRunner.cs` | `PermissionPolicies.ResolveEffective` | `~L165-L176` | `WorkspacePolicyOverride / Conversation.PermissionPolicyOverride / AgentDefinition.PermissionPolicy` | Resolves effective policy (workspace > conversation > member) and member `EnabledToolIdsJson` before entering the tool loop |
| 2 | Agent Host | `services/agent-host/Chat/ChatRunner.cs` | `MaxToolRounds` | `~L250-L312` | `BackingEngineRequest.ToolTurns -> BackingEngineToolTurn` | Caps tool execution at `MaxToolRounds=6` per turn and feeds each round's `BackingEngineToolResult` list back to `StreamTurnAsync` |
| 3 | Security | `services/agent-host/Security/ToolPermissionGate.cs` | `ToolPermissionGate.Evaluate` | `~L46-L128` | `ToolSecurityTiers (Tier0..Tier3) -> ToolGateDecision (Allow/RequireApproval/Deny)` | Denies disabled servers and secret-bearing Tier 2/3 args; Tier 3 always returns `RequireApproval` even under unrestricted |
| 4 | Security | `services/agent-host/Security/ExfiltrationGuard.cs` | `ExfiltrationGuard.ContainsSensitiveSecret` | `~L22-L49` | `RedactSecrets / ContainsSensitiveSecret / TruncateAndRedact (16_000 chars)` | Detects and redacts PEM private keys, provider API tokens, Bearer tokens, SQL passwords, and `GUILDHALL_SESSION_TOKEN` |
| 5 | Agent Host / SQL | `services/agent-host/Chat/ChatRunner.cs` | `ChatRunner.ExecuteToolCallWithGateAsync` | `~L347-L478` | `ChatRunEvents (PendingApproval / approval.requested) + pendingApprovals TCS + Tier 2 rollback snapshot` | Persists redacted args, awaits `pendingApprovals` TCS on `RequireApproval`, captures/rolls back Tier 2 snapshots on failure, redacts result (16000 chars) |
| 6 | Desktop / UI | `apps/desktop/src/RunTraceList.tsx` | `RunTraceList.onResolveDecision` | `~L90-L114` | `run-approval-card + onResolveDecision(runId, ev.id, approved)` | Displays inline Approve/Deny card with `ArgumentsRedactedJson` only while `ev.status` is `PendingApproval` |
| 7 | Agent Host | `services/agent-host/Api/ChatEndpoints.cs` | `ChatRunner.ResolveEventDecisionAsync` | `~L239-L240` | `POST /runs/{runId}/events/{eventId}/decision (ResolveRunEventDecisionRequest)` | Authenticated endpoint returning `204 NoContent` when `PendingApproval` event resolves or `404 NotFound` otherwise |
| 8 | Agent Host / SQL | `services/agent-host/Chat/ChatRunner.cs` | `ChatRunner.ResolveEventDecisionAsync` | `~L116-L134` | `ChatRunEvents (Approved/Denied, approval.resolved) + pendingApprovals.TryRemove` | Atomically transitions `PendingApproval` row via `ExecuteUpdateAsync` and signals TCS to resume execution or return denial |

## Contracts & Guardrails

- Follow [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md), and [coding-standards.md](../../../docs/coding-standards.md).
- Tier 0–3 permission boundaries, redaction, and HITL contracts: [phase-3.md](<../../../docs/masterplan/phase 3/phase-3.md>), [phase-3-contracts.md](<../../../docs/masterplan/phase 3/phase-3-contracts.md>), [phase-3 verification.md](<../../../docs/masterplan/phase 3/verification.md>), and [phase-4 verification.md](<../../../docs/masterplan/phase 4/verification.md>).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~ToolLoopChatRunnerTests|FullyQualifiedName~ToolPermissionGateTests|FullyQualifiedName~ExfiltrationGuardTests"`
- `npm --prefix apps/desktop test -- --run src/RunTraceList.test.tsx`
