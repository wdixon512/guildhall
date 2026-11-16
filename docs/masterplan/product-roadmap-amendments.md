# Owner-requested roadmap additions

These additions preserve the existing master-plan phases in [personal_ai_ecosystem_master_plan.md](personal_ai_ecosystem_master_plan.md). They clarify the intended destination and do not authorize implementing later-phase capabilities early.

## Phase 1 quality and conversation work

November 16: the owner requested more capable task-focused member behavior with understated Guildhall personalities, grounded research/proposals, usable local-model speed and a member-rules editor that does not dim the council on every keystroke. See [the bounded quality/performance follow-up](phase%201/agent-quality-and-performance.md). Prompt behavior and current editor/performance fixes belong to Phase 1; explicit SQL memory follows B05/B06, research tools and delegation keep their existing later-phase boundaries.

Owner-approved Phase 1 additions now include visible saved streaming and Stop behavior; a bounded per-session queue with Send Now promotion/removal; model selection during generation for future accepted turns; local chat attachments; safe Markdown/GFM formatting; inline image previews; copy actions; automatic first-message titles and manual rename; useful conversation filtering; saved-chat recovery; keyboard UI scaling; pinned-to-bottom transcript behavior with upward-scroll opt-out; and collapsible workspace panels. Layout Preview is a navigable placeholder surface for Git changes, Terminal, Plan, Context Usage, Browser/QC, Side Chat, and Scheduled Tasks. Placeholder controls must say when a feature is unavailable and must not imply that commands, external services, or automations are connected. Queue state is persisted in SQL, but execution is tied to the running host; restart marks unfinished work Interrupted instead of replaying it.

## Council profiles, backing engines, and memory

The owner approved bot profiles with multiple sessions, and a reviewed shared “About you” memory plus private bot/project memory. Each bot owns its role, appearance, Markdown rules, default backing engine, and sessions. Shared notes require review before use; bot and project notes remain scoped. Memory is inspectable, editable and removable, and model-provider or CLI-harness changes do not erase it. Profile-scoped identity and Markdown rule code is active. Persistent council memory (B05/B06) is planned next within Phase 1. See [the council profile and memory design](phase%201/council-profiles-and-memory.md). Applying a future SQL migration or permission change requires separate, concrete approval.

Chat attachment defaults: any file type may be stored and downloaded; 20 MiB per file, five files and 50 MiB total per message. The current local provider adapter sends bounded UTF-8 text and recognized raster images; other formats remain attached but are not interpreted by the model. PDF/Office extraction and OCR need a later, separately reviewed capability decision.

## Unified "One Guildhall" service-wrapping decisions (November 16)

1. **Primary Command Center (Not an IDE Plugin):** Guildhall is the primary desktop workspace where the user chats with Council Members, inspects context budgets, reviews plans and diffs, and approves actions. External code editors (VS Code, Cursor, Visual Studio, JetBrains) are opened only when manually editing code; Guildhall never depends on proprietary VS Code/JetBrains UI extensions.
2. **Both Cloud Model APIs and Local CLI Agent Harnesses (Pillar 1 + Pillar 2):**
   - **Pillar 1 — Direct Cloud Model APIs:** Support stateless REST/SSE adapters for **Google Gemini API**, **Anthropic Claude API**, and **OpenAI API** using API keys stored in Windows DPAPI / Credential Manager, where `Guildhall.AgentHost` runs the entire prompt and MCP tool loop.
   - **Pillar 2 — Wrapped Local CLI Agent Harnesses:** Support spawning installed CLI coding agents (**Gemini CLI** via `gemini --acp` / `--output-format stream-json`, **Claude Code CLI** via `claude -p --output-format stream-json` / ACP, **OpenAI Codex CLI** via `codex exec --json` / `app-server`, and **Cursor Agent CLI** via `agent -p --output-format stream-json`) so the user can reuse existing Pro/Max/Google subscription logins and vendor agent harnesses inside Guildhall.
   - **SQL Source-of-Truth & Host Permission Invariant:** Even when a Council Member is backed by a wrapped CLI Harness, Guildhall's SQL Server database remains the canonical owner of transcripts, memory notes, and diffs. `Guildhall.AgentHost` injects SQL-owned context/memory into the harness and enforces Guildhall's permission modes (`Always ask`, `Ask for potentially unsafe actions`, `Unrestricted / autopilot`) rather than permitting unmediated `--yolo` execution.
3. **Backing Engine per Council Member:** Any Council Member (or individual session) can select a Local Model (Ollama), a Cloud Model API, or an External CLI Agent Harness as its backing brain.
4. **Cross-Member Conversation Reassignment ("Assign to guild member…"):** Users will be able to right-click a session and reassign or pass its context from one Council Member to another through an explicit handoff/edit mode (Phase 7). In Phase 1, this action is surfaced as an explicitly disabled/greyed-out menu item.
5. **Universal Planned-Feature Placeholder Rule:** Whenever a future-phase capability is requested for early UI visibility, record it in `docs/masterplan/` and render it in the UI as an explicitly disabled/greyed-out preview control that clearly states it is planned and never implies live connectivity.

## Planned capability placement

| Existing plan area | Owner-requested outcome | Boundary to preserve |
| --- | --- | --- |
| P2 & P3 backing engines and connection catalog | A 3-pillar connection catalog covering (1) Local runtimes (Ollama), (2) Cloud Model APIs & Local CLI Harnesses (**Gemini API / Gemini CLI**, **Claude API / Claude Code**, **OpenAI API / Codex CLI**, **Cursor CLI**), and (3) MCP/Service integrations (**GitHub**, **Google Drive**, **Gmail**, **SQL Server MCP**). | Show catalog entries before sign-in; provider/harness use requires that service's supported local runtime, CLI OAuth login, or API key. Never imply one connected account grants access to another service. Keep credentials in OS-protected storage and scrub child-process environments. |
| P4 project workspaces and controlled tools | Choose a local folder or Git repository as the active project. Provide local repository discovery, branch/status, isolated Git worktrees, project-scoped tools, native diff review, and clear boundaries. | Local folder selection must work without GitHub. Use Tauri's native folder picker and explicit project roots. GitHub browsing/cloning is optional, separately authenticated and never a prerequisite for local repositories. A chosen folder or Git worktree is not itself a security sandbox; `Guildhall.AgentHost` enforces path and command boundaries. |
| P4 permission policy | Choose `Always ask`, `Ask for potentially unsafe actions`, or clearly marked `Unrestricted / autopilot`. | Each mode must be enforced in the host/tool/harness boundary, with action previews, audit history, cancellation, and revocation. “Unsafe” must be defined by capability and scope; a UI selector alone is not security. Unrestricted mode needs a conspicuous danger label and explicit enablement. |
| P4 workspace shell | Collapse/expand the conversation sidebar, member sessions, and session panels to give the active chat more room. Fill the current preview tabs with their planned Git, terminal, plan, context, browser-QC and side-chat workflows as each capability arrives. | Preserve keyboard access, focus, saved pane state and usable narrow-window navigation. A disabled placeholder is not an implemented tool. |
| P5 integrations and P6 scheduled execution | Manage remote MCP/service connections where the plan places them; later add local scheduled automations that can run agent capabilities, with run history and pause controls. | Distinguish a connected account, a configured provider/harness, an authorized tool, and a scheduled task. The local scheduler runs on this computer; define behavior while Guildhall is closed and require the normal execution policy for each run. |
| P7 cross-member handoff & delegation | Right-click a conversation to **Assign to guild member…**, entering an inspectable handoff/edit mode to pass curated context between specialists across any backing engine. | Render as a disabled preview control in Phase 1; implement full context handoff and child-run delegation in Phase 7 with immutable SQL provenance. |
| Existing settings and observability work | A settings area for theme/font preferences and resource policy; usage summaries across providers/harnesses; cost only where the provider reports or supports a defensible estimate. | Local usage and subscription-backed CLI harnesses can show tokens/time/resources when measured but must not invent a per-token API bill. A connected service with no usage API should say unavailable rather than fabricate totals. |

## Connection versus agent selection

The UI may show local, cloud API, and CLI harness choices before they are connected. Selecting an option is not the same as authenticating it. A local Ollama model needs no hosted account. A cloud API or CLI harness requires its supported API key or CLI sign-in flow before it can run; Guildhall should not require a GitHub account to select a local project or a cloud account to use local chat.

## Review and acceptance gates

- Every provider/harness/service card has explicit `Not connected`, `Connected`, `Unavailable`, `Needs sign-in`, or `Planned` status and a tested connect/disconnect/reconnect path before its phase is complete.
- Project selection works with a local non-Git folder and a local Git repository while offline. GitHub is an optional route, not the local-project picker.
- Each access mode is tested with the same action matrix across both native MCP tools and wrapped CLI harnesses so its label matches enforced behavior. Full autopilot is visibly dangerous and scoped to the active project and selected connections.
- Usage totals identify provider/harness, measurement source, currency/time range when applicable, and missing data.
- Pane collapse, app zoom (`Ctrl`/`Cmd` `+`, `-`, `0`), focus restoration, and narrow-window access pass functional QC.

## November 16 Phase 1 frontend & DevOps polish

The owner requested unread reply indicators, clearer member/session grouping, clickable role/instruction-based starter examples, persisted response elapsed labels, compact queued-message controls with promotion/edit/removal, active waiting-word feedback, clock/shield navigation icons, removal of redundant Open chat, and responsive composer/root overflow corrections. New member permanent deletion was requested alongside retained-session archival; the owner explicitly chose permanent deletion of the member and ALL linked sessions after confirmation. Archive remains the option that preserves sessions. See [the bounded implementation and QC plan](phase%201/frontend-polish-plan.md).

Additional November 16 owner follow-ups ([docs/2026-11-16.md](../2026-11-16.md)) tracked for bounded execution:
- Do not dim/grey out the conversation list while typing a draft or editing text.
- Balance left/right padding in the chat transcript so user and assistant messages feel properly centered.
- Allow accordion-collapsing all conversations under each Council Member in `Your council`, and remove the `N shown` count next to `Chats` / `Archived`.
- Add the disabled `Assign to guild member…` option in the conversation context/actions menu.
- Make Markdown links clickable in chat, opening safely in the OS-configured default browser.
- Add one-click copy buttons to formatted terminal/code blocks.
- Organize `docs/` under `docs/masterplan/` (`phase 0/`, `phase 1/`) and organize `scripts/` into `database/`, `startup/`, `sql/`, and `misc/`, with a spend-conscious CI test pipeline.
