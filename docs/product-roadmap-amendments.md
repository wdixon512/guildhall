# Owner-requested roadmap additions

These additions preserve the existing master-plan phases. They clarify the intended destination and do not authorize implementing later-phase capabilities early.

## Phase 1 quality and conversation work

November 16: the owner requested more capable task-focused member behavior with understated Guildhall personalities, grounded research/proposals, usable local-model speed and a member-rules editor that does not dim the council on every keystroke. See [the bounded quality/performance follow-up](agent-quality-and-performance.md). Prompt behavior and current editor/performance fixes belong to Phase 1; explicit SQL memory follows B05/B06, research tools and delegation keep their existing later-phase boundaries.

Owner-approved Phase 1 additions now include visible saved streaming and Stop behavior; a bounded per-session queue with Send Now promotion/removal; model selection during generation for future accepted turns; local chat attachments; safe Markdown/GFM formatting; inline image previews; copy actions; automatic first-message titles and manual rename; useful conversation filtering; saved-chat recovery; keyboard UI scaling; pinned-to-bottom transcript behavior with upward-scroll opt-out; and collapsible workspace panels. Layout Preview is a navigable placeholder surface for Git changes, Terminal, Plan, Context Usage, Browser/QC, Side Chat, and Scheduled Tasks. Placeholder controls must say when a feature is unavailable and must not imply that commands, external services, or automations are connected. Queue state is persisted in SQL, but execution is tied to the running host; restart marks unfinished work Interrupted instead of replaying it.

## Council profiles and memory

The owner approved bot profiles with multiple sessions, and a reviewed shared “About you” memory plus private bot/project memory. Each bot owns its role, appearance, Markdown rules and sessions. Shared notes require review before use; bot and project notes remain scoped. Memory is inspectable, editable and removable, and model-provider changes do not erase it. Profile-scoped identity and Markdown rule code is prepared; it becomes active after the separately approved SQL upgrade. Persistent council memory is not implemented. See [the council profile and memory design](council-profiles-and-memory.md). Applying a future SQL migration or permission change requires separate, concrete approval.

Chat attachment defaults: any file type may be stored and downloaded; 20 MiB per file, five files and 50 MiB total per message. The current local provider adapter sends bounded UTF-8 text and recognized raster images; other formats remain attached but are not interpreted by the model. PDF/Office extraction and OCR need a later, separately reviewed capability decision.

## Planned capability placement

| Existing plan area | Owner-requested outcome | Boundary to preserve |
| --- | --- | --- |
| P3 local agent/provider management | A connection catalog for local model runtimes and installed agents; show Claude, Cursor, ChatGPT/OpenAI, GitHub, Google Drive, Gmail and others as connected, unavailable, or not configured. | Show catalog entries before sign-in; provider use requires that provider's supported local runtime or account auth. Never imply one connected account grants access to another service. Keep credentials in OS-protected storage. |
| P4 project workspaces and controlled tools | Choose a local folder or Git repository as the active project. Provide local repository discovery, branch/status, project-scoped tools and clear boundaries. | Local folder selection must work without GitHub. Use Tauri's native folder picker and explicit project roots. GitHub browsing/cloning is optional, separately authenticated and never a prerequisite for local repositories. A chosen folder is not itself a sandbox. |
| P4 permission policy | Choose `Always ask`, `Ask for potentially unsafe actions`, or clearly marked `Unrestricted / autopilot`. | Each mode must be enforced in the host/tool boundary, with action previews, audit history, cancellation, and revocation. “Unsafe” must be defined by capability and scope; a UI selector alone is not security. Unrestricted mode needs a conspicuous danger label and explicit enablement. |
| P4 workspace shell | Collapse/expand the conversation sidebar and session panels to give the active chat more room. Fill the current preview tabs with their planned Git, terminal, plan, context, browser-QC and side-chat workflows as each capability arrives. | Preserve keyboard access, focus, saved pane state and usable narrow-window navigation. A disabled placeholder is not an implemented tool. |
| P5 integrations and P6 scheduled execution | Manage remote MCP/service connections where the plan places them; later add local scheduled automations that can run agent capabilities, with run history and pause controls. | Distinguish a connected account, a configured provider, an authorized tool, and a scheduled task. The local scheduler runs on this computer; define behavior while Guildhall is closed and require the normal execution policy for each run. |
| Existing settings and observability work | A settings area for theme/font preferences and resource policy; usage summaries across providers; cost only where the provider reports or supports a defensible estimate. | Local usage can show tokens/time/resources when measured but must not invent a bill. A connected service with no usage API should say unavailable rather than fabricate totals. |

## Connection versus agent selection

The UI may show local and hosted agent choices before they are connected. Selecting an option is not the same as authenticating it. A local Ollama model needs no hosted account. A hosted agent or service requires its supported sign-in/credential flow before it can run; Guildhall should not require a GitHub account to select a local project or a cloud account to use local chat.

## Review and acceptance gates

- Every provider card has explicit `Not connected`, `Connected`, `Unavailable`, or `Needs sign-in` status and a tested connect/disconnect/reconnect path before its phase is complete.
- Project selection works with a local non-Git folder and a local Git repository while offline. GitHub is an optional route, not the local-project picker.
- Each access mode is tested with the same action matrix so its label matches enforced behavior. Full autopilot is visibly dangerous and scoped to the active project and selected connections.
- Usage totals identify provider, measurement source, currency/time range when applicable, and missing data.
- Pane collapse, app zoom (`Ctrl`/`Cmd` `+`, `-`, `0`), focus restoration, and narrow-window access pass functional QC.
