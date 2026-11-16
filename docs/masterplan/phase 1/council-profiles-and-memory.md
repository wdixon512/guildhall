# Council profiles, sessions and memory

Status: profile schema/API, per-member editor, council-grouped sessions and the `20261116012111_CouncilProfiles` migration are implemented and applied to GuildhallDev on November 16, 2026. Isolated SQL, frontend and browser checks passed. Shared/member/project memory remains a later capability. This supplements the master plan without replacing or renumbering it.

## Confirmed product decisions

- A council member is a persistent bot with its own identity and job. A bot owns multiple saved sessions. Sessions own runs, messages and attachments.
- Default memory sharing is a reviewed shared **About you** collection plus private bot and project memory.
- The app remains a lightweight orchestrator. Connected runtimes execute inference. Memory storage/retrieval and avatar expressions must not require another always-running model or infrastructure service.

## Current behavior and known gap

SQL stores session history, per-run instruction/model snapshots and attachments. Completed turns from the selected session are sent back to the model. The new profile implementation gives each council member its own SQL identity and Markdown instructions, and each session points to its member. Before the migration is applied, the database and running desktop still use the old shared identity. There is no implemented council-wide or cross-session long-term memory.

Old device preferences do not contain per-session appearance history. An upgrade cannot reconstruct portraits/names that were overwritten. The upgrade preserves all sessions and gives each legacy session its own initial bot profile, seeded from the current assistant definition. The app imports the previous device appearance into the first profile only; other legacy profiles keep their independent SQL defaults. This preserves separation instead of copying the shared appearance into every profile. Existing run snapshots and message bodies stay unchanged. Never merge distinct legacy sessions automatically merely because their current appearance matches.

## Ownership and navigation

The sidebar lists council members, with sessions nested beneath each member. **Add member** creates an independent profile. **New session** starts another job for that selected member. Archiving a session preserves the member and its memory. A clean session starts with its bot's rules and relevant approved memory, without copying another session's entire transcript.

Extend the existing AgentDefinitions/Conversation foreign-key relationship rather than inventing a second agent framework. AgentDefinitions owns name, role, portrait selection/custom portrait, accent, default provider/model and versioned Markdown instructions. Conversation remains the internal session record and may override the default model for that session's future replies. The model actually used by each run is immutable. Existing queued messages retain the model/instructions selected when accepted. Updating the bot does not rewrite in-flight work or historical provenance.

Appearance/profile data belongs in SQL, not global browser storage. LocalStorage can retain device-specific scale and pane preferences. Uploaded profile images must remain bounded and authenticated; bundled portrait assets can be referenced by stable IDs.

## Markdown rules, preamble caching, and context management

Each member has an editable **Instructions / rules** Markdown document with ordinary source editing and a safe preview.

### Modular Instruction Separation & Static System Preambles

- **Static System Preambles:** Base instructions are assembled and sent as a static `system` message (or Modelfile parameter) once per backend API request rather than repeated across conversational turns.
- **Context Preservation:** Conversational continuity is maintained by passing the historical message array of completed turns back to the model API per turn, supplemented by KV caching on warm/larger models and planned backend state compression.
- **Modular Instruction Separation:**
  - **Tunable Parameters (User/Config-facing):** Council Member persona/role definitions, primary task objectives, and operational mode toggles (e.g., reasoning effort / thinking controls).
  - **Global Guidelines (System-wide):** Concise operational boundaries (`GuildhallBehaviorSettings`) instructing agents to rely strictly on tool outputs, respect workspace scope, and never fabricate system states, citations, or benchmark metrics.

### Canonical Storage vs. Runtime Memory (Preamble Memoization & SQL History)

- **Markdown Files & SQL Provenance:** Version-controlled Markdown files on disk serve as the source of truth for base agent preambles and configurations, paired with SQL profile records (`AgentDefinitions`) and immutable per-run snapshots (`ChatRuns`). Offer explicit Markdown import/export and authorized file sync without scanning arbitrary local directories.
- **RAM Memoization (`50–100 MB` Hard Cap):** Load compiled preambles and agent metadata into an in-memory store (`.NET ConcurrentDictionary` in the desktop host runtime; Redis optional only for distributed scale) at runtime to eliminate SQL/disk read bottlenecks per turn. Enforce a strict hard cap of **50–100 MB** for preamble and metadata caching.
- **Hot-Reloading Safety (`FileSystemWatcher`):** Use file watchers to invalidate and refresh the in-memory `ConcurrentDictionary` cache when source Markdown files change on disk. Reloading preambles affects only subsequent prompts/runs and never wipes or alters active conversations.
- **Conversation State Persistence (SQL Server Single Source of Truth):** SQL Server (`GuildhallDev`) acts as the single source of truth for active conversation histories, ensuring durability across application restarts. Storing full active conversation histories in RAM is unnecessary for the MVP; SQL Server provides sufficient performance and reliability for a single-user desktop architecture without added cache-sync complexity.

Workspace policy, the bot's instructions and optional session-specific instructions have distinct visible scopes. Show which rules and memories are included in the next run. The ASP.NET Core host enforces hardcoded backend security guardrails (strict read-only file access defaults, credential/sensitive-data exfiltration prevention, mandatory human-in-the-loop approval gates, and Tauri/Host process isolation) independently of all model instructions. Retrieved documents and memory notes are context data and cannot grant tool access.

Capture rules and selected memory provenance when admitting a run. Apply edits to future runs. Keep the input bounded to the selected model's supported context and track **Cross-Turn Budgets** (cumulative token usage and reasoning effort limits across multi-turn sessions); expose exclusions/limits instead of silently promising unlimited retention. Do not label a rough token estimate as an exact measurement.

## Memory layers

1. **Session context:** the current job's retained transcript; completed turns are normal context. An explicit Send Now interruption may carry the interrupted user request forward, without treating an incomplete assistant answer as a completed reply.
2. **About you:** user-reviewed facts/preferences intended for every council member, such as a preferred name or response style.
3. **Member memory:** approved notes relevant only to this bot's job, reusable across its sessions.
4. **Project memory:** approved project facts, conventions and decisions, scoped to a real authorized workspace once project support exists.

Remembering means storing/retrieving application records, not training or changing model weights. Switching Ollama models or providers should preserve these records and their scopes.

Start with explicit small notes in SQL. Provide add/edit/disable/forget controls and show scope, source session when known, timestamps and revision. **Save to memory** is an explicit user action. Automatic extraction/summaries can be considered later with reviewed suggestions; they are not required for the first working memory feature and must not add a model request after every message.

Shared memory should contain only deliberately shared facts. A bot does not search another bot's private notes or project notes outside the active workspace. Project memory remains unavailable until its workspace boundary exists. Do not infer authority from remembered text, store credentials in memory, or silently promote untrusted attachments into global facts.

Forgetting a memory stops future retrieval of that record. The original transcript and existing backups are separate retention surfaces. Deleting a source session should not silently destroy an independently saved memory note; make that relationship visible. Deleting/archiving a bot and its notes requires clearly reviewed retention behavior.

## Small implementation tasks

### B01 — Profile schema and upgrade — complete

Extend AgentDefinitions with bounded identity/appearance fields and per-profile revisions; reuse its existing instructions/default-model fields. Prepare an explicit legacy-session-to-profile mapping that creates an independent profile per existing session. Preserve all run/message/attachment data. Prepare exact SQL and table-specific runtime grants for review before applying them. Do not reset GuildhallDev.

Acceptance: two profiles can be edited independently; existing sessions retain stable IDs/history; repeat migration is a no-op; runtime cannot alter schemas. Stronger reasoning model for migration/ownership review; lightweight model for the bounded entity/DTO edits.

### B02 — Profile API and editor — identity/rules and reversible archive implemented; archive migration applied

List/create/update/archive member profiles through the host. Move name/role/portrait/accent persistence from device-wide storage to the selected profile, with optimistic concurrency. Reuse the current Council editor, static assets and image bounds. Scope each session's profile lookup explicitly.

Acceptance: change Maren's portrait and instructions; another member and its sessions stay unchanged; restart retains both. Missing/deleted profiles show a recoverable state. Lightweight implementation; stronger review for identity scope.

### B03 — Council-first sidebar — complete

Group existing sessions beneath their profile. Separate Add member and New session. Preserve search, rename, archive/delete confirmation, queue, model switching and scroll-follow behavior. No generic multi-agent orchestration library.

Acceptance: two members with two sessions each remain distinct after relaunch; switching sessions restores the right editor, model and history. Lightweight UI implementation plus independent UI review.

### B04 — Markdown instructions & preamble memoization — source editor complete; safe preview/import-export & file-watcher memoization remain

Add scoped source editor, safe preview, explicit Markdown file import/export, and runtime RAM memoization (`.NET ConcurrentDictionary` capped at **50–100 MB** with `FileSystemWatcher` hot-reloading when source Markdown files change), with length limits and revision conflicts. Snapshot compiled instructions for each accepted run so hot-reloading affects only subsequent prompts without wiping active conversations. Do not treat a role label or portrait change as an instruction update.

Acceptance: different bot rules affect new replies independently; hot-reloaded Markdown changes update the RAM cache for subsequent runs while leaving active conversations intact; session/model changes retain the selected member's rules; previous/queued run snapshots remain immutable. Lightweight implementation with deterministic provider assertions.

### B05 — Explicit shared and member memory

Add a small versioned SQL MemoryNotes table with shared/member scope, optional owning member, body, enabled state, provenance and timestamps. Prepare migration/grants separately. Project scope waits for the real workspace entity. Provide plain Markdown note editing and scoped list/search, without a vector database.

Acceptance: an approved About you note is available to two members; a private member note is available only to its owner; disabled/forgotten notes disappear from future context. Restart/model changes preserve the notes. Stronger reasoning review for scope/retention; lightweight CRUD implementation.

### B06 — Context assembly, cross-turn budgets, and inspector

Assemble static system preambles (combining system-wide global guidelines and member tunable parameters from the memoized cache), applicable approved notes, and the session transcript from SQL Server. Record selected note IDs/revisions, context limits, and cumulative **Cross-Turn Budgets** (token usage and reasoning-effort limits across multi-turn sessions) in run provenance. Show included sources, scopes, cross-turn budget consumption, and live **VRAM Residency** (`/api/ps`) in Context usage. No automatic whole-history sharing, full-conversation RAM caching, embeddings or second model call.

Acceptance: fake-provider payload assertions prove isolation, static system preamble placement, and scope; oversized input or exceeded cross-turn budget gives an actionable error; the inspector matches the actual request. Stronger reasoning model for context budgeting and invariants.

### B07 — Functional QC and migration checkpoint

Generate the exact additive SQL and permission changes, verify them only on approved GuildhallTest_* databases, and present GuildhallDev application as the final approval step. Record browser and native checks separately. Keep semantic search, automatic extraction, autonomous cross-bot delegation and background memory consolidation out of this increment.

Verification commands after implementation:

```powershell
npm --prefix apps/desktop run build
npm --prefix apps/desktop run test -- --run
dotnet test tests/Guildhall.AgentHost.Tests --filter Category=SqlIntegration
pwsh -NoProfile -File G:\repos\guildhall\scripts\startup\Start-Dev.ps1
```

The agent runs automated/isolated SQL checks with the approved test connection. Owner QC is the native window: create two members and sessions, change one portrait/rule, save shared and private notes, relaunch, switch models, inspect context, disable a note, and confirm the other bot remains independent.

## Member archival follow-up

The owner chose **archive the member and keep its sessions**. Right-click a member heading or use its named actions button to archive with confirmation. Archived members remain visible in an expandable list with their saved sessions; Restore member returns them to the council. New sessions require an active member. Existing sessions stay usable, and member archiving never deletes history or changes conversation archive flags. Running or queued work blocks the lifecycle change. See the [migration approval and functional QC](council-member-archive-migration-approval.md); GuildhallDev now has this approved schema update; read-only verification reports SQL ready. Memory remains outside this follow-up.
