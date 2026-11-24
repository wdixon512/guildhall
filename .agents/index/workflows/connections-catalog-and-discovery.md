---
id: connections-catalog-and-discovery
title: "Connections Catalog & Model Discovery"
area: chat-runtime-engines
triggers:
  - "GET /connections"
  - "POST /connections/{id}/probe"
  - "PUT /connections/{id}/api-key"
  - "POST /connections/{id}/sign-in"
  - ConnectionCatalogService
  - OllamaDiscovery
  - ConnectionsCatalog
  - agy-cli
primary_files:
  - apps/desktop/src/ConnectionsCatalog.tsx
  - apps/desktop/src/App.tsx
  - services/agent-host/Api/ConnectionEndpoints.cs
  - services/agent-host/Providers/ConnectionCatalogService.cs
  - services/agent-host/Providers/Ollama/OllamaDiscovery.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/masterplan/phase 2/phase-2.md
  - docs/masterplan/phase 2/phase-2-contracts.md
uncertainties:
  - boundary: "Local Ollama daemon and cloud endpoint runtime state"
    location: "services/agent-host/Providers/Ollama/OllamaDiscovery.cs::OllamaDiscovery"
    note: "Local Ollama daemon state (/api/tags and /api/ps) and external cloud endpoint reachability are runtime-only."
    static_or_runtime: "runtime"
---

# Connections Catalog & Model Discovery

Traces the 4-pillar connections catalog, live HTTP/CLI/MCP probes, API key DPAPI configuration, `agy-cli` model discovery, and Ollama `/api/tags` + `/api/ps` discovery.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/ConnectionsCatalog.tsx` | `ConnectionsCatalog` | `~L83-L195` | `PUT/DELETE /connections/{id}/api-key`, `POST /connections/{id}/probe`, `POST /connections/{id}/sign-in`, `onProbeAll` | Renders 4 pillars via `PILLAR_ORDER` (`L50-L55`), validates API keys `>= 8` chars before submission, and lazy-loads `PrivateInfrastructurePanel` for SQL Server MCP |
| 2 | Desktop / UI | `apps/desktop/src/App.tsx` | `probeAllConnections` | `~L769-L777` | `GET /connections?probe=true` -> `ConnectionsCatalogData` (plus startup `GET /models` & `/connections` in `refresh` `L270-L282`) | Prevents concurrent probe-all flights via `connectionsLoading` guard and preserves last catalog state on transient failure |
| 3 | Agent Host | `services/agent-host/Api/ConnectionEndpoints.cs` | `ConnectionEndpoints.MapConnections` | `~L19-L68` | `GET /connections?probe=...`, `PUT/DELETE /connections/{id}/api-key`, `POST /connections/{id}/probe`, `POST /connections/{id}/sign-in` | Restricts API key storage to `CloudApiIds` allowlist (`8..512` chars, stored in DPAPI) and re-probes after key mutations |
| 4 | Agent Host / Providers | `services/agent-host/Providers/ConnectionCatalogService.cs` | `ConnectionCatalogService.GetCatalogAsync` | `~L50-L114` | 4-pillar `ConnectionsCatalogResponse` (Local Runtimes, Cloud Model APIs, Local CLI Agent Harnesses, MCP & Service Integrations) | Supports full-catalog fast status or live probing (`runLiveProbes`) and single-entry `ProbeAsync` without throwing on offline providers |
| 5 | Providers | `services/agent-host/Providers/ConnectionCatalogService.cs` | `ConnectionCatalogService.BuildCloudEntryAsync` | `~L146-L214` | `ICredentialStore` (DPAPI) + live HTTP `GET /v1/models` (OpenAI, Anthropic) or `/v1beta/models?pageSize=1` (Gemini) | Never returns plaintext secrets in `ConnectionEntryResponse` (only `MaskedHint`) and maps `401`/`403` probe responses to `Needs sign-in` |
| 6 | Providers | `services/agent-host/Providers/ConnectionCatalogService.cs` | `ConnectionCatalogService.BuildCliEntryAsync` | `~L216-L365` | `ICliProcessRunner.ResolveExecutable` + local auth checks + `ProbeArgs` + `DiscoverAgyModelsAsync` (`agy models`, 5s timeout) | Runs CLI probes and `agy-cli` discovery under `EnvironmentScrubber`; falls back to `agy-default` on non-zero exit or non-cancellation errors (trap: L361 filters out `OperationCanceledException`, so 5s timeout propagates) |
| 7 | Providers | `services/agent-host/Providers/Ollama/OllamaDiscovery.cs` | `OllamaDiscovery.DiscoverAsync` | `~L8-L88` | `GET api/tags` (models) and `GET api/ps` (`GetRunningModelsAsync` VRAM residency) | Enforces 3s timeout, `MaxResponseBytes = 1_048_576` (1 MB) streaming cap, and `JsonDocumentOptions` `MaxDepth = 16` |

## Contracts & Guardrails

- [codebase-index](../../skills/codebase-index/SKILL.md)
- [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [phase-2.md](<../../../docs/masterplan/phase 2/phase-2.md>)
- [phase-2-contracts.md](<../../../docs/masterplan/phase 2/phase-2-contracts.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~ConnectionCatalogTests|FullyQualifiedName~ModelDiscoveryTests"`
- `npm --prefix apps/desktop test -- --run src/ConnectionsCatalog.test.tsx src/App.test.tsx`
