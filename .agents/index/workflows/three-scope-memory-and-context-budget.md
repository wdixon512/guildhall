---
id: three-scope-memory-and-context-budget
title: "Three-Scope Memory Notes, Context Budget & Live VRAM"
area: council-memory-context
triggers:
  - "/memory-notes"
  - "/guildhall-behavior"
  - "/conversations/{id}/context"
  - "/vram"
  - "ContextBuilder"
  - "CrossTurnBudgetOptions"
  - "MemoryNotesPanel"
  - "ContextInspector"
  - "WorkspaceSettingsPanel"
primary_files:
  - apps/desktop/src/WorkspaceSettingsPanel.tsx
  - apps/desktop/src/GuildhallBehaviorSettings.tsx
  - apps/desktop/src/MemoryNotesPanel.tsx
  - apps/desktop/src/ContextInspector.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Chat/ContextBuilder.cs
  - services/agent-host/Providers/Ollama/OllamaDiscovery.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 1/council-profiles-and-memory.md"
uncertainties:
  - boundary: "Local Ollama daemon /api/ps runtime state"
    location: "services/agent-host/Providers/Ollama/OllamaDiscovery.cs::GetRunningModelsAsync"
    note: "Which models are actively resident in VRAM and what context length Ollama allocates at runtime depends on the local Ollama daemon /api/ps runtime state outside source control."
    static_or_runtime: "runtime"
  - boundary: "Local SQL Server GuildhallDev state"
    location: "services/agent-host/Persistence/GuildhallDbContext.cs::GuildhallDbContext"
    note: "Current persisted GuildhallBehaviorSettings, MemoryNotes, and AgentDefinitions rows and applied migration state in GuildhallDev depend on local SQL Server database state outside the repository checkout."
    static_or_runtime: "runtime"
---

# Three-Scope Memory Notes, Context Budget & Live VRAM

Manage AboutYou, Member, and Project memory notes alongside Guildhall behavior settings, assemble bounded static system prompts with UTF-8 bytes/4 cross-turn budget checks, and inspect live Ollama VRAM residency.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/WorkspaceSettingsPanel.tsx` | `MemoryNotesPanel` | `~L170-L197` | Lazy `<GuildhallBehaviorSettings />`, `<MemoryNotesPanel scope="AboutYou" />`, `<MemoryNotesPanel scope="Project" projectId={activeProject.id} />` | Project memory panel renders only when `activeProject` is selected; `GuildhallBehaviorSettings` and `MemoryNotesPanel` are deferred via `React.lazy` and `Suspense`. |
| 2 | Desktop / UI | `apps/desktop/src/MemoryNotesPanel.tsx` | `MemoryNotesPanel` | `~L7-L150` | `GET`/`POST /memory-notes`, `PATCH`/`DELETE /memory-notes/{id}` (scope `AboutYou`/`Member`/`Project`, `expectedRevision`) | Explicit user add/edit/enable/forget actions only without automatic extraction or model calls; enforces 1-2,000 char bodies and OCC `expectedRevision`. |
| 3 | Desktop / UI | `apps/desktop/src/ContextInspector.tsx` | `ContextInspector` | `~L13-L228` | `GET /conversations/{id}/context`, `GET /vram` -> `ContextInspectorData`, `McpServerHealthView[]`, `VramResidency` | Explicitly labels token counts as rough UTF-8 bytes/4 estimates, highlights `isOverTurnLimit`/`isOverSessionBudget`, renders Tier 5 MCP reconnect controls (L177-L204), and note L217 labels non-`AboutYou` notes as `Member`. |
| 4 | Agent Host / SQL | `services/agent-host/Api/ChatEndpoints.cs` | `MemoryNoteResponse` | `~L92-L236` | `GET`/`POST /memory-notes`, `PATCH`/`DELETE /memory-notes/{id:guid}`, `GET`/`PUT /guildhall-behavior` (`expectedRevision`) | Enforces strict scope ownership (`AboutYou` forbids agent/project IDs; `Member` requires `AgentDefinition`; `Project` requires `RegisteredProject`), 1-2,000 char notes, 1-4,000 char behavior, and `409 revision_conflict` OCC. |
| 5 | Agent Host / Providers | `services/agent-host/Api/ChatEndpoints.cs` | `ContextInspectorResponse` | `~L23-L83` | `GET /vram`, `GET /conversations/{id:guid}/context` -> `ContextInspectorResponse` + `McpServerHealthView[]` | Reads `GUILDHALL_USE_GPU` and clamped `GUILDHALL_MODEL_KEEP_ALIVE_SECONDS` (0-900); returns `409 settings_missing` when `GuildhallBehaviorSettings` row `Id=1` is absent. |
| 6 | Agent Host | `services/agent-host/Chat/ContextBuilder.cs` | `ContextBuilder` | `~L60-L221` | `CrossTurnBudgetOptions` (`MaxTurnContextTokens`, `MaxSessionTokens`), `ContextAssemblyResult`, `BuildStaticSystemMessage` | `InspectAsync` (L96-L106) selects up to 50 enabled notes across `AboutYou`, `Member`, and `Project`, while `BuildStaticSystemMessage` (L203-L221) currently injects only `AboutYou` and `Member` as data-only (omitting `Project` notes from static system prompt and `systemTokens`); blocks admission on `context_overflow` or `cross_turn_budget_exceeded`. |
| 7 | Agent Host / Providers | `services/agent-host/Providers/Ollama/OllamaDiscovery.cs` | `GetRunningModelsAsync` | `~L47-L90` | Loopback `HttpClient` `GET api/ps` -> `VramResidencyResponse` (`size`, `size_vram`, `context_length`, `expires_at`) | Enforces 3s timeout and 1 MiB `MaxResponseBytes` cap; degrades gracefully to `unavailable` or `invalid_response` when Ollama is offline. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) for `React.lazy` secondary UI panels, `IOptionsMonitor<T>`, `Lazy<T>`, and bounded loopback `HttpClient` timeouts/caps.
- Enforce explicit user-controlled memory notes (max 50 enabled notes queried; `AboutYou` and `Member` currently injected as data-only in `BuildStaticSystemMessage`), `Revision` OCC, and transparent UTF-8 bytes/4 budget limits per [council-profiles-and-memory.md](<../../../docs/masterplan/phase 1/council-profiles-and-memory.md>).
- Keep index cards synced via [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md).

## Targeted Verification

- `npm --prefix apps/desktop run test -- --run src/MemoryNotesPanel.test.tsx src/ContextInspector.test.tsx src/GuildhallBehaviorSettings.test.tsx`
- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~ContextBuilderTests|FullyQualifiedName~MemoryNotesTests"`
