# Agent quality and local performance

Owner request, November 16, 2026. This supplements the existing master plan and Phase 1; it does not move tools, delegation or shared memory ahead of their accepted boundaries.

## What is stored today

SQL Server stores member identity and Markdown-formatted instructions in AgentDefinitions; sessions in Conversations; immutable instruction/model/settings snapshots in ChatRuns; conversation text in ChatMessages; attachments in ChatAttachments. Completed turns from the selected session are included in later requests. The editor's Markdown text is a SQL field, not a memory file on disk. Application data stays local.

There is no implemented shared About you collection, private cross-session member memory or project memory. B05/B06 in the council design already cover explicit SQL notes, retrieval scopes and an inspector. Project scope waits for real project ownership. Saving a chat or changing instructions does not train model weights.

## Implemented backend behavior

The shared behavior policy is editable in Workspace settings and stored in the dedicated SQL `GuildhallBehaviorSettings` singleton. The additive `GuildhallBehaviorSettings` migration seeds the prior policy text and grants the runtime identity only SELECT/UPDATE on that table. Saving uses optimistic revision checks. New run admission snapshots the text and revision; subsequent edits affect only newly accepted turns. Existing runs and each member's own instructions remain unchanged. Applying this migration to GuildhallDev requires separate approval.

New accepted runs capture the version and full text of a short common Guildhall behavior policy in the existing SettingsSnapshotJson field. The member's own instructions remain separately captured in InstructionsSnapshot. Execution joins those snapshots into the system message. Already accepted runs without the policy retain their original behavior; changing a policy does not rewrite history.

The policy asks members to take ownership, finish useful work within actual capabilities, check results, distinguish facts from assumptions, and support research with evidence actually available. The voice is a capable colleague with occasional understated guild language. It prohibits invented citations, tool calls, test claims or access to other members' conversations. It grants no new runtime capability. A Manager role cannot command other agents until bounded delegation is implemented in Phase 7.

Prompts can improve habits and tone; capability still depends on model quality, relevant context and available tools. No instruction can supply live research when research tools are unavailable. Grounded research should use supplied evidence today; actual tool access follows the existing MCP/controlled-tools phases.

## System instructions, context management, and caching architecture

- **Static System Preambles:** Base instructions (combining the workspace-wide global guidelines and the selected Council Member's preamble) are configured as a single static `system` message (or Modelfile parameter) in backend API calls per turn, rather than repeating instructions across conversational messages. This preserves prefix stability for KV cache reuse.
- **Context Preservation:** Conversational continuity is maintained by passing the historical message array of completed turns back to the model API per turn, supplemented by KV caching on warm models and planned backend state compression / explicit compaction on larger models.
- **Modular Instruction Separation:**
  - **Tunable Parameters (User/Config-facing):** Council Member persona/role definitions, primary task objectives, and operational mode toggles (such as reasoning effort / thinking controls and model selection).
  - **Global Guidelines (System-wide):** Concise operational boundaries (`GuildhallBehaviorSettings`) instructing agents to rely strictly on tool outputs, respect workspace scope, and never fabricate system states, citations, or benchmark metrics.
- **Canonical Storage vs. Runtime Memory:**
  - **Markdown Files on Disk:** Version-controlled Markdown files serve as the disk source of truth for base agent preambles and configurations, paired with immutable per-run provenance snapshots in SQL `ChatRuns`.
  - **RAM Memoization (`50–100 MB` Hard Cap):** Compiled preambles and agent metadata are memoized in an in-memory store (`.NET ConcurrentDictionary` in the single-host desktop runtime) with a hard cap of **50–100 MB** to eliminate per-turn SQL/disk read bottlenecks.
  - **Hot-Reloading Safety (`FileSystemWatcher`):** File watchers invalidate and refresh the RAM cache when source Markdown files change. Reloading preambles affects only subsequent prompts and never wipes active conversations.
  - **SQL Server Conversation State:** Active conversation histories remain persisted in SQL Server (`GuildhallDev`) as the single source of truth; storing full active conversation histories in RAM is excluded from the MVP to avoid cache-sync complexity.

## Local inference & hardware dynamics (Why Qwen can feel slow)

- **CPU vs. GPU Trade-offs:** For short conversational turns or smaller models, a modern CPU (such as the **AMD Ryzen 7 9800X3D** with its large 3D V-Cache / L3 cache) paired with fast DDR5 memory handles inference smoothly without a drastic perceived drop in speed compared to a GPU, while leaving the GPU available for games.
- **Memory Bandwidth Wall:** Dedicated GPUs remain essential for sustained loads, long context windows, and heavier quantization tiers due to vastly superior raw memory bandwidth (hundreds of GB/s on GPU VRAM vs. tens of GB/s on system DDR5 RAM).
- **VRAM Residency Tracking:** Monitor which models are currently loaded in VRAM (via Ollama `/api/ps` and host `keep_alive` timers) to surface live model residency and prevent unexpected cold-swap latency stalls across turns.
- **Core Inference Concepts:**
  - **Inference:** A trained model applying learned weights to unseen input data to generate predictions or text.
  - **Quantization:** Compressing model weights to lower precision to reduce VRAM/RAM footprints.
  - **KV Cache:** Storing key-value attention states from past tokens to accelerate subsequent token generation.
  - **Hallucination:** Confident generation of factually incorrect or fabricated outputs.
  - **Prompt Injection:** Malicious or external inputs attempting to override system-level instructions (mitigated via hardcoded ASP.NET Core backend guardrails and Tauri/Host process isolation, never prompt instructions alone).

The reviewed host defaults to four CPU threads with GPU offload disabled, and unloads the model after each request (`keep_alive: 0`) to free resources for other uses. It also enabled Qwen3 thinking while displaying only answer content. This backend change requests `think: false`, removing that explicit hidden-reasoning request. Reasoning fields returned anyway are still excluded from the saved answer. There is no measured speedup claim: automated coverage uses deterministic fake HTTP, not real inference.

The CPU/unload defaults remain intact. The existing Start-Dev.ps1 `-UseGpu` option permits automatic GPU offload for a future owner-chosen launch. Do not restart an existing session to change modes. Use an already installed smaller model for routine chat and compare a larger model on the same task before recommending downloads or hardware. Fine-tuning is not a remedy for slow token generation.

Official references: [Ollama thinking controls](https://docs.ollama.com/capabilities/thinking), [loading, GPU use and model retention](https://docs.ollama.com/faq).

## Current-phase tasks and acceptance

- **Qwen speed/quality gate (C16/Q06):** benchmark already installed models with the same short factual, multi-turn and practical task fixtures. Record CPU/GPU mode, model identity, cold load, time to first visible text, total time, VRAM residency (`/api/ps`), and tokens/second when actually reported. Distinguish model loading, prompt processing, hidden reasoning and output generation. Any optional bounded warm-retention setting must be explicit, reversible and measured for RAM/VRAM cost; preserve resource-conscious defaults. Report bad answers as well as latency. No training or download is implied by this task.
- **Member editing (B04/Q06):** implemented on `codex/profile-draft-save`. Profile drafts are keyed by member and saves serialize across members; council, session and new-session navigation stays available during typing and saves. A member with unsaved edits cannot send using stale instructions. Failed saves keep that member's draft and retry affordance; retry and subsequent edits apply only to that member. Typecheck, 39 frontend tests and production build pass, including switching during typing, edits during an in-flight save, failure/retry, editing another member, and changing a failed draft back to its original value. Independent `.cursor/agents/guildhall-ui-reviewer.md` review found no remaining actionable code issues. Native keyboard/live visual QC is still pending; the running app in the other worktree was left untouched.
- **Memory & Cross-Turn Budgets (B05/B06):** implement reviewed explicit SQL notes, actual request provenance, and cross-turn token/reasoning-effort budget tracking within the existing accepted scope; separately approve schema/grants. No silent whole-history sharing or automatic extraction.
- **Later capabilities:** source-backed research tools follow Phase 3 MCP/Phase 4 controlled workspaces (with hardcoded backend guardrails and tool execution rollbacks); cross-member execution and delegation dependency graphs follow Phase 7. Display unavailable capabilities honestly until implemented.

## Verification and owner QC

Warm-model option: `Start-Dev.ps1 -KeepModelWarmSeconds 300` now requests five minutes of model residency after a reply. Default 0 still unloads; the bounded maximum is 900 seconds. Direct hosts read `GUILDHALL_MODEL_KEEP_ALIVE_SECONDS`. Fake-provider/isolated SQL coverage checks both 0 and 300 in actual HTTP request payloads. Real RAM/VRAM and load-time measurements remain pending; retention consumes memory and Ollama can evict a model under pressure. This option takes effect on a new host launch, not the existing running session.

Backend verification: `dotnet test tests/Guildhall.AgentHost.Tests -c Release` with the approved isolated SQL test connection. Tests exercise captured behavior/version in SQL, actual fake-provider system messages, thinking disabled, and exclusion of returned reasoning from saved answers. No live model benchmark or UI render was performed.

After these changes are integrated into the active checkout, at the next agreed restart:

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\startup\Start-Dev.ps1
# Optional alternative for an explicitly GPU-enabled session:
pwsh -NoProfile -File G:\repos\guildhall\scripts\startup\Start-Dev.ps1 -UseGpu
```

Run one launch command when the previous development session is closed. In a retained member session, send a new request for a practical proposal based on supplied facts; verify a useful concrete result, modest tone and honest evidence limits. Ask for current research without providing sources and verify it does not claim a search it cannot perform. Compare the same short prompt using already installed models. Check SQL history survives. After the member editor branch is integrated, confirm that typing remains responsive while switching members/sessions, drafts return to the correct member, save failures retain the draft and retry, and messages wait for the active member's instruction save before sending.
