---
id: preamble-cache-hot-reload
title: "Preamble RAM Cache & FileSystemWatcher Hot-Reload"
area: council-memory-context
triggers:
  - "/preambles"
  - "PreambleCacheService"
  - "PreambleCacheOptions"
  - "global-guidelines.md"
  - "FileSystemWatcher"
  - "GetGlobalGuidelines"
  - "GetMemberPreamble"
primary_files:
  - services/agent-host/Program.cs
  - services/agent-host/Chat/PreambleCacheService.cs
  - services/agent-host/Preambles/global-guidelines.md
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Chat/ChatRunner.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 1/council-profiles-and-memory.md"
uncertainties: []
---

# Preamble RAM Cache & FileSystemWatcher Hot-Reload

Memoize version-controlled Markdown preambles in a 50-100 MB capped ConcurrentDictionary with FileSystemWatcher hot-reload affecting only subsequent runs and protecting global-guidelines from eviction.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host | `services/agent-host/Program.cs` | `PreambleCacheService` | `~L25-L30` | `PreambleCacheOptions` (`PreambleCache` config section), `IPreambleCacheService` singleton, `Lazy<IPreambleCacheService>` | Registered with `ValidateDataAnnotations().ValidateOnStart()` enforcing `[Range(MinCapBytes, MaxCapBytes)]` (50-100 MB) and `ValidateScopes`/`ValidateOnBuild`. |
| 2 | Agent Host | `services/agent-host/Chat/PreambleCacheService.cs` | `PreambleCacheService` | `~L44-L133` | `_cache` (`ConcurrentDictionary<string, PreambleEntry>`), `_capLock` (`Lock`), `FileSystemWatcher` (`*.md`, `IncludeSubdirectories=true`) | `MaxCacheBytes` clamps to 50-100 MB (`MinCapBytes`..`MaxCapBytes`); startup `ReloadAll` populates RAM cache solely from `_rootDirectory` without scanning arbitrary local folders. |
| 3 | Agent Host | `services/agent-host/Preambles/global-guidelines.md` | `Guildhall Global Guidelines` | `~L1-L7` | Key `global-guidelines` in `PreambleCacheService` | System-wide global operational boundaries; treated as static system preamble input and explicitly exempt from cache eviction. |
| 4 | Agent Host | `services/agent-host/Chat/PreambleCacheService.cs` | `TryLoadFileWithRetry` | `~L209-L276` | `FileSystemWatcher` `Created`/`Changed`/`Deleted`/`Renamed`/`Error` -> `_capLock` + `_cache` + `_currentBytes` | `TryLoadFileWithRetry` retries transient `IOException`/`UnauthorizedAccessException` up to 4 attempts; `OnWatcherError` triggers full `ReloadAll()` recovery. |
| 5 | Agent Host | `services/agent-host/Chat/PreambleCacheService.cs` | `EvictToFit` | `~L278-L337` | `_capLock`, `EvictToFit` (oldest `LoadedUtc` first), `_currentBytes` (`Interlocked`), SHA-256 `ContentHash` | `EvictToFit` never evicts `global-guidelines` or the incoming `protectKey`; entries exceeding `MaxCacheBytes` or un-fittable after eviction are skipped with a warning. |
| 6 | Agent Host | `services/agent-host/Api/ChatEndpoints.cs` | `PreambleCacheSnapshotResponse` | `~L84-L91` | `GET /preambles` -> `PreambleCacheSnapshotResponse` (`CurrentCacheBytes`, `MaxCacheBytes`, `Count`, `PreambleSummary[]`) | Protected by `LocalApiBoundary` loopback + constant-time bearer token check; reads memoized `ListAll()` entries without touching disk or SQL. |
| 7 | Agent Host | `services/agent-host/Chat/ChatRunner.cs` | `SettingsSnapshotJson` | `~L176-L198` | `ChatRun.InstructionsSnapshot` + `ChatRun.SettingsSnapshotJson` -> `ContextBuilder.BuildStaticSystemMessage` | Hot-reloading preambles affects only subsequent runs because accepted runs consume the immutable `InstructionsSnapshot` and `SettingsSnapshotJson` persisted at message admission. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) for `ValidateOnStart()` options, `Lazy<T>` DI, and bounded `ConcurrentDictionary` memoization without full-transcript RAM caches.
- Keep `global-guidelines` static, exempt from LRU eviction, and isolated from active queued/running `ChatRun` snapshots per [council-profiles-and-memory.md](<../../../docs/masterplan/phase 1/council-profiles-and-memory.md>).
- Maintain index synchronization via [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~PreambleCacheTests"`
