---
area: chat-runtime-engines
title: "Chat Runtime & Backing Engines"
summary: "Chat queuing, send-now interruption, streaming checkpoints, recovery, and local Ollama, Cloud SSE, and CLI harness engine adapters plus connection discovery."
primary_files:
  - apps/desktop/src/App.tsx
  - apps/desktop/src/ChatComposer.tsx
  - apps/desktop/src/ChatTranscript.tsx
  - apps/desktop/src/ConnectionsCatalog.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Api/ConnectionEndpoints.cs
  - services/agent-host/Chat/ChatRecovery.cs
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Configuration/EnvironmentScrubber.cs
  - services/agent-host/Providers/BackingEngineRegistry.cs
  - services/agent-host/Providers/ConnectionCatalogService.cs
  - services/agent-host/Providers/IBackingEngine.cs
  - services/agent-host/Providers/Cli/CliHarnessAdapters.cs
  - services/agent-host/Providers/Cli/CliHarnessDescriptors.cs
  - services/agent-host/Providers/Cli/CliHarnessEngine.cs
  - services/agent-host/Providers/Cli/CliProcessRunner.cs
  - services/agent-host/Providers/Cloud/AnthropicCloudEngine.cs
  - services/agent-host/Providers/Cloud/GeminiCloudEngine.cs
  - services/agent-host/Providers/Cloud/OpenAiCloudEngine.cs
  - services/agent-host/Providers/Cloud/SseStreamReader.cs
  - services/agent-host/Providers/Ollama/OllamaBackingEngine.cs
  - services/agent-host/Providers/Ollama/OllamaDiscovery.cs
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0003-conversation-lifecycle.md
  - docs/decisions/0004-chat-attachments.md
  - docs/decisions/0005-codebase-workflow-index.md
  - docs/masterplan/phase 2/phase-2.md
  - docs/masterplan/phase 2/phase-2-contracts.md
---

# Chat Runtime & Backing Engines

Covers chat message queuing, send-now interruption, checkpointed streaming, startup recovery, provider adapters, and 4-pillar connection/model discovery.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `apps/desktop/src/ChatComposer.tsx` | Message composer, attachment validation (<=5 files, 20 MB/file, 50 MB total), queue panel actions, delivery mode selector | `selectedFiles[]`, `deliveryMode` (`queue` or `send_now`), `editingQueuedRunId`, `queuedCount <= 8` | `ChatComposer`, `addAttachments`, `pasteIntoComposer` |
| `apps/desktop/src/App.tsx` | Active-run 250ms polling, sidebar 3s summary polling, send/edit-replace/promote/cancel orchestration, connection probing | `chatOperation.current` ref mutex, `draftsByKey`, 250ms & 3000ms intervals | `sendMessage`, `promoteQueuedMessage`, `editQueuedMessage`, `removeQueuedMessage`, `probeAllConnections` |
| `apps/desktop/src/ChatTranscript.tsx` | Transcript rendering, queued turn filtering, streaming markdown, `errorCode` status presentation | `queuedPairs` filter, `ERROR_MESSAGES` map | `ChatTranscript`, `renderEmptyMessageBody` |
| `services/agent-host/Api/ChatEndpoints.cs` | Chat message submission, idempotency, OCC, queue promotion/cancellation, attachment download | `ChatRunner.MutationGate` + Serializable SQL tx, `ChatRuns`/`ChatMessages`/`ChatAttachments` | `MapChat`, `POST /conversations/{id}/messages`, `POST /runs/{id}/promote`, `POST /runs/{id}/cancel`, `GET /attachments/{id}` |
| `services/agent-host/Chat/ChatRunner.cs` | BackgroundService dequeuing, 5-min execution timeout, 400ms/1024-char checkpoints, 131072-char cap, tool loop orchestration | `Channel<bool>` wake (capacity 1 `DropWrite`), `MutationGate` `SemaphoreSlim`, `active` `ConcurrentDictionary` | `Start`, `ExecuteAsync`, `RunAsync`, `CancelAsync`, `SaveCheckpointAsync`, `FinishAsync` |
| `services/agent-host/Chat/ChatRecovery.cs` | Host-startup reconciliation of interrupted or stale queued runs prior to `ChatRunner` dequeuing | `ChatRuns` table (`StartedUtc < hostStartedUtc` -> `Interrupted` / `host_restarted`) | `ChatRecoveryService.ReconcileAsync` |
| `services/agent-host/Providers/BackingEngineRegistry.cs` | `ProviderId` -> `IBackingEngine` resolution for local, cloud API, and CLI harness adapters | Case-insensitive `_engines` dictionary (empty defaults to `ollama`) | `Resolve`, `GetAll` |
| `services/agent-host/Providers/Ollama/OllamaBackingEngine.cs` | Local Ollama `/api/show` vision check and `/api/chat` NDJSON streaming with `<think>` wrapping and native/XML tool calls | `ollama-chat` `HttpClient`, `GUILDHALL_USE_GPU` (`num_gpu`), `GUILDHALL_MODEL_KEEP_ALIVE_SECONDS` (`0..900`) | `StreamTurnAsync`, `ExtractXmlToolCalls` |
| `services/agent-host/Providers/Cloud/SseStreamReader.cs` | Shared SSE `data:` frame reader and HTTP status -> `BackingEngineException` mapper for OpenAI, Gemini, Anthropic | 512 KB per-line SSE cap (`provider_record_too_large`) | `ReadDataEventsAsync`, `EnsureSuccessOrThrow`, `OpenAiCloudEngine`, `GeminiCloudEngine`, `AnthropicCloudEngine` |
| `services/agent-host/Providers/Cli/CliHarnessEngine.cs` | Base streaming engine for 5 CLI harnesses with scrubbed env, JSON-line parsing, and fenced `guildhall-tool-call` extraction | `EnvironmentScrubber` dict, `ICliProcessRunner` child process lifecycle | `CliHarnessEngineBase.StreamTurnAsync`, `BuildCompositePrompt`, `CliHarnessDescriptors.BuildRunArguments`, `CliProcessRunner` |
| `services/agent-host/Providers/ConnectionCatalogService.cs` | 4-pillar connections catalog aggregation, live HTTP/CLI/MCP probes, and `agy-cli` model discovery | `ICredentialStore` (DPAPI), local CLI auth files, 5s `agy models` timeout | `GetCatalogAsync`, `ProbeAsync`, `ResolveDefaultModelIdAsync`, `DiscoverAgyModelsAsync` |
| `services/agent-host/Providers/Ollama/OllamaDiscovery.cs` | Ollama `/api/tags` installed model discovery and `/api/ps` VRAM residency polling | 3s linked `CancellationTokenSource`, `MaxResponseBytes = 1_048_576` (1 MB), `MaxDepth = 16` | `DiscoverAsync`, `GetRunningModelsAsync` |
| `services/agent-host/Api/ConnectionEndpoints.cs` | HTTP API for catalog inspection, DPAPI key CRUD, live probing, and interactive CLI terminal sign-in | `CloudApiIds` allowlist, `ICredentialStore` | `MapConnections`, `GET /connections`, `POST /connections/{id}/probe`, `PUT/DELETE /connections/{id}/api-key`, `POST /connections/{id}/sign-in` |
| `apps/desktop/src/ConnectionsCatalog.tsx` | 4-pillar Connections UI, section tabs, DPAPI key form, live probe/sign-in/install actions, lazy SQL infra panel | `PILLAR_ORDER`, `busyId`, `editingId`, `PrivateInfrastructurePanel` `React.lazy` boundary | `ConnectionsCatalog`, `saveApiKey`, `deleteApiKey`, `probeOne`, `launchCliSignIn` |

## Workflows

- [`chat-queue-send-now-and-recovery`](../workflows/chat-queue-send-now-and-recovery.md) — Traces message & attachment submission, queue/send-now/promote/edit-replace lifecycle, 250ms/3s UI polling, `ChatRunner` checkpoints, and host restart recovery.
- [`backing-engine-streaming-adapters`](../workflows/backing-engine-streaming-adapters.md) — Traces `IBackingEngine` registry resolution and turn streaming across local Ollama, OpenAI/Gemini/Anthropic Cloud SSE engines, and 5 CLI harness subprocess adapters.
- [`connections-catalog-and-discovery`](../workflows/connections-catalog-and-discovery.md) — Traces the 4-pillar connections catalog, live HTTP/CLI/MCP probes, API key DPAPI configuration, `agy-cli` model discovery, and Ollama `/api/tags` + `/api/ps` discovery.

## Contracts & References

- [codebase-index](../../skills/codebase-index/SKILL.md)
- [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [0003-conversation-lifecycle.md](../../../docs/decisions/0003-conversation-lifecycle.md)
- [0004-chat-attachments.md](../../../docs/decisions/0004-chat-attachments.md)
- [0005-codebase-workflow-index.md](../../../docs/decisions/0005-codebase-workflow-index.md)
- [phase-2.md](<../../../docs/masterplan/phase 2/phase-2.md>)
- [phase-2-contracts.md](<../../../docs/masterplan/phase 2/phase-2-contracts.md>)
