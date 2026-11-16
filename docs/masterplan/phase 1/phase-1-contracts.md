# Phase 1 contracts — proposed for review

This began as a planning artifact; owner-approved amendments and implementation below supersede the original single-flight/no-queue assumptions where stated. See [the Phase 1 plan](phase-1.md) and [roadmap amendments](../product-roadmap-amendments.md).

## Existing boundaries to preserve

The frontend talks only to the authenticated local host. Only the host talks to SQL Server and the configured loopback Ollama endpoint. Keep the existing native bootstrap command, token handling, local certificate exception, PowerShell 7 requirement, and process ownership. No public endpoints, cloud accounts, or additional services.

Phase 0 CORS currently permits only GET and Authorization. Chat adds exactly GET/POST/PATCH and the Content-Type header, with preflight tests; do not broaden origins or native capabilities. Streaming and downloads use fetch with the existing bearer header. Native EventSource's constructor supplies URL/credential mode, not an arbitrary authorization-header option, so it is not the client transport for this token design.

## Data and normalized provider boundary

Add four application tables; keep installation metadata and EF migration history intact:

- **AgentDefinitions:** persistent council member profiles with identity, instructions, nullable default model ID, lifecycle state, and settings revision. The legacy Local Assistant row seeds instructions but no hardcoded permanent model. Member create, update, archive/restore, and confirmed permanent delete are supported.
- **Conversations:** ID, title, agent ID, nullable selected model ID, UTC created/updated timestamps, archive flag/time, and optimistic revision. A new chat copies the assistant's default model if one is configured. The model selector changes this conversation only; changing the assistant's default affects new chats.
- **ChatRuns:** ID, conversation ID, monotonically ordered turn number, client request ID, request fingerprint, status, UTC start/update/finish timestamps, sequence/revision, stable error code, and immutable snapshot of agent instructions, provider ID, requested model ID, and generation/context settings. Optional normalized usage and observed model metadata are populated only when reported.
- **ChatMessages:** ID, run ID, role (`user` or `assistant`), and Unicode text. Each run owns exactly one user message and one assistant message; unique `(runId, role)`. Sort history by run turn number and role, not timestamps. A cancelled/failed run retains its pair and saved partial assistant text.

Use project-owned IDs, UTC timestamps, foreign keys, unique `(conversationId, turnNumber)` and `(conversationId, clientRequestId)` indexes, explicit length limits, and EF concurrency checks. Run status supplies assistant-message completion status; do not maintain conflicting copies. Provider-native request/response JSON is not the application record. Plain-text messages are sufficient now; tool/content-block schemas can be added by an explicit migration when required.

The initial provider contract contains a normalized text-history request and an asynchronous sequence of answer-text fragments followed by terminal outcome/optional usage. Only Ollama and deterministic test fakes implement it. Keep this interface inside the existing host; do not build a plugin loader, cloud adapter, capability registry, or general-purpose agent loop.

The Ollama adapter POSTs `/api/chat` with streaming enabled, parses bounded NDJSON records across arbitrary byte/UTF-8 boundaries, requires a valid terminal `done` record, and maps failures to stable application codes. HTTP EOF without that marker is an interrupted provider response, not success. Handle error objects within a 200 stream. Do not persist/display hidden thinking or send tools; an unexpected tool-call request fails safely as unsupported and never invokes anything.

## Starting a turn and preventing duplicates

`POST /conversations/{id}/messages` accepts `clientRequestId`, `expectedConversationRevision`, text, attachments, and a delivery mode (`queue` or `send_now`). The server validates readiness, input limits, selected model, context budget, and queue bounds before admitting the turn.

In one SQL transaction: claim the next turn number, create a Queued run with immutable configuration snapshot, insert the user message and empty assistant message, and advance conversation revision. Only after commit may the host worker claim it and contact Ollama. Return 202 with run/message IDs and conversation revision.

Use one host-wide inference slot and a bounded SQL-backed queue (up to eight queued turns per session). The worker selects only the earliest queued turn in each session, then the oldest eligible turn across sessions. `send_now` promotes the new user turn ahead of that session's pending turns by changing only their order; completed runs remain immutable. `POST /runs/{id}/promote` accepts `{ expectedConversationRevision }`, reorders that existing queued run ahead of other queued turns in the same session, cancels an active run in that session, and wakes the existing runner. It neither duplicates the run nor resubmits a provider request. Stale revisions and runs no longer Queued return 409. Removing a queued turn is durable and does not touch an active turn. A selected-model update affects future accepted requests; each queued run keeps its original model/instruction snapshot. The worker is host-lifetime only: it does not survive app/host shutdown. The runner owns its EF scopes; never retain a request-scoped DbContext in a detached task.

A duplicate client request ID with the same fingerprint returns the existing run, including its terminal state; it never calls the provider twice. Fingerprint the canonical submitted payload, not mutable server settings. A different payload with that ID is 409 `request_conflict`. Check duplicates before treating an old expected revision as a conflict. Stale conversation revisions return 409 and require refresh. The UI retains the request ID while resolving a lost POST response, looks up/reissues that same request, and never automatically creates a new generation.

## Streaming, persistence, and reconnect

The desktop polls committed run snapshots about every 250 ms. Each response includes run identity, monotonic sequence/revision, UTC timestamps, status, assistant message ID, complete saved assistant text so far, and optional stable error/usage fields. The UI replaces text for a newer sequence; it never appends a snapshot as a delta. Ignore duplicate/older revisions and data for a different run. The earlier SSE proposal below is historical and is not the current transport.

Conversation summaries expose nullable `lastResponseUtc`, the finish time of the latest terminal assistant result that has output: a Completed run counts even when its answer text is empty, and other terminal runs count only when partial assistant text is non-empty. A queued turn cancelled before producing output does not create an unread response. Assistant messages expose nullable `responseDurationSeconds`, measured from the run's current `StartedUtc` to `FinishedUtc`. `StartedUtc` is set when accepted into the queue, so this is total response time including queue wait and must not be described as inference duration. Both fields remain null until a qualifying terminal assistant result exists.

The runner batches provider fragments into SQL checkpoints: flush on a short cadence while output arrives (target 500 ms), a small pending-text threshold (target 4 KiB), and every terminal transition. Do not promise a timer guarantee while a provider is stalled. Bound provider-fragment size and buffer size before accepting more output. Commit assistant text, run status, and incremented sequence atomically; only committed text may reach the UI. On a crash, only the uncommitted suffix can be lost, and it was never shown as durable text.

For this single-user slice, polling reads committed run state about every 250 ms with one request in flight. A reader can skip intermediate revisions because each snapshot is complete. Do not hold a SQL connection/transaction across a provider call.

Historical SSE proposal, superseded by the polling transport above: on connect/reconnect, send the latest committed snapshot immediately; no historical token replay is promised. A terminal snapshot is authoritative and ends the stream. A transient stream failure triggers bounded GET reconnects only, never another generation POST. After a retry limit, show reconnect/refresh controls. Disconnecting SSE alone does not cancel inference; cancellation is explicit. No session token in a URL, persisted browser state, or exported file.

## Cancellation, failure, and host restart

Run transitions are `Queued -> Running -> Completed | Failed | Cancelled | Interrupted`, with `Cancelling` as a transient state between a cancellation request and `Cancelled`. Queued runs can transition directly to Cancelled or Interrupted.

- Cancel is idempotent. A durable cancellation request prevents later provider completion from overwriting it. If completion commits first, return Completed rather than relabel it. Propagate cancellation to the upstream HTTP call, stop accepting fragments, save the current partial text with Cancelled, and release admission. Verify the provider receives cancellation; do not promise instant GPU unloading or kill the Ollama process.
- A run completes only after the adapter's valid terminal record and a successful final SQL commit. Provider failure/timeout/output-limit saves partial text as Failed with a stable code, including a provider terminal reason indicating its output-token limit. Partial text is visibly labelled and never looks like a completed answer; no automatic continuation is attempted.
- If SQL fails during generation, cancel upstream work, report unavailable/unknown state honestly, and block further generation until reconciliation. Preserve the last committed text. When SQL returns, reconcile that abandoned run before admission; never replay inference. If the host restarts first, recover it as Interrupted.
- On host startup, reconcile persisted Queued/Running/Cancelling runs to Interrupted before the worker reclaims work. Queued work is never automatically replayed after restart. `/health` remains liveness; `/ready` gates expected schema plus completed recovery. If SQL is unavailable, keep status/error UI usable and deny chat admission.
- Normal host shutdown requests cancellation with a bounded final-checkpoint attempt. Abrupt termination is handled on next startup. Closing the desktop still stops its owned host; independent work after window close is Phase 6.

Use stable error codes such as `model_missing`, `provider_unavailable`, `invalid_provider_response`, `unsupported_response`, `generation_timeout`, `output_limit`, `context_limit`, and `database_unavailable`. Never expose raw provider error bodies, SQL text, credentials, or connection strings. Logs contain IDs/status/duration/codes, not conversation text, full instructions, or answer bodies.

## Model/instruction changes and context limits

Snapshot the model, current assistant instructions, and bounded parameters when admitting a run. Future settings changes cannot mutate historical runs or in-flight requests. Switching models retains the transcript and applies only to the next accepted run. A disappearing/unsupported model produces a clear error; never download a model or silently fall back.

Context is the instruction snapshot, prior **Completed** turns as intact user/assistant pairs, and the current user message. Exclude cancelled/failed/interrupted turns by default. A Send Now turn includes only the user prompt of the active run it interrupted, never its partial assistant output or other queued prompts. Keep excluded turns visible and exported. Explain this rule briefly in the UI.

Persist all history. Phase 1 sends full eligible history until a conservative configured context threshold is reached, then rejects with `context_limit` before committing a new turn or contacting Ollama. Offer a new chat/copy action; no silent truncation, automatic summarization, or semantic memory. Prompt byte/count limits are application bounds, not an exact tokenizer. Choose and document tested context/output settings at the live-model checkpoint; show estimates as estimates.

Proposed upper bounds for fixtures and validation: user text 16 KiB UTF-8, instructions 8 KiB, answer text 128 KiB, one provider NDJSON record 256 KiB, and request body 32 KiB. Generation defaults: 120 seconds to first answer text, 60 seconds without progress, and 5 minutes total; reaching any bound cancels upstream and yields a stable state. First-output and idle timers must also bound a stalled response body read, not just header receipt. Context-window/output-token limits remain conservative configuration validated against the selected live model, not an arbitrary UI parameter editor.

## API surface proposed

- Existing authenticated `/health`, `/ready`, `/models` remain.
- `GET /agents`, `POST /agents`, `PATCH /agents/{id}`, and `DELETE /agents/{id}?expectedRevision=...`: manage council member profiles. Delete permanently removes the member's saved sessions and their dependent histories after confirmation; it returns 409 for stale revisions or linked active/queued work.
- `GET /assistant` and `PATCH /assistant`: legacy read/update contract for the seeded assistant's instructions/default model using its expected revision.
- `GET /conversations?after=...&limit=...&archived=false` and `POST /conversations`: paginated active/archived list and explicit creation; bounded title, nullable initial model.
- `GET /conversations/{id}`: conversation metadata and ordered, paginated turns/messages with run states. Expose the active run ID and conversation revision.
- `PATCH /conversations/{id}`: title/selected model/archive state with expected revision; no branching. Archive/restore is rejected while a run is active.
- `DELETE /conversations/{id}`: permanently delete one conversation and its dependent history; reject while a run is active. The UI requires explicit confirmation and states that database backups may retain copies.
- `POST /conversations/{id}/messages`: start/deduplicate one run as above.
- `GET /runs/{id}`: latest committed snapshot for recovery/diagnosis.
- `GET /runs/{id}/events`: SSE snapshots through authenticated fetch.
- `POST /runs/{id}/cancel`: idempotent durable cancellation request.
- `GET /conversations/{id}/export`: consistent versioned JSON including settings/model provenance, messages, run status, saved partials, and optional normalized usage. Never credentials, tokens, endpoint secrets, raw provider payloads, or hidden thinking.

All mutations validate JSON and expected revisions. Use stable bounded error responses and appropriate 400/404/409/413/503 statuses. Model existence and conservative context failures are rejected before generation. Paginate history/list endpoints; do not return unbounded transcripts. For export, materialize a consistent bounded read (proposed maximum 8 MiB) in a short transaction, release database resources, then send the file with no provider calls. Return a clear limit error if it is too large. Use explicit bounded read isolation for snapshots/exports rather than enabling database-wide snapshot-isolation settings as a shortcut.

## SQL access, export, and recovery

Runtime initially has SELECT only on installation metadata/history. Prepare an additive migration plus a separate reviewed grant change allowing SELECT/INSERT/UPDATE on the four new chat tables only. The owner-requested conversation lifecycle amendment already grants DELETE on `dbo.Conversations` only; cascades remove its runs/messages, while direct DELETE on those child tables remains denied. Confirmed council-member deletion additionally requires a separate narrow `DELETE` grant on `dbo.AgentDefinitions`; the API deletes that member's conversation parent rows first in the same transaction, relying on existing cascades. The member-delete grant migration does not change the existing Conversations permission and adds no direct DELETE permission for ChatRuns, ChatMessages, or ChatAttachments. No DDL, server roles, db_owner, or blanket db_datawriter grants. Keep existing metadata/history read-only. Reuse the established credentials; never rerun Phase 0 provisioning to reset the database.

Before applying to GuildhallDev, generate/review SQL, test on an isolated approved scratch database, and prove native backup/restore to another scratch database without overwriting the source. Restore testing compares known fixture data, metadata, and row counts without printing private content. Database backups do not back up DPAPI credentials; document recreating a restricted login and saving a new protected credential on another machine.

JSON export uses an authenticated fetch and user-initiated save of the returned Blob; test the actual Tauri window. Do not add broad filesystem/shell access for export. If WebView download behavior requires a native save command, review the smallest user-selected-file capability before adding it. Plain text rendering is the baseline; no raw HTML execution. Import and automatic backup scheduling are later work.

## Primary references checked

- [Ollama chat API](https://docs.ollama.com/api/chat): message history, streaming switch, terminal outcome, and usage fields.
- [Ollama streaming](https://docs.ollama.com/api/streaming): NDJSON framing.
- [WHATWG server-sent events](https://html.spec.whatwg.org/multipage/server-sent-events.html): EventSource API, UTF-8 SSE framing and event IDs.
- [EF Core migration application](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying): reviewable SQL and separate runtime/migration permissions.
- [SQL Server full backups](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/create-a-full-database-backup-sql-server?view=sql-server-ver17): backup scope, permissions, and version constraints.

## Council member lifecycle (owner-approved Phase 1 follow-up)

`AgentProfileResponse` includes `isArchived`. `GET /agents` includes active and archived members. `PATCH /agents/{id}/lifecycle` accepts `{ isArchived, expectedRevision }`; it returns the updated profile, 404 for an unknown member, 409 `revision_conflict` for a stale revision, or 409 `member_has_active_run` when any linked session has Running/Cancelling/Queued replies. Successful changes increment only the profile revision and archive flag. An already-matching state with the current revision is a no-op. Lifecycle changes share the chat mutation gate with run admission; session creation uses the same gate when checking member state.

`POST /conversations` returns 409 `member_archived` for an archived member. Archiving does not change any session archive flag or delete sessions, messages, attachments, or run snapshots. Existing sessions remain accessible and usable. Restore re-enables new sessions. `DELETE /agents/{id}?expectedRevision=...` permanently deletes the selected member and all of that member's sessions after explicit frontend confirmation. It shares the runner mutation gate and a serializable SQL transaction, rejects stale revisions and any linked Queued/Running/Cancelling run, and cascades through existing Conversation→Run→Message→Attachment foreign keys. Other members and sessions are retained. The legacy seed assistant is protected until it has at least one saved session. The sidebar groups archived members separately with a visible collapsible disclosure; its saved sessions still follow the existing Chats/Archived filter and loaded-history limit. Applying the member DELETE grant requires [separate approval](council-member-deletion-permission-approval.md); the migration must not be applied to GuildhallDev without that approval.
