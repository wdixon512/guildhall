# Phase 1 — local chat workspace plan

Status: implementation authorized, November 15, 2026. Work is constrained to the Phase 1 tasks below. Database permission upgrades, backups, inference, and useful-model downloads remain separate approval checkpoints.

## Outcome

Model retention follow-up: an opt-in `-KeepModelWarmSeconds 300` launcher setting requests five-minute retention, bounded to 0–900 seconds with default 0. Backend payload checks pass; live resource/performance QC remains part of C16/Q06.

November 16 quality follow-up: [agent quality and performance](agent-quality-and-performance.md) records SQL memory ownership, the captured Guildhall behavior policy and the thinking-off backend change. C16/Q06 must measure local model speed and answer quality. B04/Q06 draft-safe member editing is implemented on `codex/profile-draft-save` with automated UI coverage and independent code review; native keyboard/visual QC remains pending. B05/B06 memory and later tools/delegation retain their existing order. Backend code is prepared on `codex/backend`; live inference remains pending.

Guildhall behavior edit: the shared policy is now SQL-backed and editable in Workspace settings with explicit save. Accepted runs capture its text and revision; member-specific instructions remain separate. Migration `GuildhallBehaviorSettings` seeds the existing text and grants only table SELECT/UPDATE to the runtime identity. Isolated verification is in progress; GuildhallDev application requires separate approval.

Open Guildhall, choose a locally installed model for a guild member, send a message, see the answer arrive, cancel if needed, close/reopen the app, and continue the same session. Export the saved session in a documented format. No cloud account or external service is required after local dependencies/model are installed.

Accepted product choices: a council of persistent guild-member profiles, each with editable identity and instructions and multiple saved sessions; model changes within an existing session affect future replies; select useful local models at the live-testing checkpoint with separate download approval. The original one-assistant decision is superseded by the owner's council-first amendment; the underlying AgentDefinitions/Conversations relationship is extended instead of adding a second agent framework. See [decision 0002](../../decisions/0002-phase-1.md) and [council profile design](council-profiles-and-memory.md).

## Scope and engineering approach

Keep the Phase 0 stack, project structure, authenticated loopback connection, restricted SQL login, Windows migration identity, and PowerShell 7 requirement. Add ordinary host modules and focused desktop components. Use existing libraries; no orchestration framework, cloud adapter, new service, UI state framework, or tokenization package. A bounded SQL-backed host-lifetime queue was added by owner request for follow-up messages; this is not the later scheduled-task system.

The detailed, reviewable defaults are in [phase-1-contracts.md](phase-1-contracts.md). In particular:

- The existing SQL tables for assistant definitions, sessions, runs and messages are extended to represent multiple council members with multiple sessions. Store project-owned records and immutable per-run model/instruction provenance in SQL Server (`GuildhallDev`), which acts as the single source of truth for active conversation histories (no full-transcript RAM cache in MVP).
- Base instructions separate user-tunable parameters from global system guidelines (`GuildhallBehaviorSettings`) and are sent as static `system` preambles per API call. Version-controlled Markdown preambles on disk are memoized at runtime in a `.NET ConcurrentDictionary` (`50–100 MB` hard cap) with `FileSystemWatcher` hot-reloading that affects only subsequent prompts without wiping active conversations.
- One active inference across the host. Accepted messages can wait in a bounded per-session queue (up to eight); Send Now promotes a user message ahead of pending turns in that session. Model changes apply to future accepted turns; already queued runs keep their captured model. No branching, regeneration, or independent work after desktop/host shutdown. Queued work is marked Interrupted on host restart and is not replayed automatically.
- Authenticated POST creates a durable queued run; the host worker claims it for inference. The desktop polls committed snapshots and revisions; received text is visually smoothed without inventing content. Navigation does not stop a run.
- Keep partial responses labelled Cancelled/Failed/Interrupted. Never silently retry after failure/restart or feed incomplete turns back into model history.
- Preserve all history. Refuse an oversized context or exceeded **Cross-Turn Budget** (cumulative token usage and reasoning-effort limits across multi-turn sessions) with an explanation rather than silently crop or summarize it. Track **VRAM Residency** (`/api/ps`) and validate model-dependent settings (across CPU L3-cache/DDR5 execution and GPU VRAM offload) during live testing.
- Enforce hardcoded backend security guardrails in the ASP.NET Core host and strict process isolation between the Tauri UI and backend host. Safe Markdown/GFM rendering (never raw HTML), bounded requests/output, paginated history, and versioned JSON export. No arbitrary filesystem permissions.

Excluded: cloud inference, a second real provider, tools/MCP, full-text search, import, autonomous cross-member delegation, background jobs, remote access, vector memory, automatic backups, packaging/signing/deployment, and infrastructure changes. Persistent member profiles and multiple sessions per member are in scope through the accepted council amendment; this does not enable parallel inference (the host still permits one active inference). A narrow fake provider tests independence without building Phase 2. Attachment format limits and storage rules are in [decision 0004](../../decisions/0004-chat-attachments.md); that database migration was explicitly approved and applied to GuildhallDev on November 15, 2026.

**Owner-requested Phase 1 scope amendments:** add conversation archive, restore, and confirmed permanent deletion; rename and auto-title chats; visible response checkpoints, Stop, queued follow-ups and Send Now; attachments with local storage, safe Markdown, inline images and download actions; copy controls; keyboard scaling plus a display-scale setting; collapsible workspace sections; and explicit disabled placeholders for future workspace tools, provider connections, access modes, appearance options, usage, and scheduled tasks. Chats pin to the latest message on selection/send and while streaming unless the reader scrolls upward. These additions supersede the earlier exclusions for archive/delete, attachments and Markdown. Permanent deletion is refused while a response is active or queued. Runtime DELETE permission is limited to parent Conversations rows; SQL cascades remove dependent records. See [the roadmap amendments](../product-roadmap-amendments.md) for later capability boundaries.

**Council direction accepted:** persistent bot profiles with multiple sessions, profile-specific identity and Markdown instructions, reviewed shared “About you” preferences, and private bot/project memory. Profile schema/API/editor and member-grouped sessions are active; the SQL migration was applied to GuildhallDev on November 16, 2026. Shared/private memory is not implemented. See [council profiles and memory](council-profiles-and-memory.md).

## Changes the current code specifically needs

The current runtime SQL login can only SELECT metadata/history. Prepare table-specific SELECT/INSERT/UPDATE grants for the four new chat tables; the owner-requested lifecycle amendment adds DELETE only on Conversations, never on ChatRuns or ChatMessages. Do not grant DDL, db_owner, or db_datawriter. Existing Phase 0 provisioning refuses an existing database/login: do not reset or rerun it to upgrade.

The current CORS policy permits only GET and Authorization. Permit the specific new methods and Content-Type while preserving origin/Host/token checks. A separate streaming client must not inherit the status screen's short fetch timeout. Keep the existing status screen accessible during errors.

There is no run recovery or inference adapter yet. Add only the host-lifetime pieces needed for one conversation run; do not mistake async chat work or startup reconciliation for authorization to build Phase 6 scheduling/background services.

Before meaningful conversations accumulate, review and test native SQL backup/restore. Export has a different purpose and is not a backup substitute. New database/permission/backup actions will be presented as concrete scripts/SQL before execution; previous Phase 0 provisioning approval is not blanket approval for future changes.

## Small implementation tasks

Each task is one reviewable change. Start with its acceptance test and limit the edit scope below. Use existing test projects; split a task if it grows beyond one focused session. Paths are repository-relative.

### C01 — Add the chat schema

- Depends on plan approval. Files: `Persistence/` models/context/migration and `ChatSchemaSqlTests` in the backend test project.
- Add the four tables, singleton assistant seed, constraints/indexes and revisions described in the contract. No provider calls or default model hardcoding. Keep installation identity intact.
- Accept: an isolated Phase 0 database upgrades without lost metadata; repeated migration is a no-op; IDs/order/FKs/request uniqueness work; invalid duplicates are rejected. No migration on app startup.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter ChatSchemaSql` against explicitly approved scratch SQL. Generate migration SQL for review.
- Model: lightweight implementation; stronger reasoning for schema/constraint review.

### C02 — Prepare upgrade grants and backup/restore checks

- Depends C01. Files: narrowly scoped PowerShell 7 upgrade/backup scripts, setup notes, `ChatPermissionsSqlTests` and backup/restore test fixtures.
- Prepare additive runtime DML grants and native backup/restore commands with explicit targets and refusal to overwrite databases/files. Preserve runtime credentials and metadata/history read-only access. Do not create accounts or change services/firewalls.
- Accept: reviewed scripts distinguish plan/execute; permission tests can read/write the new chat tables, delete only conversation parent rows, and cannot directly delete run/message rows, write metadata/history, or change schema. Restore to a uniquely named owned scratch database reproduces known fixture records; source untouched. Document DPAPI credential recovery separately.
- Verify: script plan mode and parser checks; `dotnet test tests/Guildhall.AgentHost.Tests --filter ChatPermissionsSql`; actual backup/restore only after concrete approval. VERIFYONLY alone is insufficient.
- Model: stronger reasoning review of grants/restore safeguards; lightweight bounded scripting.

### C03 — Assistant and conversation persistence/API

- Depends C01; restricted-identity live verification follows approved C02 execution. Files: `Api/`, focused persistence queries/services, `ChatCrudSqlTests`.
- Add read/update for the one assistant, create/list/read/title/model changes for conversations, stable ordering/pagination, revision conflict handling, and strict JSON/body limits. Extend CORS only as specified. No generation endpoint in this task.
- Accept: settings/history survive a new host/context; model/default scopes are distinct; stale updates conflict; no multiple-assistant CRUD; unapproved origin/token/Host still fail; preflight allows only intended methods/headers.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter ChatCrudSql`; retain Phase 0 authentication/readiness tests.
- Model: lightweight.

### C04 — Build bounded, provider-neutral chat context

- Depends C01's records/contract. Files: one `Chat/ContextBuilder` module and `ChatContextTests`; no HTTP/provider calls.
- Construct instructions plus ordered Completed turns and the current user text. Exclude all incomplete turns. Read history in bounded batches; reject context overflow before creating a run. Keep saved history unchanged and surface estimates honestly.
- Accept: multi-turn order, model-independent text, changed instruction snapshot, failed/cancelled/interrupted exclusion, Unicode byte limits, and over-budget rejection are covered without an external model.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter ChatContext`.
- Model: lightweight implementation; stronger reasoning reviews history-selection and limit assumptions.

### C05 — Ollama streaming adapter

- Depends agreed normalized request/update contract. Files: `Providers/Ollama/` and `OllamaChatTests`; no run persistence/UI.
- Add `/api/chat` through existing HttpClient with bounded NDJSON parsing, upstream cancellation, startup/idle/total limits, normalized usage/errors, and required terminal marker. No raw thinking/tool execution or automatic model downloads/fallbacks.
- Accept: fixtures split JSON/UTF-8 across arbitrary chunks; cover slow first/body reads, empty/truncated/oversized streams, error records after HTTP 200, missing model, malformed records, cancellation, and unsupported tool output. A second fake implementation consumes the same contract.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter OllamaChat` with fake transport only.
- Model: lightweight after contract review; escalate a concrete parser/cancellation failure rather than adding an SDK.

### C06 — Durable run admission and duplicate-send handling

- Depends C03/C04. Files: one chat admission service, POST endpoint, `RunAdmissionSqlTests`. Use a fake runner; no real inference.
- Validate then commit run + message pair + configuration snapshot + conversation revision atomically. Add the host-wide gate and unique client request ID semantics. Busy/stale/conflicting requests do not create extra turns.
- Accept: simultaneous sends yield one accepted run; duplicate delivery returns its original run without duplicate messages/provider work; a failed transaction calls no runner; immutable snapshots do not change after settings edits.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter RunAdmissionSql` with real isolated SQL and deterministic fakes.
- Model: stronger reasoning for transaction, ownership, and idempotency review; keep implementation confined to admission.

### C07 — Run execution and committed text checkpoints

- Depends C05/C06. Files: one host-lifetime runner/checkpoint module, persistence operations, `RunExecutionSqlTests`.
- Consume normalized fragments, checkpoint bounded batches, and atomically save text/revision/terminal outcome. Use an EF context factory/owned scopes; no request-scoped context survives the POST and no database transaction spans provider I/O.
- Accept: output becomes observable before completion but only after persistence; final text/usage survives reopening; truncated/error streams retain labelled partial text; output/time/DB failures stop upstream work and never mark an unsaved answer Completed. Gate/resources are released on all paths.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter RunExecutionSql`; fake provider barriers control commit/failure timing.
- Model: stronger reasoning review; split checkpoint persistence from runner wiring if needed.

### C08 — Explicit cancellation and interrupted-run recovery

- Depends C07. Files: cancel endpoint, bounded startup/shutdown reconciliation, `RunRecoverySqlTests`.
- Implement idempotent cancellation and deterministic cancel/complete commit ordering. Reconcile abandoned runs before chat admission without blocking host liveness forever when SQL is down. Readiness reports recovery state; it does not run migrations. Use bounded retry/backoff, not a queue.
- Accept: cancel before/after first output, repeated cancel, concurrent completion, abrupt host exit, normal shutdown, and SQL outage/recovery are tested. Saved partial text survives; interrupted work never resumes automatically; no incomplete turn enters later context. Nothing stops shared SQL/Ollama services.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter RunRecoverySql`; include a bounded real child-host termination check against an owned test database.
- Model: stronger reasoning review of races and recovery; bounded implementation after state transitions are fixed.

### C09 — Authenticated snapshot SSE endpoint

- Depends C07/C08. Files: GET run/snapshot SSE endpoints and `SnapshotStreamSqlTests`.
- Stream the latest committed snapshot/revision, poll indexed state on a short interval, heartbeat, and close on terminal state. No durable event table or per-subscriber unbounded queue. Release reads/scopes promptly when disconnected.
- Accept: late attachment/reconnect sees the latest complete snapshot; skipped revisions are safe; terminal state is delivered; invalid auth/origin is rejected; SQL/transport failure is recoverable; a slow reader cannot exhaust memory or hold a transaction open.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter SnapshotStreamSql`; test real HTTP streaming behavior, not only serialized strings.
- Model: lightweight after the snapshot contract is frozen.

### C10 — Frontend streaming transport

- Depends C09's DTOs/fixtures. Files: desktop `src/api/` or split existing API module, `ChatTransport` tests. No visual redesign.
- Add typed JSON mutations and authenticated fetch streaming, bounded SSE framing (including split UTF-8/CRLF/multiple frames), revision handling, terminal detection, cancellation, and bounded read reconnects. No token query parameters or native EventSource.
- Accept: duplicate/older/wrong-run snapshots cannot corrupt text; first/late/terminal events parse; network failures never auto-submit a new turn; a lost POST reuses its client request ID. Navigation aborts observation, not the server run. Bound a single SSE frame (proposed 1 MiB) and reconnect count.
- Verify: `npm.cmd --prefix apps/desktop run test -- --run ChatTransport`; `npm.cmd --prefix apps/desktop run typecheck`.
- Model: lightweight; stronger reasoning only for a reproduced reconnect/idempotency fault.

### C11 — Conversation list and plain-text chat screen

- Depends C03/C10 contracts. Files: focused `ConversationList`, `ChatView`, `Composer` components, app wiring, component tests.
- Add create/open/history pagination and explicit Send with selected model visible. Preserve the connection-status screen. Render user/model text as text, allow multiline input, and provide accessible keyboard/focus/loading/error behavior. No router/state/Markdown framework.
- Accept: saved conversation history opens in order; controls show busy/loading/empty/error states; markup stays inert; loading older history does not jumble turn order; failed sends retain composer text and request identity for resolution.
- Verify: `npm.cmd --prefix apps/desktop run test -- --run ChatView`; typecheck/build.
- Model: lightweight.

### C12 — Wire live streaming, Stop, and reconnection into chat

- Depends C08–C11. Files: chat state hook, status/banner and action wiring, focused integration-style frontend tests.
- Apply snapshots by run ID/revision, show streaming/terminal/partial labels, add Stop, and link back to the active chat when navigating elsewhere. Global busy state explains why another send is unavailable. Reopening the app reloads persisted state rather than replaying the last POST.
- Accept: deterministic fake API demonstrates visible progressive output, stop, stale snapshots after navigation, disconnect/reconnect, host restart, and final failure states without duplicate text or generation. Connection errors preserve saved history.
- Verify: `npm.cmd --prefix apps/desktop run test -- --run ChatSession`; native-window behavior is checked again at C16.
- Model: lightweight; escalate only an unresolved cross-request state race.

### C13 — Assistant settings and model switching UI

- Depends C03/C11/C12. Files: small settings/model components and tests; no new native permission.
- Edit instructions/default model; select installed model for the active chat. Make scopes and future-turn behavior explicit. Disable/conflict active-run edits. Do not alter past run provenance or silently fall back if a model disappears.
- Accept: a new chat copies the configured default; an existing chat keeps its own selection; switching updates only the next accepted run; restarting preserves choices; missing model is actionable; settings revisions prevent lost updates.
- Verify: `npm.cmd --prefix apps/desktop run test -- --run AssistantSettings`; backend snapshot tests remain authoritative for persistence.
- Model: lightweight.

### C14 — Versioned conversation export endpoint

- Depends C03/C07/C08. Files: bounded export serializer/endpoint, format notes, `ChatExportSqlTests`.
- Export a consistent snapshot of one conversation in documented JSON v1, including run provenance/status/partials. No import and no model calls. Enforce size/consistency limits; never dump EF entities or sensitive provider configuration directly.
- Accept: complete/incomplete turns and Unicode survive JSON round-trip; concurrent checkpoints cannot mix incompatible run/text revisions; export contains no session token, credentials, endpoints, raw provider JSON, or hidden thinking.
- Verify: `dotnet test tests/Guildhall.AgentHost.Tests --filter ChatExportSql`.
- Model: lightweight.

### C15 — Native user-initiated export save *(Deferred to a later phase by owner direction)*

- **Status:** Deferred alongside C16/Q06. Do not block Phase 1 completion on native export UI/save verification.

### C16 — Approved local upgrade, model checkpoint, and final acceptance *(Deferred to a later phase by owner direction)*

- **Status:** Deferred by owner direction (November 16, 2026). Native JSON export verification, isolated SQL backup/restore verification (tracked under Phase 5), and formal live local-model speed/quality benchmarks (CPU L3-cache/DDR5 vs. GPU VRAM offload + `KeepModelWarmSeconds`, tracked under Phase 8) are moved out of the Phase 1 gate.

### C17 — Conversation archive, restore, and permanent delete (owner-requested)

- Depends on C03 and C11; active-run guard aligns with C08. Files: conversation model/migration, authenticated API, sidebar actions/filter, and isolated SQL/frontend workflow checks.
- Add reversible archive/restore state. Permanent delete requires a clear confirmation stating that SQL backups may retain copies. Refuse archive/delete while a response is Running or Cancelling. Grant runtime DELETE only on `dbo.Conversations`; foreign-key cascades remove only the selected conversation's run/message rows. Never grant DELETE on `ChatRuns` or `ChatMessages`.
- Accept: active Chats excludes archived rows; Archived lists them; restore returns them to Chats; canceling confirmation changes nothing; confirmation removes the selected terminal conversation and dependent rows; unrelated conversations and installation metadata remain intact; active runs cannot be hidden/deleted; runtime cannot directly delete run/message rows.
- Verify: focused frontend workflow tests and `dotnet test tests/Guildhall.AgentHost.Tests --filter 'Category=SqlIntegration'` against an authorized scratch SQL database. Review the exact additive SQL/grant before applying to `GuildhallDev`.
- Model: lightweight CRUD/UI implementation; stronger reasoning review for runtime permissions, cascades, and run-active races.

## Verification commands and planned checks

Use PowerShell 7. Existing commands remain the baseline; the new filtered test names and chat smoke script above are implementation targets, not files/tests already present or already passed.

```powershell
dotnet build Guildhall.slnx
dotnet test tests/Guildhall.AgentHost.Tests --filter 'Category!=SqlIntegration'
npm.cmd --prefix apps/desktop run typecheck
npm.cmd --prefix apps/desktop run test -- --run
npm.cmd --prefix apps/desktop run build
cargo test --locked --manifest-path apps/desktop/src-tauri/Cargo.toml
```

For authorized SQL checks on the same local instance:

```powershell
$env:GUILDHALL_TEST_SQL_CONNECTION = 'Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
./scripts/misc/Verify.ps1 -IncludeSql
./scripts/misc/Smoke-Host.ps1
```

Extend `Verify.ps1` to include new SQL categories and frontend tests without silently skipping them. SQL fixtures create/drop only owned scratch databases; transports/providers are deterministic fakes. Use real SQL Server for transactions, concurrency, permissions, and recovery; no EF in-memory substitute. Do not stop shared services or open ports to simulate failures.

Final native verification, after a live model is selected:

```powershell
npm.cmd --prefix apps/desktop run tauri -- build --debug --no-bundle
pwsh -NoProfile -File ./scripts/startup/Start-Dev.ps1 -BuiltDesktop
```

## Phase 1 completion checklist

Startup investigation: a single isolated backend probe reached authenticated health in 665 ms; no backend startup stall was reproduced. The launcher already waits for health before opening the desktop, and first React paint does not await backend requests. The reported white window remains a frontend/WebView startup issue to reproduce; no native first-paint improvement is claimed and the owner's existing window was not reloaded. See the companion QC record for timing scope.

Backend archive checkpoint (November 15, 2026, America/New_York): the additive CouncilMemberArchive migration and existing archive/restore API guards passed 2 isolated SQL tests and 33 deterministic backend tests. [The reviewed migration](council-member-archive-migration-approval.md) was applied to GuildhallDev after explicit approval; SQL is ready, observed data counts are unchanged, and all profiles initially remain unarchived. Migration commands now preview by default and apply with `-Execute`, using separate Release build output to preserve the running development session. The frontend session owns the UI and visual QC. This is Phase 1 reliability/profile work; later phases remain deferred.

- [ ] Existing Phase 0 health/status/authentication/model-discovery behavior still works; PowerShell 5.1 gives the explicit PowerShell 7 requirement.
- [ ] Approved additive SQL upgrade preserves Phase 0 metadata and uses only reviewed table grants. Runtime DDL and metadata/history mutation remain denied; conversation deletion is limited to `dbo.Conversations` with dependent-row cascades.
- [ ] Native backup restored into an isolated database reproduces known data; source and credentials remain intact. Export/recovery formats and limits are documented.
- [ ] Select a model and send a message in the native window. Answer text appears progressively through the host and is saved before display.
- [ ] Continue a second turn that uses the first completed turn's context. Close/reopen desktop and host, then continue the same conversation with history intact.
- [ ] Stop an active response. Partial text remains labelled Cancelled, and later turns exclude it. Repeated Stop and completion races settle to one durable outcome.
- [ ] Abruptly terminate only an owned host during a test run. Reopen: the run is Interrupted, partial text survives to the last committed snapshot, and inference never auto-resumes.
- [ ] Provider/SQL/stream failures are understandable and recoverable, with no duplicate send, false completion, silent fallback, or leaked sensitive data.
- [ ] Edit instructions and switch model within a conversation. Only future runs change; old provenance and saved messages remain intact. A deterministic fake proves model routing when only one real model is installed; a second download is not required to validate the UI/contract.
- [ ] Archive a conversation, find it in Archived, restore it, then permanently delete a terminal conversation after the explicit confirmation. Confirm active-run actions are refused and unrelated history remains.
- [ ] Context/output/time/request limits are tested. Oversized context is explained before generation; saved history is never silently cropped.
- [ ] Native JSON export saves/opens successfully and preserves complete and incomplete turn status plus model/instruction provenance, without secrets.
- [ ] Real local chat works without an external account; embedded runtime remains usable under simulated external-network failure. Record the exact offline-test scope rather than claim a physical network-disconnect test.
- [ ] Required automated/native/live checks are recorded with failures/skips explicit. No Phase 2–8 feature or unapproved system/deployment change was added.

## Coding-model use

Give a lightweight model `AGENTS.md`, its single task, relevant contract paragraphs, and only the files it may change. Provide exact fixtures and commands; do not preload the whole master plan. Default to lightweight implementation for CRUD, adapter mapping, transport parsing, UI, exports, scripts, and docs.

Use stronger reasoning for C01/C02's schema/permissions/recovery review, C04's context policy, and especially C06–C08's idempotency, transaction ordering, cancellation, and restart behavior. Escalate with a failing trace and a bounded question. No subagents, new framework, or larger model is required merely because a task has many routine edits; split the task first.

### Explicit model assignment

- **GPT-6 Luna (Medium):** C05 and C09–C15 — Ollama integration, streaming transport, chat UI, conversation history, model/settings UI, export UI, and the associated deterministic tests. Luna should work from the frozen contracts and keep changes task-scoped.
- **GPT-6.1 Sol (Medium):** C01–C04 and C06–C08 — schema and permissions review, context policy, run admission/idempotency, execution checkpoints, cancellation, and restart recovery. Sol should resolve integration issues and define any agent-host abstractions needed by those boundaries without introducing a framework.
- **Handoff rule:** Sol reviews the contracts and persistence/state transitions before Luna begins dependent UI or transport work. A failing cross-boundary trace is handed back with the smallest reproducer; do not switch models merely because a task has routine edits.

## Review boundary

The user has already chosen the three product options. Review the proposed engineering defaults and tasks next; no additional broad architecture questionnaire is needed. Implementation, SQL changes, backup execution, model downloads, inference, and deployments have not been performed as part of this planning work.

## End-of-phase functional QC

From `G:\repos\guildhall` in PowerShell 7, the user-facing QC command will be:

```powershell
pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -BuiltDesktop
```

Then exercise the phase's visible flows in the native window: open/create a conversation, select a local model, send a message, observe the response, cancel a response, restart, continue history, and export. I run the automated unit/deterministic checks separately. SQL integration, backup/restore, and live-model setup remain separate approval checkpoints.

### Phase 1 functional QC script

After the SQL migration/grants and a local model have been explicitly approved and applied:

```powershell
pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -BuiltDesktop
```

In the native window, verify:

1. The connection screen reports the host connected, SQL ready, and local models available.
2. Open **Local Assistant** and create a new conversation. The conversation appears with a stable title and selected model.
3. Send a short plain-text message. The user message appears exactly once, an assistant run starts, and answer text appears without HTML interpretation.
4. Send a second message that depends on the first answer. The existing conversation remains ordered and the second run uses the same conversation.
5. Change the model for the active conversation, then send another message. The new run records the new model while earlier messages retain their original provenance.
6. Edit assistant instructions, start a new conversation, and confirm the new conversation uses the updated instructions while the old run remains unchanged.
7. Start a deliberately slow response and press **Stop**. Partial text remains visibly cancelled; pressing Stop again does not create another run.
8. Close and reopen the desktop. Reopen the conversation and confirm saved messages and terminal/partial status are preserved; no request is submitted automatically.
9. Use **Export**, open the saved JSON, and confirm it contains conversation/messages/run status and model provenance but no bearer token, SQL credential, endpoint secret, or raw provider payload.
10. Trigger an unavailable-model or stopped-Ollama case. The UI shows an actionable failure, keeps the user text, and does not silently download, retry, or fall back.

Phase 1 passes only when these flows work in the native window and the automated checks are green. If the migration has not been applied yet, the expected current result is SQL **migration required** and chat remains unavailable; that is a setup checkpoint, not a failed UI test.


### Council member archive follow-up

Owner-authorized Phase 1 profile lifecycle: archive members reversibly while preserving all sessions. UI/API and isolated SQL/browser coverage are implemented; the single-column migration is approved and verified present in GuildhallDev. The [migration record](council-member-archive-migration-approval.md) includes the member archive/restore functional checklist. The master plan and later-phase exclusions remain unchanged.
