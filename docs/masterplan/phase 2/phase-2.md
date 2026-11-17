# Phase 2: Cloud Model APIs + Local CLI Agent Harnesses

**Status:** Implemented & verified  
**Date:** November 16, 2026  
**Migration:** `20261117002946_Phase2BackingEngines` (additive columns on `AgentDefinitions`, `Conversations`, and `ChatRuns`)

## Implemented Capabilities

1. **Unified Backing-Engine Abstraction (`IBackingEngine` / `IBackingEngineRegistry`):**
   - `OllamaBackingEngine`: Refactored local Ollama streaming behind `IBackingEngine`.
   - `ChatRunner`: Resolves the active `ProviderId` / `ModelId` / `HarnessMode` per run, checkpoints into SQL Server, and records `EngineKind`, `InputTokens`, `OutputTokens`, `CostSource`, and `DurationMs`.
2. **Pillar 1 — Direct Cloud Model APIs:**
   - `OpenAiCloudEngine` (`openai-api`), `GeminiCloudEngine` (`gemini-api`), and `AnthropicCloudEngine` (`anthropic-api`) using `IHttpClientFactory` named clients (`AllowAutoRedirect = false`) and shared `SseStreamReader`.
   - API keys are encrypted per Windows user via `DpapiCredentialStore` (`CryptProtectData` / `CryptUnprotectData` under `%LOCALAPPDATA%\Guildhall\credentials\`), never stored in `secrets.json`, frontend state, logs, or child process environments.
3. **Pillar 2 — Wrapped Local CLI Agent Harnesses:**
   - `CliHarnessEngine` + `CliHarnessDescriptors` + `CliProcessRunner` supporting `agy-cli` (`agy -p=<prompt> --output-format stream-json`), `codex-cli` (`codex exec --json`), `gemini-cli` (`gemini -p "" --output-format stream-json`), `claude-cli` (`claude -p --output-format stream-json --verbose`), and `cursor-cli` (`agent -p --output-format stream-json`).
   - Child processes receive a scrubbed environment (`EnvironmentScrubber`) and host-mediated permission flags (`read_only` default or explicit `workspace_write` for scoped Phase 2 testing; never `--yolo` or `--dangerously-skip-permissions`).
   - **Architectural Note for Phases 3–4:** Phase 2 shares `CliProcessRunner` and `CliHarnessEngine` for stateless single-turn subprocess execution while specializing per-CLI args, auth checks, and JSON event shapes. In Phases 3–4, when interactive tool-approval callbacks (`agy acp`, `codex app-server`, `claude` bidirectional `stream-json`) are added, split into dedicated per-CLI `IBackingEngine` adapters rather than overloading one shared parser.
4. **3-Pillar Connection Catalog & Backing-Engine Selection:**
   - `ConnectionCatalogService` + `ConnectionEndpoints` (`GET /connections`, `PUT/DELETE /connections/{id}/api-key`, `POST /connections/{id}/probe`).
   - Lazy-loaded `ConnectionsCatalog` React panel (`Connected`, `Not connected`, `Needs sign-in`, `Unavailable`, `Planned` for Phase 3/5 MCP connectors) and per-session Backing Engine / Model / CLI Mode selectors in `App.tsx`.
