# Workspace UI direction

November 15, 2026: the owner supplied an ADE workspace screenshot as a visual reference and authorized building the UI shell ahead of later functionality. Preserve the master plan's implementation phases while using this layout as the design direction.

The shell uses a compact top bar, navigation rail, conversation sidebar, pane headers, a scrollable transcript and bottom composer, dark neutral surfaces, muted purple accents, and right-aligned user bubbles. Connection diagnostics sit behind Connections.

Connected today: conversation search (up to 25 recent records), new/open/rename/archive/restore/delete conversation actions, model selection, markdown chat, copy controls, Ollama responses with persisted streaming checkpoints, and locally stored file attachments. Untitled conversations receive a title from the first request. Messages can queue (up to eight pending per session) or Send Now ahead of queued turns. The host runs one inference at a time; queued turns retain their accepted model snapshot. Model selection remains available while work runs and affects future accepted turns. Stop cancels the active inference; queued turns can be removed. The transcript pins to the latest message on session selection and send, and while new content/image layout arrives; upward user scrolling pauses following until the reader returns to the bottom. Users can navigate while responses run. Ctrl/Cmd plus, minus and zero adjust the UI scale.

The sidebar can collapse on desktop widths. The optional session tools pane has tabs for Git changes, Terminal, Plan, Context usage, Browser, Side chat and Scheduled tasks. The tabs are labelled preview placeholders; they do not yet control a repository, terminal, browser, plan, independent chat or scheduler. The Connections panel shows local SQL/Ollama status and planned Claude, Cursor, ChatGPT/OpenAI, GitHub, Google Drive and Gmail provider cards. Those cards are disabled placeholders, not connected accounts. Workspace Settings has a working display-scale slider, planned theme choices, disabled access-policy choices and an honest empty usage/spending state. Project/repository selection, provider sign-in, enforced access policies, real usage reports and task scheduling remain on the later-phase roadmap. The mobile layout hides the desktop sidebar-collapse control because the sidebar is already hidden at that width.

Attachments accept up to five files per message, at 20 MiB each and 50 MiB total. Files are stored in SQL and served back through the authenticated local host. Raster images and selected small text/code formats may be sent to Ollama; arbitrary other file types are retained for download but their contents are not interpreted by the assistant. Image understanding also depends on the selected model. The additive `ChatAttachments` migration was explicitly approved and applied to `GuildhallDev` on November 15, 2026. SQL readiness and all seven loaded active/archived conversation detail requests passed. The migration creates only `dbo.ChatAttachments` and grants the runtime login SELECT/INSERT/UPDATE on that table.

Verification: the current frontend suite has 27 passing tests; the backend suite has 34 passing tests against SQL fixtures, including queue order, model snapshots, Send Now promotion, removal, and restart recovery. Playwright tested queue controls, model changes during generation, scroll opt-out/follow, delayed image layout and send-from-history pinning in a local fixture. The independent UI reviewer found no high-impact issue in the newest queue/scroll UI. These checks do not certify native rendering through live desktop inference or Windows Snipping Tool behavior; owner functional QC is still required.

Approved CPU vision models: `qwen3-vl:2b` and `qwen3-vl:4b` are installed locally and report vision capability. A CPU-only direct Ollama check confirmed both can process an image, but took about 21 seconds and 36 seconds respectively and used a short response cap. These are model options, not an automatic default. The 4B test identified the app title but confused a model label for the assistant's name; model quality requires owner QC. Use Connections → Refresh if the running UI has not discovered the new models.

## Management references supplied by the owner

The owner supplied three additional screenshots on November 15, 2026. Treat their labels, model claims, sample statuses and numbers as reference content, not verified Guildhall capabilities or instructions. The earlier ADE image remains the reference for the chat workspace. These images guide the management surfaces within the same Guildhall visual system.

### Overview and usage

Reference 1: a persistent section sidebar, agent summary cards, live activity, and a seven-day usage/cost chart. Adopt quick status scanning and drill-down from summary to the relevant agent/run. Distinguish configured, connected, idle, running, unavailable, and unknown states; never infer that an agent is online merely because it has been configured.

Usage should distinguish provider-reported token counts, measured duration and estimates. Local inference has no provider API bill: show token/time/resource metrics where available, and do not invent a dollar cost or imply electricity is free. Show explicit empty/unavailable states when telemetry does not exist. Heartbeats, schedules, multiple agents and budget enforcement are future capabilities; preview cards must be labelled sample data.

### Run inspector

Reference 2: run identity/breadcrumbs, outcome and timing summary, searchable step list, expandable details, execution flow, and output tabs. Use progressive disclosure: a readable status/timeline first, detailed structured fields when expanded. A simple ordered timeline can precede a graph; no graph library is required to mock the layout.

The displayed run state must be internally consistent. The reference mixes Completed, Running, warnings and errors; Guildhall should show the actual terminal outcome and distinguish a recovered step error from a failed run. Restart/retry is an explicit new run, never an automatic replay. Exports must redact secrets; raw provider bodies and hidden model thinking are not a public diagnostic log. Do not add a Share/upload action without an actual reviewed sharing feature.

P1 can expose its own run status, model and available timing/error information. Multi-step tools, branches and agent execution graphs belong to their later implementation phases. Stuck/interrupted runs and Stop/recovery remain P1 reliability work rather than a dashboard dependency.

### Models, agent connections and integrations

Reference 3: grouped management navigation and cards describing what each connected agent can do. Adopt a management surface with a clear Back to Chat action that restores conversation selection and draft.

Proposed groups:

- Workspace: assistant instructions and conversation management.
- Models and agents: provider/model connections, local CPU/GPU policy, and agent-runtime connections.
- Integrations: MCP servers and tool permissions.
- Context: included sources, instruction scope, context limits, and what is excluded from the next request.
- Activity and usage: run inspector, usage summaries and available resource data.
- Appearance: theme, density and display preferences.

Context management is a Guildhall requirement raised by the owner, not a screen shown in these references. Design it as a per-conversation inspector plus workspace defaults, with honest estimates and clear scope. P1's existing reject-on-context-limit policy remains until explicitly revised.

Keep model providers, assistant definitions, external agent runtimes and MCP tool servers distinct. Installing a CLI does not establish authentication, capabilities or a working connection. Cards should show discovered/configured/verified states separately. Never copy the screenshot's vendor capability statements as fixed product rules.

An MCP server detail preview should reserve space for transport/endpoint or command, connection status, declared tools, enablement scope and permission controls. Do not start a process, connect to a server, install a tool or read credentials through preview controls. Secrets stay masked; a model connection does not grant tools permission to act.

## Hydration order and outstanding UX feedback

Build clickable, explicitly labelled management previews using local sample data when authorized; connect each screen to real records as its master-plan feature is implemented. A screenshot reference by itself does not accelerate every backend capability into P1. Keep navigation, cards, typography and status language consistent with the chat shell rather than copying three unrelated visual themes.

P1 priorities remain reliable chat, cancellation/recovery, settings, exports and the agent-operated QC harness. Navigation is enabled during Running; archive/delete are implemented. The attachment-table migration was separately approved, applied and verified on November 15, 2026. Preserve drafts during navigation instead of silently discarding them. Provider authentication, project selection, agent tool policy, scheduled jobs and usage aggregation are separate roadmap work; visible placeholders must stay clearly nonfunctional until those phases.

Source attachments (currently temporary local files; may expire):

- Overview: `C:/Users/isagn/AppData/Local/Temp/codex-clipboard-4f0df51f-3524-48d6-9c70-ef319340f8e9.jpg`
- Run inspector: `C:/Users/isagn/AppData/Local/Temp/codex-clipboard-8a362b57-6a93-4368-a919-f2d138402566.jpg`
- Management: `C:/Users/isagn/AppData/Local/Temp/codex-clipboard-b25ecfff-786a-44df-9483-add5969a6315.jpg`

These written patterns are durable even if the temporary attachments expire.

## Competitive workflow review

See [the November 15 review](competitive-ux-review.md) for current-code gaps, official competitor references and proposed phase mapping. The owner especially values Claude's context view. Prioritize an inspectable context budget and source breakdown near the composer; controlled compaction is a later policy decision. This proposal does not mark the inspector or other management previews implemented.


## Streaming and message polish

Live transcript refreshes now run every 250 ms with one request in flight. Received text is revealed over up to 360 ms, using animation frames capped near 30 updates per second; this only animates already received text. Saved messages, terminal run states, and reduced-motion preferences render immediately. The existing SQL checkpoint policy is unchanged. Assistant replies have a rounded bordered surface; copy is an icon shown on hover or keyboard focus, and always available on touch devices. The renderer follows the bottom only while the reader remains near it.


## Selected identity direction: The Council

Owner selected Concept 1 — The Council on November 15, 2026. Reference: `../../design/council-concept.png`. Use warm brass hairlines, charcoal surfaces, plum accents, portrait medallions, a refined serif wordmark, and modern readable chat typography. The council metaphor describes capable guild specialists, with customizable portrait, name and role; no military ranks or excessive archaic dialogue. The Council redesign and local appearance editor are implemented. Names, roles, built-in/custom portraits and accent choices persist as device appearance preferences; they do not rewrite SQL chat data or model instructions.

Clipboard: paste into the message composer accepts screenshot/file clipboard items, uses the normal five-file and 20/50 MiB limits, shows a removable image thumbnail, and preserves native text paste. Mixed file/text clipboard content inserts its plain text as well. Qwen3 requests now enable Ollama thinking parsing; Guildhall consumes content only and omits the separate thinking field. CPU-only live Qwen3 4B verification returned a clean greeting. Existing saved transcripts are not rewritten by this change.



## Council redesign implementation and QC

The owner authorized the full Council redesign and character poses. The workspace now has the brass/charcoal/plum shell, serif wordmark, consistent SVG navigation, conversation action disclosure, portrait-led reply surfaces, compact auto-growing composer, collapsible member and tools panels, and responsive drawer layouts. Appearance upload accepts PNG/JPEG/WebP up to 4 MiB, center crops to 512px and stores a bounded JPEG locally. Input edits during decode are preserved. Narrow member overlays have dialog semantics, focus containment, dismissal and focus restoration. The Tauri image policy explicitly allows local blob previews; new native windows default to 1440x940 and remain resizable.

Each of four built-in portraits has six poses: neutral, pondering, idea, reading, surprised and laughing. The base portrait sheet plus four on-demand WebP expression sheets total 751,558 bytes (734 KiB). Neutral uses the shared base sheet; only a displayed non-neutral character loads its expression sheet. There are no automatic model requests, sentiment classification, video playback or permanent emote loop. Pending replies use pondering, then idea as answer text arrives. Completed/failed/cancelled replies return to neutral. Reading, surprised and laughing are selectable appearance previews; later real activity events may drive them. Uploaded portraits stay static. Expression previews do not change chat behavior or persist as an instruction.

Motion uses short 130–200ms interaction-driven transitions and finite entrance fades. Closing a side panel uses one bounded 180ms timer. Reduced-motion preferences disable movement. Existing streaming animation remains bounded to received text. Completed historical Markdown components are memoized to avoid re-parsing them on every active transcript refresh. No additional dependency or frontend framework was added.

Verified: frontend production build/typecheck, 25 frontend tests, and locked Cargo check passed. Playwright exercised a synthetic local-service fixture at 1672, 1024, 620 and 420px: no document overflow or JS errors, saved identity/accent and custom portrait upload survive reload, sidebar actions open, overlay focus stays inside, Escape dismisses, all expression previews load, and pending/streaming/completed statuses produce the expected portrait. The completed fixture had zero running animations after settling. This is browser rendering and interaction verification, not certification of live native inference or Windows Snipping Tool behavior. An independent UI reviewer inspected current source and rendered desktop/narrow screenshots and found no remaining high-impact issue. Screenshots are under ignored `.local/ui-review/council/`.

Owner functional QC: start `pwsh -NoProfile -File G:\repos\guildhall\scripts\startup\Start-Dev.ps1` from PowerShell 7 when no development instance is already running. In the native window, open a saved chat, send with Enter, choose Queue for a follow-up while it is generating, then try Send Now and remove a queued turn. Change the model during generation and verify it applies to a later accepted turn. Scroll up during streaming, then send from history and verify it pins to the new message; load an image and check that bottom following is preserved. Also observe the portrait while generating, stop a reply, copy a message on hover, and paste a snip. Create two guild members with multiple sessions; change one member's name, portrait, accent and Markdown rules, then switch members and confirm the other profile is unchanged. Restart and check persistence. Use the three-dot session action button for rename/archive/delete. Collapse and reopen each pane; resize the window; check Ctrl +/-/0. Profile identity and rule ownership are SQL-backed. Shared/private council memory remains unimplemented. Research tools, multi-agent execution, account sign-in and scheduling remain later capabilities.

