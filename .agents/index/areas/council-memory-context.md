---
area: council-memory-context
title: "Council, Memory & Context"
summary: "Council member profiles and lifecycle, preamble cache hot-reload, three-scope memory notes, and cross-turn context budgets with live VRAM."
primary_files:
  - apps/desktop/src/CouncilSidebar.tsx
  - apps/desktop/src/CouncilMember.tsx
  - apps/desktop/src/ConfirmationDialogs.tsx
  - apps/desktop/src/App.tsx
  - apps/desktop/src/MemoryNotesPanel.tsx
  - apps/desktop/src/ContextInspector.tsx
  - apps/desktop/src/WorkspaceSettingsPanel.tsx
  - apps/desktop/src/GuildhallBehaviorSettings.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Chat/PreambleCacheService.cs
  - services/agent-host/Preambles/global-guidelines.md
  - services/agent-host/Chat/ContextBuilder.cs
  - services/agent-host/Chat/ChatRunner.cs
  - services/agent-host/Providers/Ollama/OllamaDiscovery.cs
  - services/agent-host/Persistence/GuildhallDbContext.cs
  - services/agent-host/Program.cs
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 1/council-profiles-and-memory.md"
  - "docs/masterplan/phase 1/council-member-archive-migration-approval.md"
  - .agents/roster.md
---

# Council, Memory & Context

Council member profiles and lifecycle, memoized preamble hot-reload, explicit three-scope memory notes, cross-turn context budgets, and live Ollama VRAM inspection across Desktop UI, Agent Host, and SQL Server.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `apps/desktop/src/CouncilSidebar.tsx` | Council-grouped session sidebar, member collapse state, and portal member action menu | `collapsedMembers`, `memberMenuId` portal menu, database readiness gate | `CouncilSidebar`, `renderMember` |
| `apps/desktop/src/CouncilMember.tsx` | Member profile editor, portrait 512x512 JPEG crop, Markdown import/export, and MCP tool policy toggles | `uploadVersion` ref, `confirmUnrestricted` policy confirmation state, `customMcpOptions` | `CouncilMember`, `Portrait`, `stripMarkdownFrontmatter` |
| `apps/desktop/src/ConfirmationDialogs.tsx` | Accessible alertdialogs for archive/delete council member, delete/rename session, and save to memory | `archiveMemberTarget`, `deleteMemberTarget` focus traps | `ConfirmationDialogs` |
| `apps/desktop/src/App.tsx` | Per-member dirty draft protection, profile save/discard, and member lifecycle API orchestration | `profileDraftsRef`, `profileToolDraftsRef`, `profileMutation`, `chatOperation` | `saveMemberProfile`, `discardMemberProfile`, `addGuildMember`, `setGuildMemberArchived`, `deleteGuildMember` |
| `apps/desktop/src/MemoryNotesPanel.tsx` | Explicit CRUD and search UI for `AboutYou`, `Member`, and `Project` memory notes | `notes` list, `busyId`, `editingNote` modal | `MemoryNotesPanel` |
| `apps/desktop/src/ContextInspector.tsx` | Context usage, cross-turn budget meters, included source counts, MCP health, and live VRAM panel | `data` (`ContextInspectorData`), `vramOnly` (`VramResidency`), `probingServerId` | `ContextInspector` |
| `apps/desktop/src/WorkspaceSettingsPanel.tsx` | Lazy-loaded host for `GuildhallBehaviorSettings`, `AboutYou` memory, and `Project` memory panels | `accessPolicy`, `worktreeSettings`, `activeProject` scope | `WorkspaceSettingsPanel` |
| `apps/desktop/src/GuildhallBehaviorSettings.tsx` | Shared Guildhall behavior instruction editor with optimistic concurrency revision tracking | `saved` (`GuildhallBehavior`), draft dirty check | `GuildhallBehaviorSettings` |
| `services/agent-host/Api/ChatEndpoints.cs` | Loopback HTTP endpoints for `/agents`, `/preambles`, `/memory-notes`, `/guildhall-behavior`, `/conversations/{id}/context`, and `/vram` | `ChatRunner.MutationGate`, `Serializable` transactions for delete/lifecycle | `ChatEndpoints.MapChat` |
| `services/agent-host/Chat/PreambleCacheService.cs` | 50-100 MB capped `ConcurrentDictionary` preamble cache with `FileSystemWatcher` hot-reload | `_cache` (`ConcurrentDictionary`), `_capLock` (`Lock`), `_currentBytes` (`Interlocked`), `_watcher` | `PreambleCacheService`, `ReloadAll`, `GetGlobalGuidelines`, `GetMemberPreamble` |
| `services/agent-host/Preambles/global-guidelines.md` | Version-controlled global operational guidelines preamble protected from LRU eviction | `FileSystemWatcher` watched key `global-guidelines` | `global-guidelines.md` |
| `services/agent-host/Chat/ContextBuilder.cs` | Static system message assembly, three-scope memory selection, UTF-8 bytes/4 estimation, and cross-turn budget checks | `IOptionsMonitor<CrossTurnBudgetOptions>`, `Lazy<IPreambleCacheService>` | `ContextBuilder.InspectAsync`, `ContextBuilder.BuildStaticSystemMessage`, `ContextBuilder.EstimateTokens` |
| `services/agent-host/Chat/ChatRunner.cs` | Queued run execution consuming immutable `InstructionsSnapshot` and `SettingsSnapshotJson` captured at admission | `MutationGate` `SemaphoreSlim`, `active` `ConcurrentDictionary` | `ChatRunner.ExecuteAsync` |
| `services/agent-host/Providers/Ollama/OllamaDiscovery.cs` | Live Ollama `/api/ps` VRAM residency discovery with 3s timeout and 1 MiB response cap | Loopback `HttpClient` | `OllamaDiscovery.GetRunningModelsAsync` |
| `services/agent-host/Persistence/GuildhallDbContext.cs` | EF Core schema for `AgentDefinitions`, `GuildhallBehaviorSettings`, `MemoryNotes`, and `ChatRuns` | `Revision` `IsConcurrencyToken` on `AgentDefinition`, `GuildhallBehaviorSettings`, and `MemoryNote` | `GuildhallDbContext` |

## Workflows

- [`council-member-lifecycle`](../workflows/council-member-lifecycle.md) — Create, edit, archive, restore, and delete persistent council member profiles with dirty-draft protection, bounded portraits, Markdown import/export, and MCP tool permission policies.
- [`preamble-cache-hot-reload`](../workflows/preamble-cache-hot-reload.md) — Memoize version-controlled Markdown preambles in a 50-100 MB capped ConcurrentDictionary with FileSystemWatcher hot-reload affecting only subsequent runs and protecting global-guidelines from eviction.
- [`three-scope-memory-and-context-budget`](../workflows/three-scope-memory-and-context-budget.md) — Manage AboutYou, Member, and Project memory notes alongside Guildhall behavior settings, assemble bounded static system prompts with UTF-8 bytes/4 cross-turn budget checks, and inspect live Ollama VRAM residency.

## Contracts & References

- [codebase-index](../../skills/codebase-index/SKILL.md)
- [work-work](../../skills/work-work/SKILL.md)
- [coding-standards.md](../../../docs/coding-standards.md)
- [council-profiles-and-memory.md](<../../../docs/masterplan/phase 1/council-profiles-and-memory.md>)
- [council-member-archive-migration-approval.md](<../../../docs/masterplan/phase 1/council-member-archive-migration-approval.md>)
- [roster.md](../../../.agents/roster.md)
