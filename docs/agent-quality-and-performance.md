# Agent quality and local performance

Owner request, November 16, 2026. This supplements the existing master plan and Phase 1; it does not move tools, delegation or shared memory ahead of their accepted boundaries.

## What is stored today

SQL Server stores member identity and Markdown-formatted instructions in AgentDefinitions; sessions in Conversations; immutable instruction/model/settings snapshots in ChatRuns; conversation text in ChatMessages; attachments in ChatAttachments. Completed turns from the selected session are included in later requests. The editor's Markdown text is a SQL field, not a memory file on disk. Application data stays local.

There is no implemented shared About you collection, private cross-session member memory or project memory. B05/B06 in the council design already cover explicit SQL notes, retrieval scopes and an inspector. Project scope waits for real project ownership. Saving a chat or changing instructions does not train model weights.

## Implemented backend behavior

New accepted runs capture the version and full text of a short common Guildhall behavior policy in the existing SettingsSnapshotJson field. The member's own instructions remain separately captured in InstructionsSnapshot. Execution joins those snapshots into the system message. Already accepted runs without the policy retain their original behavior; changing a policy does not rewrite history.

The policy asks members to take ownership, finish useful work within actual capabilities, check results, distinguish facts from assumptions, and support research with evidence actually available. The voice is a capable colleague with occasional understated guild language. It prohibits invented citations, tool calls, test claims or access to other members' conversations. It grants no new runtime capability. A Manager role cannot command other agents until bounded delegation is implemented in Phase 7.

Prompts can improve habits and tone; capability still depends on model quality, relevant context and available tools. No instruction can supply live research when research tools are unavailable. Grounded research should use supplied evidence today; actual tool access follows the existing MCP/controlled-tools phases.

## Why Qwen can feel slow

The reviewed host defaults to four CPU threads with GPU offload disabled, and unloads the model after each request (`keep_alive: 0`) to free resources for other uses. It also enabled Qwen3 thinking while displaying only answer content. This backend change requests `think: false`, removing that explicit hidden-reasoning request. Reasoning fields returned anyway are still excluded from the saved answer. There is no measured speedup claim: automated coverage uses deterministic fake HTTP, not real inference.

The CPU/unload defaults remain intact. The existing Start-Dev.ps1 `-UseGpu` option permits automatic GPU offload for a future owner-chosen launch. Do not restart an existing session to change modes. Use an already installed smaller model for routine chat and compare a larger model on the same task before recommending downloads or hardware. Fine-tuning is not a remedy for slow token generation.

Official references: [Ollama thinking controls](https://docs.ollama.com/capabilities/thinking), [loading, GPU use and model retention](https://docs.ollama.com/faq).

## Current-phase tasks and acceptance

- **Qwen speed/quality gate (C16/Q06):** benchmark already installed models with the same short factual, multi-turn and practical task fixtures. Record CPU/GPU mode, model identity, cold load, time to first visible text, total time and tokens/second when actually reported. Distinguish model loading, prompt processing, hidden reasoning and output generation. Any optional bounded warm-retention setting must be explicit, reversible and measured for RAM/VRAM cost; preserve resource-conscious defaults. Report bad answers as well as latency. No training or download is implied by this task.
- **Member editing (B04/Q06), frontend pending:** profileDirty becomes true on each keystroke; the sidebar disables profile selection, sessions and creation for both dirty and saving states, and the global disabled style reduces opacity. Preserve a per-member draft and serialize saves, or flush/await save before navigation, rather than globally dimming the council during typing. Tests must cover continuous typing, editing during an in-flight save, switching member/session, save failure/retry and revision conflicts without lost rules. Keep send/archive operations from using stale instructions. Normal and narrow-window keyboard/native visual review are required. The backend session has not changed or visually verified this UI.
- **Memory (B05/B06):** implement reviewed explicit SQL notes and actual request provenance within the existing accepted scope; separately approve schema/grants. No silent whole-history sharing or automatic extraction.
- **Later capabilities:** source-backed research tools follow Phase 3 MCP/Phase 4 controlled workspaces; cross-member execution follows Phase 7. Display unavailable capabilities honestly until implemented.

## Verification and owner QC

Warm-model option: `Start-Dev.ps1 -KeepModelWarmSeconds 300` now requests five minutes of model residency after a reply. Default 0 still unloads; the bounded maximum is 900 seconds. Direct hosts read `GUILDHALL_MODEL_KEEP_ALIVE_SECONDS`. Fake-provider/isolated SQL coverage checks both 0 and 300 in actual HTTP request payloads. Real RAM/VRAM and load-time measurements remain pending; retention consumes memory and Ollama can evict a model under pressure. This option takes effect on a new host launch, not the existing running session.

Backend verification: `dotnet test tests/Guildhall.AgentHost.Tests -c Release` with the approved isolated SQL test connection. Tests exercise captured behavior/version in SQL, actual fake-provider system messages, thinking disabled, and exclusion of returned reasoning from saved answers. No live model benchmark or UI render was performed.

After these changes are integrated into the active checkout, at the next agreed restart:

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\Start-Dev.ps1
# Optional alternative for an explicitly GPU-enabled session:
pwsh -NoProfile -File G:\repos\guildhall\scripts\Start-Dev.ps1 -UseGpu
```

Run one launch command when the previous development session is closed. In a retained member session, send a new request for a practical proposal based on supplied facts; verify a useful concrete result, modest tone and honest evidence limits. Ask for current research without providing sources and verify it does not claim a search it cannot perform. Compare the same short prompt using already installed models. Check SQL history survives. Sidebar typing QC waits for the separate frontend fix.
