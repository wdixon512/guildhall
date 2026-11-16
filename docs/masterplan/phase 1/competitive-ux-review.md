# Guildhall vs Codex, Claude Code and Cursor

Research date: November 15, 2026. Scope: product workflows, UI/UX, current repository implementation and master-plan coverage. This is a research and prioritization proposal, not authorization to implement later phases or change the master plan.

## Finding

Guildhall currently offers persistent local text chat inside an emerging agent-workspace shell. The reference products provide a larger working loop: establish context, plan, act with tools, observe progress, steer or stop, review changes, verify results and recover. Visual similarity is useful, but functional parity depends on that loop. Our first priority should be making state understandable and controllable, then adding safe agency.

The master architecture remains suitable: a project-owned host, provider adapters, SQL records, tool policies and independent execution lifetimes can support these capabilities. Several UX requirements are only implicit in the roadmap and need explicit acceptance criteria.

## Method and limits

Reviewed current `apps/desktop/src/App.tsx`, `api.ts`, `style.css`, the host's `ChatRunner`, `ChatEndpoints`, `Program`, persistence records, development launcher, Phase 1 contracts, QC plan and master roadmap at `docs/masterplan/personal_ai_ecosystem_master_plan.md`. Earlier screenshots and owner QC reports provide observed behavior. This review did not exercise fresh live competitors or certify every current Guildhall workflow.

Competitor research uses official documentation. Surface matters: Codex desktop, CLI and IDE extension; Claude Code desktop and terminal; Cursor editor/Agents Window and selected tool documentation. A documented capability on one surface does not prove identical behavior elsewhere. Account, version, OS, model and deployment affect availability. Some old Codex links redirect to ChatGPT Learn; the citations below identify the actual destination. An inaccessible guessed route was not used as evidence.

No numerical parity score: the workflows have unequal importance, and a styled placeholder is not a working feature. Status below distinguishes present behavior, unfinished planned work and proposed roadmap additions.

## What to learn from each product

### Codex: project work, isolation and review

Codex's desktop worktree workflow isolates parallel chats by checkout and supports moving work between local and worktree environments. Its review pane supports line-specific feedback and staging/reverting at several levels. CLI guidance documents resume, fork, compaction and session status. Together these suggest making the chat's project/environment and its changes inspectable. Do not equate independent chat histories with independent filesystem execution. [Worktrees](https://learn.chatgpt.com/docs/environments/git-worktrees), [code review](https://learn.chatgpt.com/docs/code-review?surface=app), [long-running chat practices](https://learn.chatgpt.com/docs/learn/best-practices).

### Claude Code: understandable context and session control

The desktop documentation describes a Context card with category breakdown, `/context`, and manual/automatic compaction. It also covers multiple sessions, attachments, panes, interruption and session organization. These are strong references for exposing what the agent is doing without requiring a separate dashboard visit. Terminal conventions should become desktop controls rather than being copied mechanically. [Desktop application](https://code.claude.com/docs/en/desktop).

Claude's context walkthrough explains that instructions, files, tool information and conversation consume the window; compaction summarizes history and reloads several forms of durable configuration. The important design lesson is that saving a transcript and supplying active model context are different operations. [Context window](https://code.claude.com/docs/en/context-window).

### Cursor: context selection close to the work

Cursor documents explicit mentions for files, folders, terminals, chats and diffs, plus a context ring with a category breakdown. Its agent supports code changes and checkpoints; restoring files and shortening a conversation have different effects. Its browser tools can inspect console/network state and interact with an application. These patterns connect evidence gathering, editing and verification in one workflow. [Prompting and context](https://cursor.com/docs/agent/prompting), [Agent](https://cursor.com/help/ai-features/agent), [browser](https://cursor.com/docs/agent/tools/browser).

## Current implementation: the gaps we should fix first

### G01 — Active context has no visible budget or provenance

Current: the runner sends the instruction snapshot, all earlier Completed message pairs, then the user prompt. It requests `num_ctx=4096`. There is no actual context-budget enforcement, category accounting, model-limit negotiation, summary mechanism or user-visible included/excluded list. The planned reject-on-overflow policy is not implemented. The model catalog's advertised maximum is not our configured active window, and neither is evidence of reliable recall.

Consequence: conversations may be stored correctly while older material is not usable by the model; failures can look like broken memory. We must not claim every long request is silently truncated without checking provider behavior, but we currently cannot explain what happened.

Recommendation: P1 Context Inspector, specified below. Later compaction must be an explicit policy change to Phase 1's existing no-silent-summarization contract. Priority: critical.

### G02 — Run state does not provide control or recovery

Current: React polls the viewed conversation; navigation during Running has been unlocked. There is no Stop endpoint/control or implemented startup reconciliation. Detached tasks receive `CancellationToken.None`. Errors can leave abandoned Running rows; there is no global active-run view. The runner saves the answer only after consuming the response and accepts EOF without requiring valid terminal completion.

Recommendation: finish P1 cancellation, output checkpoints, terminal validation, timeouts, recovery and honest failure labels. A global active-run badge should remain usable when viewing another conversation, with return-to-run and Stop. Accept: stop during output, restart during output, reopen without replay, failure preserves labelled committed text. Priority: critical; already planned P1.

### G03 — Duplicate sends and concurrency are not safely managed

Current: persisted request IDs and unique indexes are partial safeguards. The client creates a fresh ID on each attempt; duplicate lookup does not load its assistant message. There is no host-wide admission gate, queue or controlled ownership of in-flight tasks. Async tasks permit overlap but do not establish safe concurrent sessions.

Recommendation: implement reviewed idempotency/admission contracts and resolve a lost POST with its original identity. P1 allows browsing and creating chats while one host generation runs; a second generation gets a clear busy state. Later parallel work needs bounded concurrency, CPU/GPU limits, cancellation ownership and workspace isolation. Priority: critical, P1 then P4/P6/P7.

### G04 — Conversation lifecycle is incomplete

Current: list displays up to 25 recent records, search filters only those loaded titles, history response is unbounded, titles often remain New conversation, and numbering is a display fallback. No archive, restore, delete, rename UI, fork, pin or full-history search. A nonempty draft still blocks navigation rather than being stored per conversation. On narrow windows the sidebar disappears without an equivalent full history picker.

Recommendation: finish pagination, meaningful titles/rename, per-chat drafts, responsive history access and archive/restore. Owner has explicitly requested delete: define confirmation, active-run handling, retention and permanent deletion behavior; never make a hidden soft-delete promise that implies erasure. Concrete SQL changes still require review. Archive/delete were excluded from the original P1 contract; propose recording this owner-requested scope change. Fork/edit-and-resend can follow in a separate conversation-management increment. Priority: high.

### G05 — Composer and response ergonomics remain basic

Current: Enter/Shift+Enter, model selector, text input and safe plain-text output work. No copy actions, explicit retry controls, message timestamps, persisted per-turn model labels, attachment chips or markdown/code-block rendering. Scrolling only follows when already near the bottom and needs functional checks for long chats. The model selector exposes names without capability/resource guidance.

Recommendation: add focus/draft preservation, jump to latest, accessible loading/error announcements, copy, visible model provenance and missing-model actions. Safe code formatting/markdown needs a scoped rendering decision; do not insert raw HTML. Attachments require extraction/vision/context limits, not only an upload button. Priority: high for basic controls, later increment for attachments/rendering.

### G06 — Assistant settings and export lack their UI workflows

Current: a singleton assistant has backend read/update endpoints, but no instructions/default-model settings UI. Conversation export is absent. The second pane is explicitly a visual preview; Agents/Tools/Activity rail controls are disabled.

Recommendation: finish P1 settings with future-turn scope and revision conflicts; implement native user-initiated JSON export and verify its saved file. Treat previews as design scaffolding, never as completed functionality. Priority: high, already P1.

### G07 — Resource policy is invisible

Current: CPU mode, four threads, 4096 context and unload-after-reply are request defaults; GPU is a launcher option. There is no resource indicator or in-app profile. Changing models can change resource consumption dramatically even when the UI looks identical.

Recommendation: show CPU/GPU preference, actual execution data when available, context setting and load/unload state. Provide a gaming-friendly profile and unload action; disclose that CPU work can still compete with a game. Do not claim a requested policy is observed GPU residency. Avoid introducing a continuous telemetry service just to draw a chart. Priority: high for visible policy, later measured telemetry.

## Agent-workspace capabilities beyond current chat

### G08 — Workspace selection and context acquisition

Missing: registered repositories/folders, file picker/mentions, selected snippets, scoped search, terminal output and changed-file references. No app agent currently reads our repository. Current development-agent filesystem access belongs to the coding tool building Guildhall, not to Guildhall itself.

Plan coverage: workspace registration in P4, read-only tools in P3, documents later. Gap: specify workspace picker, trust boundaries and included-source chips. File/search tools can precede an editor; do not clone a complete IDE to enable grounded questions.

### G09 — Agent loop and permission modes

Missing: tool selection/execution loop, inspectable calls/results, approvals and read-only/plan/edit execution modes. Existing loopback authentication protects the API; it is not a tool sandbox. A card named Agent does not create an acting agent.

Claude documents allow/ask/deny rules and permission modes; Cursor separates run modes from sandbox restrictions. Guildhall should distinguish intent (Ask/Plan/Act), permission policy and enforcement, with a concrete action preview and scoped remembered approvals. Read-only must be enforced below the model. [Claude permissions](https://code.claude.com/docs/en/permissions), [Cursor run modes](https://cursor.com/docs/agent/security/run-modes).

Plan coverage: P3 read-only and P4 controlled writes. Add explicit policy-resolution, denied-action and permission-revocation acceptance cases.

### G10 — MCP management is an operational workflow

Missing: install/configuration, discovery, authentication, server status/logs, per-tool scope, reconnect and process cleanup. Cursor describes server/tool configuration and expandable call arguments/results. Claude documents status, scope and connection-failure handling. These show why an MCP catalog alone is insufficient. [Cursor MCP](https://cursor.com/docs/mcp), [Claude MCP](https://code.claude.com/docs/en/mcp).

Plan coverage: P3 local registry/lifecycle/tools, P5 private remote tools. Add disabled/starting/connected/degraded/failed states and distinctions between configure, connect and authorize. MCP server permission and each action's authority remain distinct; tool catalog overhead also belongs in context accounting.

### G11 — Changes, review and checkpoints

Missing: patch preview, dirty-file ownership, changed-file list, line comments, staged decisions, rollback and checkpoints. Proposed actions vs already-applied edits must be clearly distinguished. Git/file rollback cannot undo an external email, SQL write or network side effect.

Plan coverage: P4 worktree/sandbox, edit tools and diff approvals. Add a review loop that takes a finding back into the agent with the exact file/lines and verifies the revised result. Use an existing editor for full editing before considering an embedded IDE. Codex's line feedback and Cursor's file-only checkpoint semantics are useful precedents; see the product sections above.

### G12 — Verification the agent can perform itself

Missing: autonomous native-app interaction, owned dev-server controls, browser/native screenshots tied to a run, evidence of checks and reviewable output artifacts. The current layout-check script uses synthetic services in Chromium; it does not prove Tauri bootstrap, actual SQL, streaming, model output or native export.

Plan coverage: companion QC plan. Complete it before declaring P1 done; later expose approved testing tools to Guildhall's own agent. Accept a failing fixture scenario, a fixed native flow, correct checkout/build identity, and cleanup without owner clicks. Browser testing can verify UI behavior; it cannot by itself prove every native dialog or OS effect. Cursor's browser workflow is a useful reference, not evidence that our harness already exists.

### G13 — Steering, queueing and notifications

Missing: Stop-and-revise, queued prompts, steering during work, follow-up questions, durable task progress and inactive-chat notifications. Enter currently sends a turn; there is no defined behavior for a message submitted during a run.

Claude's terminal documents queued input and timing for when it is incorporated. Guildhall should expose separate send-next, steer and stop controls only when the backend supports those semantics. [Interactive input](https://code.claude.com/docs/en/interactive-mode).

Plan coverage: Stop in P1; task queue/lifetime in P6; delegation in P7. Define these as separate behaviors rather than treating every text input as cancellation or a new parallel job.

### G14 — Durable rules, skills and memory

Missing: project/user instruction scopes, skill activation, persistent preferences, source-of-instruction inspection and memory lifecycle. SQL history alone is neither cross-chat memory nor a rules engine. Skills/rules used by Codex during development are not automatically installed into Guildhall.

Plan coverage: agent instructions and later tools/memory, but discovery/loading/precedence UX is underspecified. Propose a scoped project-guidance increment around P3/P4: inspect which instructions apply and why, allow edits and version them, keep imported content lower-trust than user authority. Semantic memory remains P8 unless evidence warrants moving it earlier. Claude's documented context lifecycle motivates preserving durable rules independently of summaries.

### G15 — Provider capability and external-agent boundaries

Missing: real provider abstraction, capability metadata, credential settings, local/cloud routing and external runtime integrations. Model/provider and agent-runtime selectors must remain distinct. Installing Codex/Claude Code is not the same as obtaining a general model API, nor proof that its account can be reused by another app.

Plan coverage: P2 adapters/usage, cloud-agent integrations as explicit boundaries. Prioritize one normalized provider contract and capability-driven UI. Do not promise arbitrary vendor account reuse or make a third-party SDK the owner of Guildhall data. Keep external runtime histories distinguishable from Guildhall-owned conversations.

### G16 — Usage, observability and audit

Missing: first-output latency, output rate, actual token usage, error taxonomy, per-run model/settings display, resource measurement and action audit trail. Today logs are minimal and UI errors generic; the schema has status and provenance fields but little user access.

Plan coverage: P2 usage, P3 trace, P7 graph. Distinguish context occupancy, tokens billed, cached tokens, provider quota and measured local resource use. Unknown values should remain unknown. Local inference should not display a fabricated API bill. Cost tracking and context efficiency are separate concerns. [Claude usage and costs](https://code.claude.com/docs/en/costs).

### G17 — Background, remote and multi-agent work

Missing: separately running host, durable queue, reconnectable remote sessions, task ownership, child-run budgets and cancellation trees. Independent histories, simultaneous HTTP tasks, multiple app windows and coordinated agents are four different features.

Plan coverage: P6 independent lifetime/remote work, P7 coordination. Keep P1's single-generation policy while browsing multiple conversations. Later require explicit scheduling/resource bounds and isolate write-capable work, using worktrees where appropriate. Codex worktrees demonstrate isolation, not unlimited model capacity or a scheduler.

## Context Inspector: proposed design priority

The owner's preference for Claude's context view is explicit. Treat context as a primary session control beside the model selector, opening an inspector panel. This is a Guildhall proposal inspired by the sources, not a claim of exact feature parity with Claude's UI.

### First increment: visibility and enforced limits

- Display configured active window and reserved output budget. Never show a model catalog maximum as if it were the current request allowance.
- Show next-request input budget by category: instructions, eligible conversation, attachments, tool schemas/results and summary. Initially only the categories actually used appear.
- Mark counts as estimated before sending unless the adapter can provide authoritative token counts. A character-based estimate is not an exact tokenizer. Provider-reported usage after a run is a separate measurement.
- List included turns and excluded incomplete turns with a reason. Selecting a past run shows its immutable context/model/settings provenance, not today's settings.
- Enforce a conservative overflow threshold before writing a new run/contacting the provider. Keep the full saved transcript. Offer a new conversation or copy/export path with an explanation.
- Model switching recomputes the next request's budget. If the smaller model cannot fit the conversation, explain before Send.

Acceptance: intentionally exceed a small configured fixture budget; the app explains the limit, creates no run and makes no provider call. A conversation with cancelled/failed turns shows those in the transcript but excludes them from the request. Displayed accounting matches the constructed request and labels uncertainty.

### Later increment: controlled context editing

Add source chips, pin/exclude controls and source previews as attachment/workspace tools arrive. Pinning preserves user intent, not a guarantee of model attention. Track source revision/hash and report changed/missing files. A tool's content is data, not authority to grant permissions. Keep prompt content out of default logs; a user-requested inspector is a separate, intentional display of their own material.

### Later increment: explicit compaction

Let the user review a summary and preserve goals, decisions, constraints and unresolved questions. Create a new context revision while keeping original messages. Show when automatic compaction happened, what was summarized and what durable instructions reload. Summary generation is itself a model call with latency/cost and possible information loss. Make changes to policy reviewable, test long follow-ups, and avoid retrying compaction forever.

Do not move automatic summarization silently into P1; it conflicts with the reviewed contract. The first inspector can ship without embeddings, a vector database, RAG infrastructure or another model service.

## Proposed order of work

1. Reliability/QC: native harness; Stop; interrupted-run recovery; real output checkpoints; host gate and duplicate-send resolution. Finish existing P1 commitments first.
2. P1 UX amendment for review: Context Inspector and budget enforcement; archive/restore/delete semantics; drafts and navigation; responsive history access; clear model/resource policy. Context overflow prevention is already promised; the inspector makes it understandable.
3. Complete P1 settings/export and baseline message ergonomics. Functional milestone: a bounded multi-turn conversation can stream, stop, recover, reopen, inspect its context and export under agent-driven native QC.
4. P2 provider/capability/usage surfaces. P3 read-only context acquisition and MCP lifecycle/permissions. P4 scoped edits, project rules, worktrees, diffs, review and approved tests. This is the meaningful coding-agent parity milestone.
5. P6/P7 durable background work, controlled parallel sessions and delegation. Add advanced dashboards when they summarize actual activity, not before.

Prototype future management screens using the supplied references, with explicit sample-data labels. Do not declare backend parity from a screenshot or expand all later phases into P1. The existing master plan can retain its phases with these workflows added as explicit acceptance criteria.

## How Guildhall can differ usefully

Proposed differentiators: inspectable local SQL records and portable exports; provider-independent agent definitions; explicit CPU/GPU/resource profiles; context provenance with honest estimates; visible tool scope and approvals; one coherent UI spanning personal assistance, private tools and coding. These are goals to validate, not advantages already achieved. Core local operation should remain available without a cloud subscription.

Avoid mandatory enterprise administration, a hosted marketplace, a full editor replacement or graphs as prerequisites for usable chat. Match the important user loops first. A modest model can support tests, but app correctness and model answer quality need separate evaluation.

## Owner-requested capability: scheduled automations

The owner wants scheduled jobs in Guildhall that run locally on this computer and can use the same capabilities an agent is allowed to use. The master plan already places a durable background task host in Phase 6 and names scheduling among advanced features in Phase 8. The local scheduler and UI should be specified alongside Phase 6's durable queue; remote scheduling and multi-agent schedules remain later work.

Acceptance criteria to carry into that phase:

- Create, name, edit, pause, resume, run now, and permanently remove a schedule; show its time zone, next run, last run, and durable run history.
- Store schedules and run outcomes in Guildhall's local SQL Server database. A scheduled run uses the same registered agent, model/provider policy, workspace, and tool permissions as an interactive run, with a visible snapshot of the configuration used.
- Execute only on this computer. Define behavior when the desktop is closed, Windows is asleep/off, the host crashes, or a scheduled time is missed. Do not promise execution while the machine or required local host is unavailable.
- Bound retries, overlapping runs, model/resource usage, duration, output, and concurrency. Surface missed, skipped, failed, cancelled, and completed states without silently duplicating side effects.
- Evaluate permissions at run time, show the action scope on the schedule, and prevent a schedule from silently gaining new tool authority after permissions change. External network actions remain visible and subject to the same explicit grants as interactive agents.
- Keep local operation independent of cloud accounts and do not install a Windows service or Task Scheduler job by default. Any optional OS-level background startup needs a separate reviewed setup step.

This capability is a roadmap requirement only; it does not authorize Phase 6 implementation during Phase 1.

## Review outcome

Most deep coding capabilities already have a home in P3–P7. The roadmap's biggest missing specificity is context accounting/control, conversation lifecycle, durable guidance, steering semantics, resource policy and review/recovery behavior. These deserve tracked work and functional acceptance criteria. This review changed documentation only; no competitor integrations, application features or master-plan edits were performed.
