# Phase 3: Local MCP & External Service Integrations (Pillar 3)

**Status:** Complete (`P3-00` through `P3-05` + live QC polish)  
**Started:** November 16, 2026  
**Completed:** November 17, 2026  
**Baseline:** Phase 2 (`6dde037`) + `P3-00` (`App.tsx` modularization, `a08a029`)  
**Migration:** `Phase3McpToolsAndTraces` (additive columns on `AgentDefinitions` and `Conversations`; new `ChatRunEvents` and `McpServerRegistrations` tables; DML-only `guildhall_runtime` grants)

---

## 1. Scope & Non-Negotiables

1. **Zero New NuGet Packages:** Built-in `.NET 10` JSON-RPC 2.0 MCP client (`System.Text.Json`, `ICliProcessRunner`, `IHttpClientFactory`) with `Guildhall.AgentHost.csproj` and `packages.lock.json` kept locked.
2. **Hardcoded Backend Guardrails (`services/agent-host/Security/`):**
   - `WorkspacePathGuard`: Canonical path resolution (`Path.GetFullPath`), directory traversal rejection, symlink/reparse point escape rejection, and hard-blocked credential/secret file patterns (`.env*`, `*.dpapi`, `runtime.credential.xml`, `secrets.json`, private keys).
   - `ExfiltrationGuard`: Automatic redaction of DPAPI blobs, session tokens, SQL connection strings, API keys, and private key headers in tool arguments/results/logs, plus outbound exfiltration blocking on Tier 3 calls.
   - `ToolPermissionGate`: Enforces per-member `EnabledToolIds` and permission policy (`ask_unsafe` vs `always_ask`) across `Tier0_None`, `Tier1_ReadOnlyLocal`, `Tier2_WorkspaceWrite` (rejected in Phase 3 as `tier2_planned_phase4`), and `Tier3_ExternalOrSensitive` (mandatory human-in-the-loop approval).
3. **Tier 5 MCP Health & Graceful Degradation (`services/agent-host/Mcp/`):**
   - Per-server health state machine (`Connected`, `Degraded`, `Failed`, `Disconnected`) with bounded exponential backoff (`2s`, `5s`, `15s`) and fault isolation so failing servers never crash the host or block healthy tools/chat.
4. **First-Party Pillar 3 MCP Servers & Connectors:**
   - **Local Read-Only Filesystem (`filesystem-mcp`, Tier 1):** `fs_list_directory`, `fs_read_file`, `fs_search_files`.
   - **Local Read-Only Git (`git-mcp`, Tier 1):** `git_status`, `git_log`, `git_diff`, `git_show`.
   - **GitHub (`github-mcp`, Tier 1 Read / Tier 3 Write):** DPAPI-stored PAT; `github_get_repository`, `github_list_issues`, `github_list_pull_requests`, `github_get_pull_request_files`, plus approval-gated `github_create_issue` and `github_create_issue_comment`.
   - **Google Drive (`google-drive-mcp`, Tier 1 Read):** DPAPI-stored OAuth token; `gdrive_search_files`, `gdrive_read_text_file`.
   - **Gmail (`gmail-mcp`, Tier 1 Read / Tier 3 Draft):** DPAPI-stored OAuth token; `gmail_search_messages`, `gmail_get_message`, plus approval-gated `gmail_create_draft`.
   - **Custom Local `stdio` & `Streamable HTTP` MCP Registry:** User-registered external MCP servers with scrubbed child environments and DPAPI-protected headers.
5. **Pillar 1 Tool Loop & Pillar 2 Per-CLI Adapter Split:**
   - Bounded multi-step tool loop (max 6 rounds per turn) in `ChatRunner` across Ollama, OpenAI, Gemini, and Anthropic, with `PendingApproval` pause/resume via `POST /runs/{runId}/events/{eventId}/decision`.
   - Split `CliHarnessEngine` into dedicated per-CLI adapters (`AgyCliEngine`, `CodexCliEngine`, `GeminiCliEngine`, `ClaudeCliEngine`, `CursorCliEngine`), recording tool events into `ChatRunEvents` and mediating read-only workspace inspection without `--yolo`.

---

## 2. Work Breakdown

| ID | Task | Status |
|---|---|---|
| `P3-00` | Behavior-preserving `App.tsx` modularization (`CouncilSidebar`, `ChatTranscript`, `ChatComposer`, `WorkspaceSettingsPanel`, `ConfirmationDialogs`) | **Done** (`a08a029`) |
| `P3-01` | Phase 3 docs, `Phase3McpToolsAndTraces` SQL migration + approval record, and `Security/` guardrails (`WorkspacePathGuard`, `ExfiltrationGuard`, `ToolPermissionGate`) | **Done** (`cd7031a`) |
| `P3-02` | MCP client runtime (`McpJsonRpcClient`), `McpHealthSupervisor` (Tier 5), `McpRegistry`, and built-in `filesystem-mcp` + `git-mcp` | **Done** (`f03b657`) |
| `P3-03` | Scoped external connectors (`github-mcp`, `google-drive-mcp`, `gmail-mcp`) + `ConnectionCatalogService` / `/mcp` endpoints | **Done** (`f03b657`) |
| `P3-04` | Pillar 1 native tool-calling + Pillar 2 dedicated per-CLI adapters + `ChatRunner` bounded tool loop & `PendingApproval` gate + `ContextBuilder` tool schema accounting | **Done** (`6f8d570`) |
| `P3-05` | Frontend UI: `RunTraceList.tsx` (inline traces & Approve/Deny), `ConnectionsCatalog.tsx` Pillar 3 cards, `ContextInspector.tsx` Tier 5 health & schema tokens, `CouncilMember.tsx` tool/policy selectors, and session workspace root picker | **Done** (`d485ae8`, QC polish `43df176`–`dc555ed`) |
