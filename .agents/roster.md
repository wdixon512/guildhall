# Guildhall Repository Agent Roster & Orchestration Guide

**Status:** Active agentic instruction specification  
**Updated:** November 16, 2026  
**Canonical Entry Points:** [AGENTS.md](../AGENTS.md), [CLAUDE.md](../CLAUDE.md), [docs/coding-standards.md](../docs/coding-standards.md), [docs/masterplan/personal_ai_ecosystem_master_plan.md](../docs/masterplan/personal_ai_ecosystem_master_plan.md).

---

## 1. Dual-Layer Scope of Agent Management

This repository manages two distinct layers of agents:

1. **Layer A — Repository Development Agents:** External coding agents and subagents (Antigravity, Claude Code, OpenAI Codex, Cursor, Gemini CLI) that plan, implement, test, and review Guildhall's source code across Git worktrees.
2. **Layer B — In-App Guildhall Council Members (`AgentDefinitions`):** Persistent specialists inside the Guildhall application whose base instructions are authored as version-controlled Markdown preambles on disk (`services/agent-host/Preambles/`), memoized at runtime in a bounded `.NET ConcurrentDictionary` (`50–100 MB` hard cap) with `FileSystemWatcher` hot-reloading, and snapshotted immutably into SQL Server (`ChatRuns`) per turn.

---

## 2. Layer A — Development Agent Roster & Model Tiering

| Role | Responsibility & Allowed Scope | Recommended Reasoning Tier |
|---|---|---|
| **1. Coordinator / Architect** | Owns `main` merges, `docs/masterplan/` updates, cross-worktree task decomposition, ADR decisions (`docs/decisions/`), and approval gates for packages or destructive/permission SQL migrations. | **High / Pro** (e.g., Gemini 4 Argon High, GPT-6.1 Sol, Claude Opus) |
| **2. Backend & SQL Specialist** | Owns `services/agent-host/`, `tests/Guildhall.AgentHost.Tests/`, and `scripts/database/`. Implements DI services (`IOptionsMonitor<T>`, `TimeProvider`, `Lazy<T>`), EF Core 10 queries (`ILazyLoader`, `.AsNoTracking()`, projections), concurrency/recovery, and isolated `GuildhallTest_*` SQL tests. | **Medium–High** (High for concurrency, run recovery, and SQL permission gates; Medium for CRUD/DTOs) |
| **3. Frontend & Tauri Specialist** | Owns `apps/desktop/` and `scripts/startup/`. Implements React 19 + strict TypeScript UI, `React.lazy` code-splitting, CSS layout, Vitest unit tests, and bounded Tauri 2 Rust commands (`src-tauri/`). | **Fast / Medium** (e.g., GPT-6 Luna, Gemini Flash/Medium) |
| **4. Security & Guardrail Reviewer** | Read-only audit of hardcoded ASP.NET Core backend guardrails, loopback/token auth, Tauri CSP/capabilities, subprocess environment scrubbing, anti-exfiltration filters, and least-privilege `guildhall_runtime` SQL grants. | **High** (Read-only subagent or review pass) |
| **5. UI & Interaction Reviewer** | Defined in [`.cursor/agents/guildhall-ui-reviewer.md`](../.cursor/agents/guildhall-ui-reviewer.md). Mandatory read-only visual hierarchy, keyboard/focus, responsive, and placeholder honesty check before presenting any UI change. | **Inherited / Medium** (Read-only subagent) |

---

## 3. Tiered Context Funnel (Token-Spend Discipline)

To keep token spend low and prevent context pollution, every coding agent must follow this strict loading order:

1. **Step 1 — Root Router (`AGENTS.md`):** Read [AGENTS.md](../AGENTS.md) (and [docs/coding-standards.md](../docs/coding-standards.md) when writing C#, EF Core, TypeScript, or Tauri code).
2. **Step 2 — Scoped Master Plan & Active Phase Folder:** Read only the relevant section of [personal_ai_ecosystem_master_plan.md](../docs/masterplan/personal_ai_ecosystem_master_plan.md) and the specific document in `docs/masterplan/phase N/` for the current task (e.g., `docs/masterplan/phase 6/phase-6.md`).
3. **Step 3 — Task Declaration Before Editing:** State the allowed files, acceptance criteria, and verification commands before modifying source files. Use broad codebase search only when targeted documentation does not answer the question.

---

## 4. Parallel Worktrees, Multi-Instance Hosting & Window Tagging

1. **Coordinator-Owned `main`:**
   - Parallel coding agents work in isolated Git worktrees on scoped branches. Only the Coordinator merges into `main`.
2. **Side-by-Side Multi-Instance Execution:**
   - Multiple Guildhall instances can run simultaneously across worktrees using `scripts/startup/Start-Dev.ps1 -AutoPort` (or explicit `-HostPort` / `-FrontendPort`), or replace an existing session using `-Replace`.
   - Every running session registers in `%LOCALAPPDATA%\Guildhall\dev\sessions\<sessionId>.json`. Inspect all active worktree sessions with `scripts/startup/Get-DevStatus.ps1` and stop sessions cleanly with `scripts/startup/Stop-Dev.ps1` (`-All`, `-SessionId`, `-Checkout`, `-Port`).
3. **Automatic Version & Branch Window Naming:**
   - `Start-Dev.ps1` automatically titles every native window and in-app header badge with its **version tag, branch/worktree name (plus optional `-WindowTag`), and port** (`Guildhall v0.1.0 [branch | :43121/:1421]`), allowing both humans and Playwright/WebView2 automation harnesses to identify which branch and version each window is testing.
4. **Shared DPAPI Credential & Database Policy:**
   - Interactive worktree instances share the DPAPI-encrypted runtime credential (`%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml`) and connect to `GuildhallDev` by default (auto-applying additive migrations via `Start-Dev.ps1`). Automated SQL tests always use isolated `GuildhallTest_<guid>` scratch databases.
5. **Spend-Conscious CI:**
   - Run targeted unit/build checks locally during iteration and batch `git push` updates to minimize GitHub Actions CI pipeline spend.

---

## 5. Layer B — On-Disk Layout for In-App Council Preambles (`services/agent-host/Preambles/`)

When implementing and maintaining Guildhall's disk-backed Council Member preambles:

```text
services/agent-host/
  Preambles/
    global-guidelines.md          # System-wide operational boundaries (relies strictly on tool outputs,
                                  # respects workspace scope, never fabricates states or benchmark metrics)
    members/
      artisan.md                  # YAML frontmatter (tunable persona/role, reasoning effort, default model)
      maren.md                    # + Markdown body (primary task objectives and role instructions)
      researcher.md
      scholar.md
```

- **Static System Preamble Assembly:** `Guildhall.AgentHost` combines `global-guidelines.md` + the active member's `members/<slug>.md` into a single static `system` message per API call.
- **RAM Memoization & Hot-Reload Safety:** Compiled preambles are cached in a `.NET ConcurrentDictionary` (hard-capped at **50–100 MB**) and refreshed via `FileSystemWatcher` when Markdown files change on disk. Hot-reloading affects only subsequent prompts/runs and never wipes or mutates active conversations in SQL Server.
