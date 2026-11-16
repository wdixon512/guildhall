# Personal AI Ecosystem: Master Architecture and Implementation Plan

**Version:** 1.3 (Unified "One Guildhall" architecture, local inference dynamics, preamble memoization, backend guardrails, and 5-tier resilience)
**Prepared:** November 16, 2026
**Status:** Active architectural baseline (Phase 0 complete; Phase 1 in progress)
**Primary objective:** Build a local-first, self-hosted desktop AI command center ("Guildhall") where a council of persistent, named Guild Members can be powered by local models (Ollama), direct Cloud Model APIs (Gemini, Claude, OpenAI), or wrapped local CLI Agent Harnesses (Gemini CLI, Claude Code, Codex CLI, Cursor CLI), sharing unified SQL Server state, hot-reloaded Markdown preambles memoized in RAM, MCP tools, diff reviews, 5-tier health resilience, and hardcoded backend permission gates.

---

## 1. Executive decision

**Build the desktop command center and independent `.NET` agent host first, anchored by SQL Server ownership of all conversations, council profiles, memory, and run provenance.** Do not fragment workflows across separate vendor web apps, terminal silos, or IDE plugins. Guildhall itself is the primary workspace where you converse with agents, inspect context, review plans and code diffs, and approve consequential actions—opening external editors (VS Code, Cursor, Visual Studio) only when you want to manually edit code.

The implementation sequence is:

1. **Phase 0 (Completed):** Establish a working Tauri 2 desktop shell, authenticated loopback ASP.NET Core (`.NET 10`) agent host, restricted SQL Server migration/runtime split, and local Ollama model discovery.
2. **Phase 1 (In Progress):** Deliver the Local AI Workspace & Council MVP—persistent Council Member profiles (`AgentDefinitions`), multi-session conversations, committed-snapshot streaming, bounded per-session queueing, attachments, Markdown rules, and reviewed SQL memory (`About you` and Member notes).
3. **Phase 2:** Add the Unified Backing-Engine Abstraction—supporting both **Direct Cloud Model APIs** (Google Gemini API, Anthropic Claude API, OpenAI API via API keys) and **Wrapped Local CLI Agent Harnesses** (Gemini CLI, Claude Code, OpenAI Codex CLI, Cursor Agent CLI via ACP / headless JSON streams) as selectable brains per Council Member or session.
4. **Phase 3:** Add local MCP tool discovery, invocation, permission enforcement, and trace visibility, plus external service integrations (GitHub, Google Drive, Gmail) through the shared host tool gate.
5. **Phase 4:** Build bounded, approval-driven coding workflows inside Guildhall (local project registration, Git worktree isolation, host-enforced command sandboxing, native diff review, and `Always ask` / `Ask for unsafe` / `Unrestricted` policies enforced across both native loops and wrapped CLI harnesses).
6. **Phase 5:** Connect existing home SQL Server (read-only MCP) and DigitalOcean infrastructure over private networking (WireGuard/Tailscale).
7. **Phase 6:** Add durable background jobs, local scheduled automations, and remote execution hosts.
8. **Phase 7:** Add cross-member conversation reassignment ("Assign to guild member" context handoff) and multi-agent delegation across heterogeneous engines.
9. **Phase 8:** Move local model execution to dedicated hardware only when benchmarks justify it.

**Milestone 1 (Phase 1 target):** Open Guildhall, configure a Council of named Guild Members backed by local Ollama models, converse with progressive saved output, queue or stop turns, attach files, close and reopen the app, and resume any session with full provenance—without requiring any cloud account, subscription, or external editor.

---

## 2. Vision and priorities

### The problem

AI workflows are scattered across incompatible surfaces: consumer web apps (ChatGPT, Claude, Gemini), terminal-only coding CLIs (`gemini`, `claude`, `codex`), and locked-in IDE extensions (Gemini Code Assist, Cursor, Copilot). Each surface traps its own conversation history, memory, instructions, authentication, and tool permissions. Developers also face a billing dilemma: paying monthly for Pro/Max/Google subscriptions while being asked to pay a second time per token for raw API keys just to orchestrate their own workflows.

### Desired user experience ("One Guildhall")

- **One primary command center:** A single desktop application for talking to your Council of AI specialists, reviewing their plans and code diffs, inspecting context budgets, and approving actions.
- **Any brain per Council Member:** Assign any Guild Member (or individual session) to run on a **Local Model** (Ollama), a **Cloud Model API** (Gemini API, Claude API, OpenAI API), or an **Installed CLI Agent Harness** (Gemini CLI, Claude Code, Codex CLI, Cursor CLI)—allowing you to reuse existing subscription logins alongside pay-as-you-go APIs and free local inference.
- **Persistent Council & SQL-owned memory:** Every Guild Member has its own role, portrait, versioned Markdown rules, private member memory, and shared reviewed "About you" memory stored in your local SQL Server database—never locked inside a vendor's cloud or CLI cache.
- **Seamless cross-member handoff:** Brainstorm architecture with one Guild Member (e.g., backed by Claude or Gemini Pro), then reassign or hand off the conversation's context to another Guild Member (e.g., a Builder backed by Codex/Gemini CLI or a local Qwen model) without losing history or provenance.
- **Unified tool & service integrations:** Connect local repositories, filesystem, Git worktrees, build/test runners, SQL Server, GitHub, Google Drive, and Gmail once through Guildhall's host, and scope them per Council Member.
- **Explicit planned placeholders:** When future capabilities are previewed in the UI before their implementation phase (e.g., provider cards, session tool tabs, access modes, or conversation handoff menus), they appear as clearly labelled, disabled/greyed-out controls that never imply fake connectivity.

### Non-negotiable design principles

1. **Local-first:** Core operation works offline with locally installed model inference (Ollama) and local SQL Server. Optional cloud APIs and CLI harnesses degrade gracefully when offline.
2. **Self-hostable:** No mandatory orchestration SaaS, hosted database, hosted authentication, telemetry service, or provider-specific agent SDK owning application records.
3. **Provider- and harness-agnostic:** Model APIs and external CLI agents are replaceable execution adapters. Guildhall's SQL Server database is the single source of truth for Council profiles, transcripts, memory notes, and run provenance.
4. **Separate interface from execution:** The Tauri/React desktop UI is an authenticated client; the ASP.NET Core host owns state, database access, provider/CLI orchestration, and permission enforcement.
5. **MCP for capability integration; ACP/JSON-stream for external CLI harnesses:** Tools attach via MCP; external CLI agents attach via **ACP (Agent Client Protocol)** over `stdio` JSON-RPC 2.0 or structured headless JSON streams (`--output-format stream-json`). Neither bypasses Guildhall's permission gate.
6. **Least privilege by default:** Every Council Member and scheduled task receives only explicitly approved, scoped capabilities.
7. **Lightweight orchestrator:** Connected model runtimes and agent harnesses do the heavy compute. Avoid embedding inference engines, duplicate file indexers, or permanent background busy loops in the desktop shell.
8. **Spend-conscious context & docs:** Both human developers and coding agents read only the relevant phase subfolder (`docs/masterplan/phase N/`) and scoped context before exploring code or spending tokens.
9. **Traceability:** Record per-run model/harness snapshots, instruction revisions, memory provenance, tool calls, approvals, outcomes, and errors, with secrets redacted.
10. **Replaceable infrastructure:** Local inference and background execution can move to a home server or droplet without changing UI contracts.

---

## 3. Terminology and the 3-Pillar Integration Model

To wrap all AI services into one Guildhall without architectural confusion, these concepts must remain distinct:

| Concept | Meaning | Examples |
|---|---|---|
| **Council Member (`AgentDefinition`)** | Named, persistent Guildhall specialist with identity, Markdown rules, memory scope, and default backing engine | Maren (Steward), Architect, Backend Builder, QA Reviewer |
| **Pillar 1: Model Provider (Stateless API)** | Supplies raw model inference (text/vision/tool-call generation) while Guildhall's `.NET` host runs the prompt/tool loop | Local Ollama (`127.0.0.1:11434`), Google Gemini API, Anthropic API, OpenAI API |
| **Pillar 2: External Agent Harness (CLI Bridge)** | Locally installed CLI agent spawned by Guildhall over `stdio` (ACP JSON-RPC or `stream-json`), reusing CLI OAuth logins or keys | Gemini CLI (`gemini --acp` / `-p`), Claude Code (`claude -p`), OpenAI Codex CLI (`codex exec`), Cursor CLI (`agent -p`) |
| **Pillar 3: Tool / Service Integration (MCP)** | Concrete capability or external data source exposed to Council Members behind Guildhall's permission gate | Local Filesystem/Git/Terminal/SQL MCPs; GitHub, Google Drive, Gmail integrations |
| **Agent Host (`Guildhall.AgentHost`)** | Project-owned ASP.NET Core (`.NET 10`) service owning SQL persistence, context assembly, run supervision, and security policy | Loopback host on `127.0.0.1:43120` |
| **Workspace / Project** | User-authorized local folder or Git repository with scoped rules, worktrees, and project memory | `G:\repos\guildhall` |

### Why Pillar 1 (Direct APIs) and Pillar 2 (CLI Harnesses) both exist

A consumer or pro subscription (Google One / Gemini Code Assist, Claude Pro/Max, ChatGPT Plus/Pro, Cursor Pro) often does **not** grant raw per-token REST API keys—instead, those vendors provide official terminal agents (**Gemini CLI**, **Claude Code**, **Codex CLI**, **Cursor CLI**) that authenticate via browser OAuth and expose programmatic headless modes (`--acp` or `--output-format stream-json`).

By supporting **both** Pillar 1 and Pillar 2 behind a unified Council Member backing-engine selector:
- You can drive a Council Member via **Local Ollama** (100% offline, zero marginal cost).
- You can drive a Council Member via **Direct Cloud REST APIs** when you want deterministic HTTP control, exact token/cost accounting, and Guildhall's native `.NET` MCP loop.
- You can drive a Council Member via **Wrapped CLI Agent Harnesses** when you want to leverage your existing Pro/Max/Google logins or a vendor's specialized coding loop—**while keeping the entire experience inside Guildhall's UI, SQL transcript, and approval gates.**

---

## 4. Architectural overview

```text
                        GUILDHALL DESKTOP WORKSPACE
                       Tauri 2 + React + TypeScript
         Council / Sessions / Context Inspector / Diffs / Approvals
                                     |
                 authenticated loopback HTTP (127.0.0.1:43120)
                                     |
                       GUILDHALL AGENT HOST (.NET 10)
        SQL Server persistence, Council profiles, Memory notes, Queue,
         Context Builder, Run Supervisor, Permission & Approval Gate
            /                        |                          \
   PILLAR 1: MODEL APIs     PILLAR 2: CLI HARNESSES     PILLAR 3: MCP & SERVICES
   (Host runs agent loop)   (Host bridges ACP/JSONL)    (Host enforces tool gate)
   - Local Ollama           - Gemini CLI (gemini)       - Local Git / Worktrees
   - Google Gemini API      - Claude Code (claude)      - Scoped Filesystem
   - Anthropic Claude API   - OpenAI Codex (codex)      - Controlled Terminal
   - OpenAI API             - Cursor CLI (agent)        - SQL Server (Read-Only)
                                                        - GitHub / Drive / Gmail
                                     |
                    LOCAL & PRIVATE INFRASTRUCTURE
          Dedicated SQL Server (GuildhallDev), OS Credential Store,
                 Home GPU/CPU, Optional WireGuard -> DO Droplet
```

### Desktop client (`apps/desktop`)

- **Tauri 2 shell + React + strict TypeScript + Vite.**
- **Primary command center:** Renders the Council sidebar (members and nested sessions), markdown chat transcript, queued follow-up blade, Member identity/rules editor, Session Tools panel (Git changes, Terminal, Plan, Context usage, Browser/QC, Side chat, Scheduled tasks), Connections catalog, and Workspace Settings.
- **Strict boundary:** No direct SQL Server, Ollama, cloud API, or subprocess access from the webview. Communicates exclusively with `Guildhall.AgentHost` over authenticated loopback HTTP using an in-memory session bearer token supplied via a narrow Tauri bootstrap command.
- **External IDE relationship:** Guildhall does not depend on VS Code or JetBrains extensions (such as Gemini Code Assist IDE plugins). Users review plans, diffs, and approvals directly in Guildhall and open external editors only when they choose to edit files manually.

### Independent agent host (`services/agent-host`)

- **ASP.NET Core service targeting .NET 10 LTS**, kept in a single host project (`Guildhall.AgentHost.csproj`) with focused internal modules until a real deployment boundary requires extraction.
- Bound to `127.0.0.1:43120` in desktop mode; rejects unauthenticated requests, unapproved `Origin` headers, and non-loopback `Host` headers.
- Owns SQL Server access (`GuildhallDbContext`), migration readiness checks (`/ready`), run admission, per-session queueing (up to 8 queued turns with `Send Now` promotion), committed-snapshot checkpoints, cancellation, and startup reconciliation of interrupted runs.
- Enforces the workspace permission policy (`Always ask`, `Ask for potentially unsafe actions`, `Unrestricted / autopilot`) below the model and harness boundary.

### Backing-Engine Contract (Pillar 1 Providers + Pillar 2 CLI Harnesses)

Every Council Member profile (`AgentDefinition`) specifies a default backing engine, and each session (`Conversation`) may override it for future turns:

1. **Direct Model Providers (Pillar 1):**
   - **Ollama Adapter (Implemented):** Streams `/api/chat` NDJSON over loopback HTTP with bounded buffers, thinking-field filtering, vision/text attachment support, and explicit `keep_alive` / CPU-vs-GPU controls.
   - **Cloud API Adapters (Phase 2):** Normalized HTTP/SSE adapters for **Google Gemini API**, **Anthropic Claude API**, and **OpenAI API**. Credentials live in Windows Credential Manager / DPAPI; SQL stores only profile metadata and secret references.
2. **External CLI Agent Harnesses (Pillar 2 — Phase 2/4):**
   - **Subprocess Bridge:** Spawns installed CLI binaries (`gemini`, `claude`, `codex`, `agent`) in non-interactive headless mode—preferring **ACP (`--acp` JSON-RPC 2.0 over `stdio`)** where available, or **streaming JSONL (`-p --output-format stream-json` / `codex exec --json`)**.
   - **SQL Source-of-Truth Rule:** Guildhall never lets a CLI trap the conversation in an opaque external folder. Before invoking a CLI harness, `Guildhall.AgentHost` assembles the bounded context (Guildhall behavior policy + Member Markdown rules + approved `About you` / Member / Project memory notes + eligible session history) and passes it to the harness. As the harness streams text, tool requests, and file diffs back over `stdout`, the host checkpoints them into `ChatRuns` and `ChatMessages` just like any other run.
   - **Mediated Approvals & Environment Scrubbing:** Child CLI processes receive a scrubbed environment (never exposing `GUILDHALL_SESSION_TOKEN`, `ConnectionStrings__*`, or unrelated provider keys) and a working directory scoped to the active project/worktree. Tool executions that mutate files, run shell commands, or contact external networks are gated by Guildhall's approval policy rather than defaulting to unrestricted `--yolo` flags.

### Tool & Service Layer (Pillar 3 — MCP)

- Uses the official **MCP C# SDK** in `Guildhall.AgentHost` to connect to local `stdio` MCP servers and authenticated remote **Streamable HTTP** MCP servers.
- Categorizes every capability into explicit security tiers (Tier 0: None, Tier 1: Read-only local, Tier 2: Controlled workspace writes, Tier 3: Sensitive/remote/external services like GitHub, Google Drive, Gmail, or SQL writes).
- Logs all tool invocations, arguments, approvals, and results with automatic secret redaction.

### Persistence, Caching & Data Ownership

- **Canonical Storage vs. Runtime Memory (Preambles & Configurations):**
  - **Markdown Files on Disk:** Serve as the version-controlled source of truth on disk for base agent preambles, persona definitions, and rule configurations (paired with immutable per-run snapshots in SQL `ChatRuns`).
  - **RAM Memoization (`50–100 MB` Hard Cap):** Compiled system preambles and agent metadata are loaded at runtime into an in-memory store (`.NET ConcurrentDictionary` in single-host desktop mode; Redis optional only if scaled to distributed hosts later) with a strict **50–100 MB** hard allocation cap to eliminate SQL/disk read bottlenecks on every conversational turn.
  - **Hot-Reloading Safety (`FileSystemWatcher`):** File watchers monitor source Markdown preamble files on disk and automatically invalidate/refresh the in-memory `ConcurrentDictionary` cache when modified. Reloading preambles affects only subsequent prompts/runs and never wipes or mutates active conversations or in-flight runs.
- **Conversation State Persistence (`GuildhallDev` SQL Server):**
  - **SQL Server as Single Source of Truth:** Active conversation histories (`Conversations`, `ChatRuns`, `ChatMessages`, `ChatAttachments`), `GuildhallBehaviorSettings`, `AgentDefinitions`, and planned `MemoryNotes` / `Workspaces` / `Schedules` live in the dedicated `GuildhallDev` SQL Server database, guaranteeing ACID durability across application and host restarts.
  - **MVP Scoping (No Full-Transcript RAM Cache):** Storing full active conversation histories in RAM is unnecessary for the MVP; SQL Server provides sufficient performance and reliability for a single-user desktop architecture without introducing dual-write cache-synchronization complexity.
- **Separate SQL Identities:** Explicit schema migrations run under the owner's Windows identity (`scripts/database/Migrate-Database.ps1` with preview-by-default and `-Execute` after approval). The running host uses the restricted `guildhall_runtime` SQL login with table-specific DML grants and zero DDL/`db_owner` authority.
- **OS-Protected Secret Storage:** Runtime SQL credentials, Cloud API keys, and OAuth tokens are stored via Windows DPAPI / OS credential APIs (`%LOCALAPPDATA%\Guildhall\`), never in SQL tables, frontend bundles, or source control.

---

## 5. Stack selection

| Layer | Choice | Notes |
|---|---|---|
| Desktop shell (Tier 1) | Tauri 2 | Native window, strict CSP/capabilities, process-isolated from backend host |
| Frontend | React + strict TypeScript + Vite | Custom "The Council" design system; zero heavy UI/state frameworks |
| Agent host (Tier 2) | ASP.NET Core (.NET 10 LTS) | Central orchestrator managing agent lifecycles, reasoning loops, tool bindings, and hardcoded security guardrails |
| Preamble & metadata cache | In-memory `.NET ConcurrentDictionary` (`50–100 MB` cap) | Hot-reloaded via `FileSystemWatcher` from version-controlled Markdown files on disk |
| Host-to-UI transport | Authenticated REST + 250ms Snapshot Polling (SSE optional) | Phase 1 uses committed SQL snapshot polling (`GET /conversations/{id}`); bounded SSE/events available where needed |
| Persistence (Tier 3) | SQL Server (EF Core migrations) | Dedicated `GuildhallDev` database; single source of truth for active conversation histories |
| Local inference (Tier 4) | Ollama (`127.0.0.1:11434`) | Configurable CPU/GPU policy, warm retention (`0–900s`), and VRAM residency tracking |
| Cloud Model APIs | Gemini API, Claude API, OpenAI API | Direct HTTP adapters in `.NET` host; opt-in via OS-stored API keys |
| CLI Agent Harnesses | Gemini CLI, Claude Code, Codex CLI, Cursor CLI | Driven via `stdio` ACP (JSON-RPC 2.0) or `--output-format stream-json` |
| Tool protocol (Tier 5) | Official MCP C# SDK | Local `stdio` and remote Streamable HTTP MCP servers with health checks and rollback hooks |
| Secret storage | Windows DPAPI / OS Credential Manager | No plaintext tokens in SQL, logs, or env files |
| Private networking | WireGuard (Tailscale optional) | Used in Stage B/Phase 5 for private Droplet/home connectivity |
| Automated testing | xUnit (deterministic + isolated `GuildhallTest_*` SQL), Vitest, Playwright | Real isolated SQL databases and deterministic fake HTTP/CLI transports |

**Intentionally excluded for MVP:** Full conversation history caching in RAM, mandatory Redis (`.NET ConcurrentDictionary` handles the 50–100 MB preamble memoization cap locally), VS Code extension dependencies, Kubernetes, dedicated vector databases, LangGraph/external orchestration frameworks, mandatory Docker, and hosted control planes.

---

## 6. Product UX: The Council & Command Center

### Visual identity and shell

- **The Council metaphor:** Warm brass hairlines, charcoal surfaces, customizable member accents, portrait medallions with six bounded activity-driven poses (`neutral`, `pondering`, `idea`, `reading`, `surprised`, `laughing`), and clean typography.
- **Three-zone resizable workspace:**
  1. **Left Navigation Rail & Council Sidebar:** Lists active and archived Council Members (`AgentDefinitions`) with their sessions (`Conversations`) nested beneath each member. Supports collapsible member accordions, session search, unread reply dots, archive/restore, permanent deletion with confirmation, and session context actions.
  2. **Center Primary Pane:** Transcript view with progressive Markdown rendering, inline image/file attachments, per-turn model/engine provenance, elapsed response duration, and a compact composer with an adjacent **Queued Messages** blade (`Send Now`, `Edit`, `Remove`). Also hosts the **Connections** catalog and **Workspace Settings** overlays.
  3. **Right Collapsible Context Pane:** Toggles between the **Guild Member Editor** (name, role, portrait, accent, versioned Markdown rules, and memory scope) and **Session Tools** (`Git changes`, `Terminal`, `Plan`, `Context usage`, `Browser`, `Side chat`, `Scheduled tasks`).

### Connections Catalog: 3-Pillar Clarity

Rather than mixing model APIs, CLI agents, and data tools into one undifferentiated list, the Connections surface groups integrations by what they actually provide:

1. **Local Runtime & Database:** Agent Host status, SQL Server (`GuildhallDev`) readiness, and Local Ollama models (`127.0.0.1:11434`).
2. **Cloud Model APIs & CLI Agent Harnesses (Backing Engines):**
   - **Google Gemini:** Gemini API (API key) & Gemini CLI (`gemini --acp` / headless OAuth).
   - **Anthropic Claude:** Claude API (API key) & Claude Code CLI (`claude` headless/ACP).
   - **OpenAI / Codex:** OpenAI API (API key) & Codex CLI (`codex exec` / app-server).
   - **Cursor:** Cursor Agent CLI (`agent -p --output-format stream-json`).
3. **Workspace & Service Integrations (MCP Tools):**
   - **GitHub**, **Google Drive**, **Gmail**, and **SQL Server Read-Only MCP**.

Every card displays an honest state (`Connected`, `Not configured`, `Needs sign-in`, `Unavailable`, or `Planned`) and never implies that connecting one account grants access to another service.

### Cross-Member Collaboration & Conversation Reassignment

Because SQL Server owns the normalized transcript and memory notes:
- **Switch engine mid-session:** You can change a session's backing model/engine at any time; future accepted runs use the new engine while historical turns keep their immutable provenance.
- **Assign conversation to another Guild Member ("Pass along context"):** Right-clicking a session exposes **Assign to guild member…** (rendered as a disabled preview option until Phase 7). When active, reassigning a session opens an explicit **Context Handoff / Edit Mode** where you select the target Council Member and choose whether to transfer the full eligible transcript, a reviewed summary, or selected messages/attachments—recording a durable handoff event in SQL so the receiving specialist starts with clean, auditable context.

---

## 7. Common contracts, persistence, and execution lifecycle

### 7.1 Core SQL entities (Implemented & Planned)

- **`GuildhallBehaviorSettings` (Implemented):** Singleton versioned table storing the workspace-wide Guildhall behavior policy captured into every run's snapshot.
- **`AgentDefinitions` (Implemented, extended in Phase 2/3):** Persistent Council Member profiles (`Id`, `Name`, `Role`, `PortraitId`, `CustomPortrait`, `Accent`, `Instructions`, `DefaultModelId`, `IsArchived`, `Revision`; extended in Phase 2 with `DefaultProviderId` / `HarnessMode` and in Phase 3 with `EnabledToolIds` / `PermissionPolicy`).
- **`Conversations` (Implemented):** Sessions linked to an `AgentDefinitionId`, with `Title`, `SelectedModelId` (and future `SelectedProviderId`), `IsArchived`, `LastResponseUtc`, and optimistic `Revision`.
- **`ChatRuns` (Implemented):** Durable execution record per turn (`TurnNumber`, `ClientRequestId`, `RequestFingerprint`, `Status` [`Queued`, `Running`, `Cancelling`, `Completed`, `Failed`, `Cancelled`, `Interrupted`], timing, error code, and immutable snapshots of member instructions, workspace behavior policy, provider/harness ID, model ID, and context settings).
- **`ChatMessages` & `ChatAttachments` (Implemented):** User/assistant message pairs and bounded `varbinary(max)` file attachments (up to 5 files, 20 MiB each, 50 MiB per turn).
- **`MemoryNotes` (Phase 1 B05/B06):** Versioned SQL notes scoped to `AboutYou` (shared across the council after user review), `Member` (private to one Guild Member across its sessions), or `Project` (scoped to an authorized workspace in Phase 4).

### 7.2 System Instructions & Context Management

- **Static System Preambles:** Configure base instructions as static `system` messages (or Modelfile parameters) in backend API calls once per request rather than repeating them across conversational turns. This preserves prompt prefix consistency so local and hosted engines can reuse cached system-prefix states.
- **Context Preservation:** Conversational continuity is maintained by passing the ordered historical message array of completed turns back to the model API per turn, supplemented by KV caching on warm/larger models and planned backend state compression / reviewed compaction for long-running sessions.
- **Modular Instruction Separation:**
  - **Tunable Parameters (User/Config-facing):** Council Member persona/role definitions, primary task objectives, and operational mode toggles (such as reasoning effort / thinking controls and model selection).
  - **Global Guidelines (System-wide):** Concise operational boundaries (`GuildhallBehaviorSettings` / system-wide guidelines) instructing agents to rely strictly on verified tool outputs, respect workspace scope, and never fabricate system states, citations, or benchmark metrics.

### 7.3 Caching & Persistence Architecture

- **Canonical Storage vs. Runtime Memory:**
  - **Markdown Files:** Serve as the version-controlled source of truth on disk for agent preambles and configurations, paired with SQL profile records and immutable per-run provenance snapshots in `ChatRuns`.
  - **RAM Memoization (`50–100 MB` Hard Cap):** Load compiled preambles and agent metadata into an in-memory store (`.NET ConcurrentDictionary` in the single-host desktop runtime; Redis optional only if scaled out later) at runtime to eliminate SQL/disk read bottlenecks per turn. Allocate a strict hard cap of **50–100 MB** for preamble and metadata caching.
  - **Hot-Reloading Safety:** Use file watchers (`FileSystemWatcher`) to invalidate and refresh the RAM cache automatically when source Markdown files change on disk. Reloading preambles affects only subsequent prompts/runs and never wipes or alters active conversations.
- **Conversation State Persistence:**
  - **SQL Server (`GuildhallDev`):** Acts as the single source of truth for active conversation histories, ensuring durability across application and host restarts.
  - **MVP Scoping:** Storing full active conversation histories in RAM is unnecessary for MVP; SQL Server provides sufficient performance and reliability for a single-user desktop architecture without added cache-sync complexity.

### 7.4 Streaming, checkpoints, and event model

- **Committed-Snapshot Polling (Active Phase 1 transport):** The runner batches incoming fragments from the active provider/harness into atomic SQL checkpoints (every ~500ms, 4 KiB, or terminal state). The desktop polls `GET /conversations/{id}` every 250ms with one request in flight and smoothly reveals only committed text.
- **Structured Run Events & Approvals (Phase 3/4 extension):** When tools or CLI harnesses are active, runs additionally record structured step events (`tool.requested`, `approval.requested`, `approval.resolved`, `tool.completed`, `tool.rolled_back`, `diff.proposed`, `agent.handed_off`, `agent.delegated`) so the UI can pause a run for human approval and render full tool traces in the Run Inspector.

### 7.5 Additional State Tracking Requirements

1. **Tool Execution Rollbacks (Phase 3/4):** Implement transactional or compensating rollback mechanisms for failed, rejected, or cancelled tool actions—such as Git worktree/file checkpoint rollback for workspace mutations, compensating cleanup steps for multi-step local operations, and explicit status tracking distinguishing reversible local actions from non-reversible external side effects.
2. **VRAM Residency Tracking (Phase 1/2/8):** Monitor which models are currently loaded in VRAM/RAM (via Ollama `/api/ps` alongside host `keep_alive` retention timers) to surface live model residency in the UI, inform routing, and prevent unexpected cold-swap latency stalls across turns or Council Members.
3. **Cross-Turn Budgets (Phase 1/2/7):** Track cumulative token usage, context consumption, and reasoning-effort limits across multi-turn sessions and delegated child runs, enforcing configurable session budgets before admitting new turns.
4. **Delegation Dependency Graphs (Phase 7):** Maintain durable directed execution graphs (DAGs) in SQL Server when multiple Council Members or sub-agents delegate tasks to one another—tracking parent/child run edges, blocking/awaiting states, inherited budgets, and cascading cancellation/error propagation.

---

## 8. Local inference & hardware dynamics

### 8.1 Hardware & resource defaults

- Primary desktop pairs a modern high-cache CPU (such as the **AMD Ryzen 7 9800X3D** with large 3D V-Cache / L3 cache and fast DDR5 system memory) with an **NVIDIA RTX 3070 (8 GB VRAM)**.
- To keep the desktop responsive for gaming and creative work, `Guildhall.AgentHost` defaults to **CPU-only inference (4 threads, 4096 context, `keep_alive: 0` unload-after-reply, `think: false`)**.
- Users can opt into GPU acceleration (`-UseGpu` / `GUILDHALL_USE_GPU=true`) and bounded warm retention (`-KeepModelWarmSeconds 0..900`) via the launcher or Workspace Resource Settings, backed by **VRAM Residency Tracking** to prevent cold-swap latency stalls when conversing with active models.
- Benchmark installed models (e.g., `qwen3:1.7b`, `qwen3:4b`, `qwen3-vl:2b/4b`, and 7B/8B coding models) on cold load, time-to-first-token, tokens/sec, RAM/VRAM residency, and instruction adherence before changing defaults.

### 8.2 CPU vs. GPU Trade-offs & the Memory Bandwidth Wall

- **CPU vs. GPU Trade-offs:** For short conversational turns or smaller models, a modern CPU (such as the AMD Ryzen 7 9800X3D with its large L3 cache) paired with fast DDR5 memory handles inference smoothly without a drastic perceived drop in speed compared to a GPU, while leaving dedicated VRAM completely uncontested for games or graphics workloads.
- **Memory Bandwidth Wall:** Dedicated GPUs remain essential for sustained loads, long context windows, and heavier quantization tiers due to vastly superior raw memory bandwidth (hundreds of GB/s on GPU VRAM vs. tens of GB/s on system DDR5 RAM). As context length or model weight size grows beyond L3 cache locality, token generation becomes strictly memory-bandwidth bound.

### 8.3 Core Inference Concepts

- **Inference:** A trained model applying learned weights to unseen input data to generate predictions or text (as distinct from training or fine-tuning, which modify model weights).
- **Quantization:** Compressing model weights to lower numerical precision (e.g., FP16 to Q4_K_M / Q8_0) to reduce VRAM and system RAM footprints while balancing output quality.
- **KV Cache:** Storing key-value attention states from past tokens in memory to accelerate subsequent token generation without recomputing prior context—making static system preambles and warm model residency critical for multi-turn responsiveness.
- **Hallucination:** Confident generation of factually incorrect or fabricated outputs; mitigated in Guildhall through global guidelines requiring strict reliance on tool outputs, explicit evidence provenance, and temperature/reasoning controls.
- **Prompt Injection:** Malicious or untrusted external inputs (inside files, web pages, attachments, or database rows) attempting to override system-level instructions or hijack tool execution; mitigated via hardcoded ASP.NET Core backend guardrails and process isolation rather than prompt instructions alone.

---

## 9. MCP infrastructure and tool tiers

### Tool authorization tiers

- **Tier 0 — No external tools:** Pure conversation, context inspector, and SQL-backed memory notes.
- **Tier 1 — Read-only local workspace (Default):** List/read files inside an authorized project root, inspect Git status/log/diffs, and query read-only SQL views/schemas.
- **Tier 2 — Controlled workspace writes & execution:** Edit files in an isolated Git worktree (with transactional/compensating **Tool Execution Rollbacks**), run allowlisted build/test commands in a scrubbed environment, and propose diffs for review.
- **Tier 3 — Sensitive, external, or destructive operations:** Git commits/pushes, database writes, GitHub PR creation, Gmail/Google Drive actions, remote SSH/Droplet commands. Enforced by mandatory human-in-the-loop approval gates in the ASP.NET Core host.

---

## 10. Coding workflows, CLI harnesses, and sandbox boundaries

### How coding works inside Guildhall

Whether a Council Member is using Guildhall's native `.NET` MCP loop (with Ollama / Gemini API / Claude API / OpenAI API) **or** a wrapped CLI Agent Harness (Gemini CLI / Claude Code / Codex CLI / Cursor CLI):

1. **Project attachment:** The user selects a local folder or Git repository via Tauri's native picker (no GitHub account required).
2. **Worktrees vs. Sandboxes (Critical distinction):**
   - **Git worktrees** isolate file modifications so an agent or parallel session can edit and run tests without dirtying the user's active working tree, and provide a clean checkpoint substrate for **Tool Execution Rollbacks** when a tool action or test validation fails.
   - **Worktrees are NOT security sandboxes.** Process, path, environment, and command restrictions must be enforced by `Guildhall.AgentHost` (validating canonical paths against the project root, enforcing strict read-only file access defaults, scrubbing secret environment variables, enforcing command timeouts/output caps, and mediating CLI harness permission flags).
3. **Review before merge:** Proposed file changes surface in Guildhall's **Git changes / Diff** pane. The user can inspect diffs, trigger rollbacks of failed tool edits, request line-specific revisions from the Council Member, or approve applying/merging the worktree changes.

---

## 11. Cloud APIs, CLI Harnesses, and vendor independence

### Unified Backing-Engine Adapter Matrix

| Ecosystem | Pillar 1: Direct Model API Adapter | Pillar 2: Local CLI Harness Adapter | Auth & Credential Storage |
|---|---|---|---|
| **Local / Open Weights** | Ollama (`/api/tags`, `/api/chat`, `/api/ps`) | Optional local CLI tools | None required (loopback HTTP) |
| **Google Gemini** | Gemini REST API (`generativelanguage.googleapis.com`) | **Gemini CLI** (`gemini --acp` or `gemini -p --output-format stream-json`) | API key in Windows DPAPI **or** Gemini CLI Google OAuth login |
| **Anthropic Claude** | Anthropic Messages API (`/v1/messages`) | **Claude Code CLI** (`claude -p --output-format stream-json` / ACP) | API key in Windows DPAPI **or** Claude Code subscription login |
| **OpenAI / ChatGPT** | OpenAI Responses / Chat Completions API | **OpenAI Codex CLI** (`codex exec --json` / `codex app-server`) | API key in Windows DPAPI **or** Codex CLI ChatGPT Plus/Pro login |
| **Cursor** | N/A (Cursor has no standalone public LLM API) | **Cursor Agent CLI** (`agent -p --output-format stream-json`) | Cursor CLI subscription login |

### Rules preserving vendor independence

- **No vendor owns the transcript:** Switching a conversation from `Claude Code CLI` to `Gemini 2.5 Pro API` to `Local Qwen3 (Ollama)` works seamlessly because Guildhall stores the canonical messages and memory notes in SQL Server and reconstructs the prompt context on every turn.
- **Honest capability & usage reporting:** If a provider or CLI reports exact token counts and billing rates, display them; if a CLI subscription or local model does not report dollar cost, show measured duration/tokens (tracked against **Cross-Turn Budgets**) and label cost as `Subscription / Not billed per token` or `Local inference` rather than fabricating numbers.

---

## 12. Deployment topology, networking, and multi-tier resilience

### Stage A: Desktop-only (Phases 0–4)

```text
Windows desktop
  [Tier 1] Tauri UI <-> [Tier 2] ASP.NET Core AgentHost (127.0.0.1:43120)
                                    |-- [In-Memory] .NET ConcurrentDictionary (50–100 MB Markdown preamble cache + FileSystemWatcher)
                                    |-- [Tier 3] SQL Server (localhost / GuildhallDev — active conversation state)
                                    |-- [Tier 4] Ollama (127.0.0.1:11434 — local inference & VRAM residency)
                                    |-- [Tier 5] Active MCP Tool Servers (stdio / Streamable HTTP) & Local CLI Harnesses
                                    |-- Optional Cloud APIs (HTTPS outbound only)
```

No inbound public ports, no Droplet dependency, no router/firewall changes.

### Stage B: Private home/remote network (Phase 5)

```text
Desktop / Home Host                          DigitalOcean Droplet
SQL Server + Ollama  <--- WireGuard VPN ---> Authenticated MCP / Relay Host
```

- Home SQL Server (`1433`) and Ollama (`11434`) are **never** exposed to the public internet.
- Outbound calls to Cloud APIs or local CLI harnesses in Stage A/B do **not** require the Droplet.

### Stage C: Independent home server (Phase 6–8)

- Decouple `Guildhall.AgentHost`, Ollama, or scheduled task execution onto an always-on home server when 24/7 background tasks or larger GPUs justify it, without changing the desktop UI contracts.

### Multi-Layer Health Checks, Auto-Retry & Graceful Degradation

Local background services (such as Ollama daemons or MCP subprocesses) can crash, hit OOM limits, or fail to restart after updates. Guildhall implements health checks, bounded auto-retry logic, and graceful degradation across all **five architectural tiers**:

1. **Tier 1 — Tauri UI Process:** Monitors host reachability and stream/poll health; preserves unsent composer drafts and client request IDs on transient disconnects; degrades gracefully to clear status/reconnect banners without freezing the window or auto-resubmitting mutations.
2. **Tier 2 — ASP.NET Core Agent Host:** The central orchestrator managing agent lifecycles, reasoning loops, and tool bindings. Exposes authenticated liveness (`/health`) independently of downstream outages, gates work admission on readiness (`/ready`), reconciles abandoned runs on startup, and isolates background worker faults.
3. **Tier 3 — SQL Server Database:** Verified via `/ready` schema and connectivity probes; applies bounded retry/backoff for transient read faults; aborts active generation cleanly if SQL becomes unreachable mid-run and blocks new admissions until reconciliation succeeds.
4. **Tier 4 — Ollama Inference Service:** Probes `/api/tags` and `/api/ps` (for **VRAM Residency Tracking**); detects daemon crashes, OOM evictions, stalled streams, or post-update restart failures; applies bounded connection/first-token/idle timeouts and surfaces actionable recovery states without crashing the Agent Host.
5. **Tier 5 — Active MCP (Model Context Protocol) Tool Servers:** Tracks per-server health (`Connected`, `Degraded`, `Failed`, `Disconnected`) across local `stdio` child processes and remote `Streamable HTTP` endpoints; implements bounded auto-retry and subprocess restart backoff; degrades gracefully by disabling only the affected tool server while keeping chat and healthy tools operational.

---

## 13. Security, backend enforcement, and process isolation

1. **Hardcoded Backend Guardrails (Never Rely Solely on Prompts):** Prompt instructions alone are never treated as a security boundary. Critical operational boundaries are hardcoded and enforced in the ASP.NET Core backend layer (`Guildhall.AgentHost`):
   - **Strict read-only file access defaults:** Tools and harnesses default to read-only inspection confined to explicitly authorized workspace roots; arbitrary host filesystem traversal is blocked in code.
   - **Prevention of credential or sensitive data exfiltration:** Outbound requests, tool arguments, logs, and exports are scrubbed and validated by the host to prevent exfiltration of DPAPI secrets, session tokens, connection strings, `.env` files, or private keys.
   - **Mandatory human-in-the-loop approval gates:** Any destructive, mutating, or external-facing system operation (file writes outside safe checkpoints, shell command execution, database mutations, Git pushes, external API writes) pauses execution at a host-enforced approval gate requiring explicit user confirmation in the UI.
2. **Process Isolation (Tauri UI vs. ASP.NET Core Host):** Maintain strict OS process isolation between the Tauri WebView UI frontend and the ASP.NET Core agent host backend. The frontend has no direct shell, filesystem-write, database, or subprocess capability, ensuring that prompt injections in model output or untrusted data cannot escalate from the UI or reasoning loop into local OS compromise.
3. **Loopback & Origin Boundary:** Host binds to `127.0.0.1`, requires a cryptographic per-session bearer token (32–128 chars), validates `Origin` and `Host`, and never puts tokens in URLs, browser storage, or logs.
4. **Untrusted Content Isolation:** Model outputs, repository files, attachments, web/search results, database rows, and memory notes are treated strictly as **data**, never as authority to alter permissions, change instructions, or approve tools.
5. **Database Least Privilege:** Runtime login (`guildhall_runtime`) has only explicit table-level `SELECT`/`INSERT`/`UPDATE` (and narrow parent-row `DELETE` on `Conversations` and `AgentDefinitions`). Schema changes require the separate Windows migration identity and explicit owner approval.
6. **Subprocess & CLI Harness Containment:** When spawning MCP servers or CLI harnesses (`gemini`, `claude`, `codex`, `agent`), scrub inherited environment variables, enforce execution timeouts and output byte caps, restrict working directories to approved project roots/worktrees, and intercept/gate destructive actions according to the active Guildhall permission mode.
7. **Safe Rendering & Redaction:** Render Markdown/GFM without raw HTML execution; open external links safely in the OS default browser after protocol validation (`https:`/`http:`); redact secrets from all logs and exports.

---

## 14. Repository and documentation layout

The repository follows a flat, reviewable monorepo structure without speculative microservice projects:

```text
guildhall/
  AGENTS.md                                  # Engineering principles & doc routing rules
  CLAUDE.md                                  # Claude Code / multi-harness instruction entry point
  README.md                                  # Setup, launch, migration, and verification guide
  Guildhall.slnx                             # .NET 10 solution
  .agents/
    roster.md                                # Development agent roster, model tiers & worktree rules
  docs/
    coding-standards.md                      # Authoritative .NET 10, EF Core 10, Tauri 2 & React 19 standards
    masterplan/
      personal_ai_ecosystem_master_plan.md   # This master architecture & roadmap document
      product-roadmap-amendments.md          # Owner-approved cross-phase amendments & boundaries
      phase 0/                               # Completed Phase 0 plans, reviews, and verification
        architecture-review.md
        phase-0.md
        setup-approval.md
        verification.md
      phase 1/                               # Active Phase 1 plans, contracts, QC, and SQL records
        phase-1.md
        phase-1-contracts.md
        agent-quality-and-performance.md
        competitive-ux-review.md
        council-profiles-and-memory.md
        development-qc-plan.md
        frontend-polish-plan.md
        workspace-ui.md
        council-profile-migration-approval.md
        council-member-archive-migration-approval.md
        council-member-deletion-permission-approval.md
    decisions/                               # Numbered ADR decision records (0001..0004+)
    design/                                  # Visual references (keep asset sizes bounded)
  apps/
    desktop/                                 # Tauri 2 + React + TypeScript desktop app
  services/
    agent-host/                              # Single ASP.NET Core (.NET 10) host executable
      Api/
      Chat/
      Configuration/
      Persistence/
      Providers/
  tests/
    Guildhall.AgentHost.Tests/               # Deterministic & isolated SQL integration tests
  scripts/                                   # PowerShell 7 & SQL scripts organized by domain:
    database/                                #   Provisioning, migrations, and SQL upgrade runners
    startup/                                 #   Prerequisite checks, dev launcher, status, and stop scripts
    sql/                                     #   Reviewed SQL scripts
    misc/                                    #   Shared helpers, verification, and smoke tests
```

**Documentation & Token-Spend Rule:** Coding agents must consult `AGENTS.md` (or `CLAUDE.md`) and only the relevant `docs/masterplan/phase N/` documents before exploring source code, and place new phase documentation inside its corresponding `docs/masterplan/phase N/` subfolder.

### 14.1 Repository Agent Management & Multi-Instance Worktree Orchestration

This same orchestration pattern governs **both** how development agents build Guildhall today and how Guildhall's own Council Members and scheduled automations will manage parallel project worktrees in Phases 4, 6, and 7:

- **Coordinator-Owned `main` & Isolated Feature Worktrees:** Only the Coordinator merges into `main` or applies approved schema migrations to `GuildhallDev`. Parallel coding agents work in isolated Git worktrees on scoped branches, sharing the Windows DPAPI runtime credential (`%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml`) read-only and batching git pushes to minimize CI spend.
- **Side-by-Side Multi-Instance Hosting (`-AutoPort`, `-HostPort`, `-FrontendPort`, `-Replace`):**
  - `scripts/startup/Start-Dev.ps1` supports `-AutoPort` (automatically selecting free loopback ports when `43120`/`1420` are in use), explicit `-HostPort` / `-FrontendPort`, and `-Replace` (cleanly stopping any conflicting Guildhall session on the target ports before starting).
  - Active sessions register in `%LOCALAPPDATA%\Guildhall\dev\sessions\<sessionId>.json`, allowing `scripts/startup/Get-DevStatus.ps1` to inspect all running worktree instances and `scripts/startup/Stop-Dev.ps1` (`-All`, `-SessionId`, `-Checkout`, `-Port`) to stop them cleanly.
- **Version & Branch Window Tagging (`GUILDHALL_WINDOW_TITLE` / `GUILDHALL_INSTANCE_TAG`):**
  - Every launched window automatically computes its version tag (from `tauri.conf.json`, e.g., `v0.1.0`), Git branch / worktree name, optional `-WindowTag`, and active port pair, formatting the native OS window title (`Guildhall v0.1.0 [branch | :43121]`) and rendering an in-app header badge (`v0.1.0 · branch :43121`).
  - Both human reviewers and UI/Browser automation harnesses can immediately identify and target which branch, version, and port each running window is testing.
- **Standard End-of-Implementation Handoff:** Every agent implementation concludes with:
  1. Copy-pasteable command(s) to run Guildhall in the active worktree with GPU mode and maximum model retention (`Start-Dev.ps1 -UseGpu -KeepModelWarmSeconds 900` by default, with `-Replace` and `-AutoPort` variants).
  2. Migrations run (if applicable).
  3. Manual testing notes / functional QC checklist.

---

## 15. Phased delivery roadmap

### Phase 0: Foundation and proof of connectivity — **COMPLETE**

- **Built & Verified:** Repository baseline, Tauri 2 window, authenticated loopback `.NET 10` host (`/health`, `/ready`, `/models`), dedicated `GuildhallDev` SQL Server database with Windows migration identity and DPAPI-protected `guildhall_runtime` login, local Ollama discovery, and 46 passing automated checks plus native smoke verification.
- **Record:** See [Phase 0 verification](phase%200/verification.md).

### Phase 1: Local AI Workspace & Council MVP — **IN PROGRESS**

- **Scope:** Persistent Council Member profiles (`AgentDefinitions`) with custom roles, portraits, accents, and modular Markdown instructions (separating user-tunable parameters from global system guidelines, configured as static system preambles); version-controlled Markdown preambles memoized in a `.NET ConcurrentDictionary` (`50–100 MB` hard cap) with `FileSystemWatcher` hot-reloading that updates only subsequent turns; member-grouped sessions (`Conversations`) persisted in SQL Server as the single source of truth (without full-history RAM caching in MVP); progressive committed-snapshot streaming (`ChatRuns`/`ChatMessages`); Stop and startup interrupted-run reconciliation; bounded per-session queue (up to 8 turns) with `Send Now`, `Edit`, and `Remove`; local file/image attachments (`ChatAttachments`); reversible member/session archival and confirmed permanent deletion; workspace-wide behavior policy (`GuildhallBehaviorSettings`); **VRAM Residency Tracking** (`/api/ps`) and **Cross-Turn Budgets** (cumulative token and reasoning-effort tracking) in the Context Inspector; and explicit SQL memory notes (`About you` and Member memory, B05/B06). *(C15/C16/Q06 native JSON export, isolated SQL backup/restore verification, and formal live model speed/quality benchmarks are deferred to later phases by owner direction.)*
- **Done when:** All active Phase 1 functional gates (chat, council profiles, preamble memoization/hot-reload B04, SQL memory notes B05, context assembly/inspector B06, and automated unit/SQL checks) pass.
- **Record & Tasks:** See [Phase 1 plan](phase%201/phase-1.md), [Phase 1 contracts](phase%201/phase-1-contracts.md), and [Development QC plan](phase%201/development-qc-plan.md).

### Phase 2: Unified Backing-Engine Abstraction (Cloud Model APIs + CLI Agent Harnesses)

- **Build:**
  - Normalized backing-engine contract supporting both **Pillar 1 Direct Cloud APIs** (Google Gemini API, Anthropic Claude API, OpenAI API) and **Pillar 2 Local CLI Agent Harnesses** (Gemini CLI via `--acp`/`stream-json`, Claude Code CLI, OpenAI Codex CLI, Cursor Agent CLI).
  - Connections UI & OS credential storage (Windows DPAPI / Credential Manager) for API keys and CLI health/auth detection (`Not connected`, `Needs sign-in`, `Connected`, `Unavailable`).
  - Per-member and per-session engine selector allowing any Council Member to switch between Local Ollama, Cloud APIs, and CLI Harnesses while keeping SQL Server as the transcript/memory owner.
  - Honest per-run and **Cross-Turn Budget** tracking (cumulative token usage, reasoning-effort limits, timing, and cost/subscription visibility).
- **Done when:** A Council Member can conduct a multi-turn session using local Ollama, switch to a Cloud API or wrapped CLI Harness (starting with Gemini API / Gemini CLI or Claude / OpenAI), and preserve full SQL history, memory injection, and offline fallback when cloud/CLI engines are disabled.

### Phase 3: Local MCP & External Service Integrations (Pillar 3)

- **Build:** Official MCP C# SDK client in `Guildhall.AgentHost`, local `stdio` MCP server registry and lifecycle management, **Tier 5 MCP health checks with auto-retry and graceful degradation**, tool discovery and schema accounting in the Context Inspector, hardcoded backend permission policy gate (strict read-only defaults and anti-exfiltration checks), Run Inspector tool trace UI, first read-only Filesystem/Git MCP tools, and scoped service connectors (GitHub, Google Drive, Gmail).
- **Done when:** A Council Member (backed by either a Model API or CLI Harness) can invoke an approved read-only tool or service integration within scope, and the user can inspect every request, approval, and result.

### Phase 4: Controlled Coding Workspace, Worktrees, Rollbacks, and Enforced Access Modes

- **Build:** Local project/repository picker (working offline without GitHub), isolated Git worktree management, **Tool Execution Rollbacks** (transactional/compensating rollback mechanisms for failed or rejected tool edits and commands), hardcoded ASP.NET Core command sandboxing and mandatory human-in-the-loop approval gates, native diff preview & review loop in Session Tools, Project-scoped memory notes, and host-enforced access modes (`Always ask`, `Ask for potentially unsafe actions`, `Unrestricted / autopilot`) governing both native MCP tools and wrapped CLI harnesses.
- **Done when:** A Council Member fixes a failing test in an isolated worktree, recovers or rolls back cleanly from a simulated tool failure, runs validation under the selected access policy, presents a reviewable diff inside Guildhall, and merges only after human approval.

### Phase 5: Private Infrastructure and SQL Server MCP

- **Build:** Read-only SQL Server MCP for authorized databases, private WireGuard (or optional Tailscale) tunnel to the DigitalOcean Droplet, authenticated remote Streamable HTTP MCP gateway, and automated SQL backup/restore verification.
- **Done when:** Authorized local or remote clients can query approved read-only SQL views/schemas without exposing SQL Server (`1433`) or Ollama (`11434`) publicly.

### Phase 6: Durable Background Tasks and Local Scheduled Automations

- **Build:** Independently running background host mode, SQL-backed durable task queue, **5-tier health monitoring and auto-recovery**, local cron/schedule manager (`Scheduled tasks` tab) executing Council Member runs under their configured access policy and cross-turn budgets, missed-run/sleep handling, and reconnectable progress streams.
- **Done when:** Scheduled automations and long-running tasks execute locally with full audit history, pause/resume controls, and clean recovery across desktop restarts.

### Phase 7: Cross-Member Context Handoff, Delegation Dependency Graphs, and Multi-Agent Coordination

- **Build:**
  - **Conversation Reassignment ("Assign to guild member"):** Interactive handoff/edit mode allowing the user to transfer a conversation or curated context packet (full transcript, reviewed summary, or pinned items) from one Council Member to another across different backing engines.
  - **Bounded Multi-Agent Delegation & Delegation Dependency Graphs:** Child-run protocol, supervisor-to-specialist task delegation, durable **Delegation Dependency Graphs** tracking multi-agent execution dependencies, per-agent tool and **Cross-Turn Budgets**, run graph visualization, and cascading cancellation/rollback propagation.
- **Done when:** You can reassign a session across Council Members or let a steward member delegate a bounded task graph to specialists (e.g., Architect on Claude/Gemini -> Builder on Codex/Gemini CLI -> Reviewer on Local Ollama) with full dependency graph tracking, provenance, and human approval boundaries.

### Phase 8: Dedicated Hardware and Advanced Capabilities

- **Consider only with demonstrated need:** Dedicated 24GB+ VRAM home inference server (overcoming the **Memory Bandwidth Wall** for heavy quantizations and long contexts), multi-model **VRAM Residency Tracking** across hosts, semantic/vector retrieval over large archives, local voice/speech I/O, and multi-device sync.
- **Done when:** Inference and execution hosts can be relocated via configuration without altering desktop or Council contracts.

---

## 16. Current active development focus

Phase 0 is complete. Active work is strictly bounded to finishing **Phase 1** ([docs/masterplan/phase 1/phase-1.md](phase%201/phase-1.md)) and the owner's current Frontend & DevOps polish items ([docs/2026-11-16.md](../2026-11-16.md)):

1. Complete Phase 1 frontend ergonomics (preventing conversation list dimming while typing, balanced transcript padding, collapsible member session accordions, removing redundant filter counts, safe external link opening in the OS browser, copy buttons on terminal command blocks, and adding the disabled `Assign to guild member…` placeholder in the conversation menu).
2. Reorganize repository scripts into `scripts/{database,startup,sql,misc}` with all references updated and verified, plus a spend-conscious CI test workflow.
3. Implement reviewed SQL Memory Notes (`About you` and Member scopes, B05/B06), bounded RAM preamble memoization (`50–100 MB` `.NET ConcurrentDictionary` + `FileSystemWatcher` hot-reload, B04), and the Context Inspector (including Cross-Turn Budget and VRAM Residency visibility) before starting Phase 2 (Cloud APIs + CLI Agent Harnesses). *(C16/Q06 native JSON export, SQL backup/restore verification, and live model benchmarks are deferred to later phases.)*

---

## 17. Decisions that remain open until their phase gate

- **First Phase 2 integration target:** Which cloud API / CLI harness pair to wire up first in Phase 2 (e.g., **Google Gemini API + Gemini CLI** vs. **Claude API + Claude Code** vs. **OpenAI API + Codex CLI**)?
- **ACP vs. JSONL per CLI harness:** Which installed CLIs support full bidirectional ACP (`--acp` JSON-RPC 2.0 tool approval callbacks) on Windows vs. requiring one-shot/resumable `--output-format stream-json` with MCP callback bridges? Benchmark each CLI in a bounded Phase 2 spike.
- **Best local model on Ryzen 7 9800X3D (CPU) vs. 8 GB RTX 3070 (GPU):** Deferred to later hardware/benchmarking phases (Phase 8 / C16-Q06 deferral) across short turns vs. sustained/long-context workloads.
- **Host packaging:** Keep launcher-owned processes for development; evaluate Tauri sidecar vs. optional Windows background service at Phase 6.

---

## 18. Known risks and mitigations

| Risk | Mitigation |
|---|---|
| Prompt injection or hallucinated tool authority | Hardcoded ASP.NET Core backend guardrails (strict read-only defaults, anti-exfiltration filters, mandatory human-in-the-loop approval gates) and Tauri/Host OS process isolation; never rely solely on prompts |
| External CLI agents bypass Guildhall's approval policy or trap history in CLI state | Use ACP / `stream-json`; inject SQL-owned context/memory on each turn; persist all output/diffs to SQL Server; mediate permission flags in `.NET` host |
| Local daemon crashes, OOM evictions, or post-update restart failures | 5-tier health checks, bounded auto-retry, and graceful degradation across Tauri UI, ASP.NET Core Host, SQL Server, Ollama, and active MCP tool servers |
| Per-turn SQL/disk bottlenecks for static instructions vs. cache-sync complexity | Memoize compiled Markdown preambles in `.NET ConcurrentDictionary` (`50–100 MB` hard cap) with `FileSystemWatcher` hot-reloading; keep active conversation histories strictly in SQL Server for MVP |
| Cold-swap latency stalls & Memory Bandwidth Wall on local hardware | Track live model residency in VRAM (`/api/ps`), support bounded warm retention (`0–900s`), use high-cache CPU (e.g., Ryzen 7 9800X3D + DDR5) for short turns, and offload long-context/sustained loads to GPU VRAM |
| Failed mid-run tool mutations leaving dirty workspace state | Transactional or compensating **Tool Execution Rollbacks** using isolated Git worktree checkpoints |
| Runaway token consumption or cyclic multi-agent delegation | Enforce **Cross-Turn Budgets** (cumulative tokens & reasoning effort) and track **Delegation Dependency Graphs** (DAGs) in the host |
| Consumer subscription vs. API billing confusion | Separate Pillar 1 (API keys) and Pillar 2 (CLI subscription logins) clearly in Connections UI and usage reports |
| Git worktrees mistaken for security sandboxes | Enforce path validation, environment scrubbing, and command allowlists in `Guildhall.AgentHost` |
| UI coupled to a single model or vendor | Normalized Council Member & Run contracts in `Guildhall.AgentHost` |
| Secrets exposed to models, child CLIs, or logs | Windows DPAPI storage, scrubbed subprocess environments, and output/export redaction |
| High token spend during agentic development | Structured `docs/masterplan/phase N/` hierarchy; agents read scoped phase docs before exploring code |

---

## 19. Authoritative reference documentation

- Tauri 2 capabilities & security: https://v2.tauri.app/security/capabilities/
- Agent Client Protocol (ACP) specification: https://agentclientprotocol.com/
- Gemini CLI headless & stream-json mode: https://geminicli.com/docs/cli/headless/
- Official MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP Streamable HTTP transport: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports
- Ollama API documentation: https://docs.ollama.com/api
- .NET 10 support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
