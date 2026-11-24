---
id: chat-queue-send-now-and-recovery
title: "Chat Queue, Send-Now & Recovery"
area: chat-runtime-engines
triggers:
  - "POST /conversations/{id}/messages"
  - "POST /runs/{id}/promote"
  - "POST /runs/{id}/cancel"
  - "GET /attachments/{id}"
  - send_now
  - ChatRunner
  - ChatRecoveryService
  - ReconcileAsync
  - MutationGate
primary_files:
  - apps/desktop/src/ChatComposer.tsx
  - apps/desktop/src/App.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Chat/ChatRecovery.cs
  - apps/desktop/src/ChatTranscript.tsx
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0003-conversation-lifecycle.md
  - docs/decisions/0004-chat-attachments.md
  - docs/masterplan/phase 2/phase-2.md
uncertainties:
  - boundary: "Local SQL Server ChatRuns runtime state"
    location: "services/agent-host/Chat/ChatRecovery.cs::ChatRecoveryService"
    note: "Persisted ChatRuns, ChatMessages, and ChatAttachments rows live in the local SQL Server GuildhallDev instance at runtime."
    static_or_runtime: "runtime"
---

# Chat Queue, Send-Now & Recovery

Traces message & attachment submission, queue/send-now/promote/edit-replace lifecycle, 250ms/3s UI polling, `ChatRunner` checkpoints, and host restart recovery.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/ChatComposer.tsx` | `ChatComposer.addAttachments` | `~L112-L208` | `selectedFiles[]`, `deliveryMode` (`queue` or `send_now`, L354-L364), `queuedMessages` panel | Enforces `<=5` attachments, `<=20 MB` per file, `<=50 MB` total, and blocks non-edit sends when `queuedCount >= 8` (L384) |
| 2 | Desktop / UI | `apps/desktop/src/App.tsx` | `promoteQueuedMessage` | `~L630-L689` | `POST /runs/{id}/cancel`, `POST /runs/{id}/promote`, `GET /attachments/{id}`, 250ms `GET /conversations/{id}` poll | Guards actions with `chatOperation.current`; pairs 250ms active-run poll with 3s sidebar `refreshSummaries` poll (`L343-L358`) and revision monotonicity |
| 3 | Desktop / UI | `apps/desktop/src/App.tsx` | `sendMessage` | `~L915-L982` | `POST /conversations/{id}/messages` (JSON or multipart `FormData` with `clientRequestId`, `expectedConversationRevision`, `deliveryMode`) | On queued edit-replace, verifies original run is still `Queued` and cancels it before submitting replacement; preserves draft on failure |
| 4 | Agent Host / SQL | `services/agent-host/Api/ChatEndpoints.cs` | `ChatEndpoints.RequestFingerprint` | `~L445-L583` | `GET /attachments/{id}`, `POST /conversations/{id}/messages` -> `ChatRuns`/`ChatMessages`/`ChatAttachments` (plus `/promote` `L241-L282`, `/cancel` `L237-L238`) | Holds `MutationGate` + Serializable SQL tx; enforces `<=5`/`20MB`/`50MB`/`16KB` limits, SHA-256 `RequestFingerprint` + `clientRequestId` idempotency, OCC revision, max 8 queued runs, and negative-turn vacating on `send_now`/`promote` |
| 5 | Agent Host / SQL | `services/agent-host/Chat/ChatRunner.cs` | `ChatRunner.ExecuteAsync` | `~L29-L86` | `Channel<bool>` wake (Bounded capacity 1 `DropWrite`) -> `MutationGate` claim -> `RunAsync` (5-min `CancellationTokenSource`) | Runs `ChatRecoveryService.ReconcileAsync` before dequeuing; claims lowest `TurnNumber` `Queued` run atomically via `ExecuteUpdateAsync`; `CancelAsync` (`L88-L114`) cancels active CTS or marks `Queued`/orphan runs `Cancelled` |
| 6 | Agent Host / SQL | `services/agent-host/Chat/ChatRunner.cs` | `ChatRunner.SaveCheckpointAsync` | `~L266-L345` | `IBackingEngine.StreamTurnAsync` chunks -> `SaveCheckpointAsync` / `FinishAsync` (L509-L538) SQL `ExecuteUpdateAsync` | Enforces 131072-char output cap (`output_limit`) and persists DB checkpoints every 1024 chars or 400 ms; `FinishAsync` (L509-L538) preserves `Cancelling` -> `Cancelled` precedence |
| 7 | Agent Host / SQL | `services/agent-host/Chat/ChatRecovery.cs` | `ChatRecoveryService.ReconcileAsync` | `~L10-L18` | `ChatRuns` where `StartedUtc < hostStartedUtc` and `Status` in (`Running`, `Cancelling`, `Queued`) | Marks orphaned in-flight or queued runs from prior host lifetimes as `Interrupted` (`ErrorCode = host_restarted`) on startup |
| 8 | Desktop / UI | `apps/desktop/src/ChatTranscript.tsx` | `ChatTranscript` | `~L79-L249` | `conversation.messages` sorted by `turnNumber`, `queuedPairs` filtered out into `ChatComposer` queue-panel | Hides queued user/assistant pairs from main transcript until active; renders streaming `AssistantResponse` and mapped `ERROR_MESSAGES` / `renderEmptyMessageBody` (L13-L45) |

## Contracts & Guardrails

- [codebase-index](../../skills/codebase-index/SKILL.md)
- [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [0003-conversation-lifecycle.md](../../../docs/decisions/0003-conversation-lifecycle.md)
- [0004-chat-attachments.md](../../../docs/decisions/0004-chat-attachments.md)
- [phase-2.md](<../../../docs/masterplan/phase 2/phase-2.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~PersistenceTests|FullyQualifiedName~ToolLoopChatRunnerTests"`
- `npm --prefix apps/desktop test -- --run src/App.test.tsx`
