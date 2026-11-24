---
id: backing-engine-streaming-adapters
title: "Backing Engine Streaming Adapters"
area: chat-runtime-engines
triggers:
  - IBackingEngine
  - BackingEngineRegistry
  - OllamaBackingEngine
  - OpenAiCloudEngine
  - GeminiCloudEngine
  - AnthropicCloudEngine
  - SseStreamReader
  - CliHarnessEngineBase
  - guildhall-tool-call
primary_files:
  - services/agent-host/Providers/IBackingEngine.cs
  - services/agent-host/Providers/BackingEngineRegistry.cs
  - services/agent-host/Providers/Ollama/OllamaBackingEngine.cs
  - services/agent-host/Providers/Cloud/OpenAiCloudEngine.cs
  - services/agent-host/Providers/Cloud/GeminiCloudEngine.cs
  - services/agent-host/Providers/Cloud/AnthropicCloudEngine.cs
  - services/agent-host/Providers/Cloud/SseStreamReader.cs
  - services/agent-host/Providers/Cli/CliHarnessEngine.cs
  - services/agent-host/Providers/Cli/CliHarnessDescriptors.cs
  - services/agent-host/Providers/Cli/CliProcessRunner.cs
  - services/agent-host/Providers/Cli/CliHarnessAdapters.cs
  - services/agent-host/Configuration/EnvironmentScrubber.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/masterplan/phase 2/phase-2.md
  - docs/masterplan/phase 2/phase-2-contracts.md
uncertainties:
  - boundary: "Local CLI binaries, OAuth sessions, and DPAPI keys"
    location: "services/agent-host/Providers/Cli/CliHarnessEngine.cs::CliHarnessEngineBase"
    note: "Encrypted *.dpapi files under %LOCALAPPDATA%\\Guildhall\\credentials, user-profile CLI auth files, and PATH executables live outside the repository checkout."
    static_or_runtime: "runtime"
---

# Backing Engine Streaming Adapters

Traces `IBackingEngine` registry resolution and turn streaming across local Ollama, OpenAI/Gemini/Anthropic Cloud SSE engines, and 5 CLI harness subprocess adapters.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Providers | `services/agent-host/Providers/IBackingEngine.cs` | `IBackingEngine` | `~L48-L93` | `BackingEngineRequest` -> `Func<BackingEngineChunk, CancellationToken, ValueTask>` -> `BackingEngineUsage` | Provider-agnostic streaming contract decoupling `ChatRunner` from Ollama, Cloud SSE, and CLI subprocess formats |
| 2 | Providers | `services/agent-host/Providers/BackingEngineRegistry.cs` | `BackingEngineRegistry.Resolve` | `~L3-L17` | `providerId` -> `Dictionary<string, IBackingEngine>` (`StringComparer.OrdinalIgnoreCase`) | Defaults blank `providerId` to `ollama`; unknown IDs return `null` so `ChatRunner` fails with `provider_unavailable` |
| 3 | Providers | `services/agent-host/Providers/Ollama/OllamaBackingEngine.cs` | `OllamaBackingEngine.StreamTurnAsync` | `~L20-L237` | `POST /api/show` (vision check) + `POST /api/chat` NDJSON (`num_gpu`, `keep_alive` `0..900s`, `tools`) | Rejects images on non-vision models (`model_does_not_support_images`), sets `num_gpu = 0` when `GUILDHALL_USE_GPU` is `false`, wraps thinking in `<think>...</think>`, caps lines at 256 KB, and falls back to `<tool_call>` regex via `ExtractXmlToolCalls` |
| 4 | Providers | `services/agent-host/Providers/Cloud/OpenAiCloudEngine.cs` | `OpenAiCloudEngine.StreamTurnAsync` | `~L16-L173` | `POST /v1/chat/completions` (Bearer DPAPI key, `stream_options.include_usage`, `choices[0].delta`) | Accumulates indexed streaming `delta.tool_calls` JSON fragments into finalized `BackingEngineToolCall` items on stream end |
| 5 | Providers | `services/agent-host/Providers/Cloud/GeminiCloudEngine.cs` | `GeminiCloudEngine.StreamTurnAsync` | `~L14-L157` | `POST /v1beta/models/{model}:streamGenerateContent?alt=sse` (`x-goog-api-key` header, `inlineData` images, `functionDeclarations`) | Maps assistant turns to `model` role and emits `functionCall` parts and `usageMetadata` token counts via `MeasuredApi` |
| 6 | Providers | `services/agent-host/Providers/Cloud/AnthropicCloudEngine.cs` | `AnthropicCloudEngine.StreamTurnAsync` | `~L15-L177` | `POST /v1/messages` (`x-api-key`, `anthropic-version` `2023-06-01`, `content_block_start/delta`, `message_start/delta`) | Assembles `partial_json` `tool_use` blocks by content index and merges `input_tokens` from `message_start` with `output_tokens` from `message_delta` |
| 7 | Providers | `services/agent-host/Providers/Cloud/SseStreamReader.cs` | `SseStreamReader.ReadDataEventsAsync` | `~L7-L60` | `Stream` -> SSE `data:` event strings; `HttpResponseMessage` -> `BackingEngineException` | Enforces 512 KB SSE line limit (`provider_record_too_large`) and maps `401`/`403` -> `provider_auth_failed`, `429` -> `provider_rate_limited`, `400`/`422` -> `provider_bad_request` |
| 8 | Agent Host / Security | `services/agent-host/Providers/Cli/CliHarnessEngine.cs` | `CliHarnessEngineBase.StreamTurnAsync` | `~L21-L135` | `CliHarnessDescriptors` (`codex-cli`, `agy-cli`, `claude-cli`, `cursor-cli`, `gemini-cli`) + `EnvironmentScrubber` -> `CliProcessRunner.StreamJsonLinesAsync` | Scrubs `GUILDHALL_`, `ConnectionStrings__`, `ASPNETCORE_`, `DOTNET_`, and secret env keys; blocks raw `guildhall-tool-call` fenced blocks from streaming to transcript and parses them into `BackingEngineToolCall`; maps `denied_actions` to `cli_permission_denied` |

## Contracts & Guardrails

- [codebase-index](../../skills/codebase-index/SKILL.md)
- [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [phase-2.md](<../../../docs/masterplan/phase 2/phase-2.md>)
- [phase-2-contracts.md](<../../../docs/masterplan/phase 2/phase-2-contracts.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~CloudEngineTests|FullyQualifiedName~CliHarnessTests"`
