---
name: work-work
description: >-
  Runs a single-session, 3-phase Feature and Bug orchestration workflow for
  Guildhall (Intake & Scope Gate -> Index-First Anchored Plan Artifact ->
  Parallel Subagent Execution, Index Sync, and GitHub PR Review Loop into main).
  Use when the user invokes /work-work, says "work work", or asks to plan and
  orchestrate a Guildhall feature or bug fix end-to-end without blind codebase
  grepping.
---

# `/work-work` — Index-First Feature & Bug Orchestrator (`Guildhall`)

*"Something need doing? Work, work."*

Run **Intake -> Anchored Plan -> Delegated Execution** inside **one chat session** while keeping main-thread token usage minimal: consult `.agents/index/` before grepping, offload discovery, parallel implementation waves, noisy test suites, UI reviews, and GitHub/PR loops to fresh subagents with bounded file reads, and keep the codebase index synchronized iteratively on every change.

---

## Session & Phase Naming (Every Turn)

Keep the session organized and scannable from **Turn 1** through completion using one strict template:

```text
Phase <1|2|3>: <short description (<=5 words)>
```

Examples: `Phase 1: Add tool rollback filter`, `Phase 2: Fix SSE reconnect leak`, `Phase 3: Add tool rollback filter`.

- **Lock the short description on Turn 1** (<=5 words, plain wording) and reuse the exact same description across phases unless the user changes scope.
- **On every turn and every phase transition**, apply the current `Phase <1|2|3>: <short description>` label across all active surfaces:
  1. **Turn header:** Start every assistant reply with `**Phase <1|2|3>: <short description>**` on the first line.
  2. **Live status (`update_focus`):** Call `update_focus` with `Phase <1|2|3>: <short description>` at the start of Phase 1 and whenever transitioning to Phase 2 or Phase 3 (and whenever a multi-tool turn begins).
  3. **Swarm title (`swarm`):** Whenever using a swarm, set and update the swarm title with `swarm(action="title", title="Phase <1|2|3>: <short description>")` on creation and on every phase change.
  4. **Host session rename (if available):** If the current host/CLI exposes a session-rename tool or command, rename the session to `Phase <1|2|3>: <short description>` on Turn 1 and at every phase transition.

---

## Mode Selection (Feature vs. Bug)

Classify the request upfront (ask if ambiguous):

| Aspect | **Feature Mode** | **Bug Mode** |
| :--- | :--- | :--- |
| **Issue type label** | `enhancement` (**never** `bug`) | `bug` (**never** `enhancement`) |
| **Branch prefix** | `feature/<issue>-<slug>` off `origin/main` | `fix/<issue>-<slug>` or `feature/<issue>-<slug>` off `origin/main` |
| **Phase 1 extras** | May pitch optional complementary ideas (strictly gated) | **No** unrequested feature ideas; focus on repro & smallest fix |
| **Phase 2 focus** | Affected workflows, blast radius, phase authorization (`docs/masterplan/`), schema/API prerequisites | Entry-point -> root-cause trace, `git log -S`/`blame`, regression test |

> **Security & phase gates:**
> - **Security gate (Bug mode):** If a report describes a `LocalApiBoundary` bypass, `WorkspacePathGuard` escape, `SqlReadOnlyGuard` bypass, credential/DPAPI leak, or `ExfiltrationGuard` failure, **stop immediately** — do not post details to a public GitHub issue; ask the owner how to handle it first.
> - **Phase boundary gate:** Verify the requested scope is authorized in `AGENTS.md` and `docs/masterplan/`. Never install/upgrade/remove NuGet, npm, or Cargo packages, or run destructive/permission SQL migrations without explicit owner approval.

---

## Phase 1 — Intake, 3-Search Gate & Scope Lock (`STOP FOR APPROVAL`)

**Label immediately on Turn 1:** Set `Phase 1: <short description>` (turn header, `update_focus`, and `swarm(action="title")` / host session rename if active).

Do **not** edit code, create branches, or create GitHub issues in Phase 1.

1. **Pre-Work 3-Search Gate** (delegate to a quick `flash` subagent or run read-only checks):
   - `git fetch origin --prune`
   - Search existing issues (`gh issue list --state all --search "<keywords>"`), remote branches (`git branch -r`), and PRs (`gh pr list --state all --search "<keywords>"`) with 2–3 keyword sets.
   - Report whether a matching issue, live worktree branch, or open PR already exists before proposing new ones.
2. **Scope & Issue Draft:**
   - **Feature:** Who/why, proposed behavior, constraints (including phase authorization and package/migration boundaries), and *"Done when"* checklist.
   - **Bug:** What happened vs. expected, repro steps, affected surface (`Desktop / UI`, `Tauri / Rust`, `Agent Host`, `SQL / EF`, `Providers`, `MCP`, `Scheduling`, `Ops / Scripts`), redacted log clues if provided.
   - Show the proposed GitHub issue draft (title, body summary, labels, assignee `wdixon512` — **never** include DPAPI credentials, `GUILDHALL_SESSION_TOKEN`, SQL connection strings, API keys, or private prompts).
3. **Optional Complementary Ideas *(Feature mode only)*:**
   - If you spot high-value complementary improvements, list them in a separate **"Optional Complementary Ideas (Require Approval)"** block. **Never** fold them into the plan unless explicitly approved.
4. **Open Questions:**
   - For each ambiguity, state: **Question** / **Recommendation** (with short reason) / **Alternatives**.
5. **Hard Gate 1:** Stop and wait for user approval on scope, optional ideas, open questions, and issue/branch handling.

---

## Phase 2 — Index-First Discovery & Anchored Plan Artifact (`STOP FOR APPROVAL`)

**Update label on entry:** Switch to `Phase 2: <short description>` (turn header, `update_focus`, and `swarm(action="title")` / host session rename if active).

Once Phase 1 is approved, spawn **one read-only `flash` `research` subagent** to map the work without bloating the main thread. Instruct the subagent to follow this exact lookup order:

### 1. Locate Code via the Workflow Index (Before Grepping)
- Read `.agents/index/INDEX.md` (<=120 lines) and match the task's HTTP route, Tauri IPC command, EF entity, provider/MCP ID, PowerShell script, or symptom to the relevant workflow card(s).
- Read `.agents/index/workflows/<id>.md` (<=90 lines) and `.agents/index/areas/<area>.md` (<=100 lines) when crossing subsystems or touching shared locks, entities, or security guards.
- Use bounded `read_file(Path, StartLine, EndLine)` calls around each hop's `~L` line hints. **Do not** run broad repo `Grep` scans or read full large files (`App.tsx`, `ChatEndpoints.cs`, `ChatRunner.cs`, `GuildhallDbContext.cs`, `ConnectionCatalogService.cs`, `ScheduledTasksPanel.tsx`) unless the index does not cover the target.
- Read relevant contract docs linked from the card (`docs/coding-standards.md`, the relevant `docs/masterplan/phase N/` spec/verification record, and `.agents/roster.md`).

### 2. Check Blast Radius & Guildhall Invariants Before Editing
- Inspect `file_to_workflows` and `symbol_to_workflows` in `.agents/index/manifest.json` for **every** file and symbol you plan to touch.
- Read dependent workflow cards so cross-layer contracts stay intact:
  - **.NET 10 / EF Core 10:** `sealed` DI services (`IOptionsMonitor<T>`, `TimeProvider`, `IDbContextFactory<GuildhallDbContext>`, `Lazy<T>`), `ValidateScopes` + `ValidateOnBuild`, built-in `ILazyLoader` (no proxy packages), `.AsNoTracking()` + DTO `.Select()` projections + deferred `ChatAttachments` `varbinary(max)` loading, `guildhall_runtime` DDL-free permissions.
  - **Security & Isolation:** `LocalApiBoundary` (loopback + Origin + fixed-time bearer token), Tauri CSP/capabilities, `WorkspacePathGuard`, `ExfiltrationGuard`, `ToolPermissionGate` (`Tier3` always requires HITL approval), `SqlReadOnlyGuard` (never `GuildhallDev`), `EnvironmentScrubber`, DPAPI credentials.
  - **Concurrency & Recovery:** `ChatRunner.MutationGate`, OCC `Revision` tokens, `PreambleCacheService` 50–100 MB LRU cap, `DurableTaskSchedulerService` misfire/restart reconciliation, `FiveTierHealthSupervisor`.
- **Bug mode:** Trace entry point -> exact failing line, check `git log -S` / `git blame` for recent regressions, and check existing test coverage (`tests/Guildhall.AgentHost.Tests/` and `apps/desktop/src/*.test.ts*`).

### 3. Write the Plan Artifact
Create a user-facing Plan Artifact (`RequestFeedback: true`) containing:
1. **Code Anchors:** Every touchpoint written as ``path :: symbol (Lstart–Lend)`` with verified 1-based line ranges, shared state, and invariants.
2. **Blast Radius & Risks:** Impacted workflow cards from `manifest.json`, breakage/regression risks, and any SQL migration or approval prerequisites.
3. **Story & Concurrency Plan:**
   - Partition work into small stories with explicit **file ownership**.
   - Mark stories with **disjoint files** as **Parallel-Safe Waves** (using `Workspace: "share"` when concurrent edits/builds happen) and stories sharing files as **Sequential Waves** so line anchors do not drift mid-wave.
4. **Targeted Verification & Index Sync Commands:**
   - Exact commands from each card's `## Targeted Verification` section + applicable repo gates (`dotnet restore Guildhall.slnx --locked-mode`, `dotnet build Guildhall.slnx --no-restore`, `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --no-build --filter "Category!=SqlIntegration"`, `npm.cmd --prefix apps/desktop run typecheck`, `npm.cmd --prefix apps/desktop run test -- --run`, `npm.cmd --prefix apps/desktop run build`, `cargo test --locked --manifest-path apps/desktop/src-tauri/Cargo.toml`) + index sync commands (`python scripts/index_doc.py --dirty` / `--write` / `--release-ready`).
5. **Branch & GitHub Plan:** Issue `#N`, scoped branch off `origin/main`, PR targeting `main`.

**Hard Gate 2:** Present the Plan Artifact cleanly and stop until the user clicks **Proceed** or approves.

---

## Phase 3 — Orchestrated Execution, Index Sync & PR Review Loop

**Update label on entry:** Switch to `Phase 3: <short description>` (turn header, `update_focus`, and `swarm(action="title")` / host session rename if active), and track execution steps live with `update_steps`.

### Step 1 — GitHub Issue & Worktree Branch Setup (`flash` subagent)
Follow `.agents/agents/github-manager.md`:
- Create/reuse the confirmed issue via UTF-8 `--body-file` (outside the repo tree) with required labels and `--assignee wdixon512`.
- Work on the scoped feature/fix branch off `origin/main` in an isolated Git worktree (**never** commit or push straight to `main`).

### Step 2 — Delegated Implementation Waves & Mandatory UI Review
- Spawn `self` implementer subagents per wave, passing **only that story's slice** of the Plan Artifact (`path :: symbol (Lstart–Lend)`, invariants, guardrails, owned files) so each subagent starts with a clean turn-1 context and zero blind searching.
- Run compatible disjoint-file stories concurrently (`Workspace: "share"` when simultaneous builds/edits occur, then land cleanly onto the feature branch).
- **Mandatory UI Review:** Whenever any user-facing file under `apps/desktop/src/` (React/CSS/HTML) changes, run `ui-reviewer` following `.cursor/agents/guildhall-ui-reviewer.md` before opening the PR (and pair with `browser` when a live dev server is running). Report visual review separately from automated checks.

### Step 3 — Targeted Verification & Keep the Index in Sync (`verifier` / `flash`)
1. Run a `verifier` (`Model: "flash"`) subagent on the card's `## Targeted Verification` commands plus applicable CI checks (locked restores only; never target `GuildhallDev` in automated tests — isolated `GuildhallTest_<guid>` only).
2. **Keep the Index in Sync Before Committing (Mandatory Iterative Loop):**
   - Run `python scripts/index_doc.py --dirty` to check if your edits affected any indexed workflow cards.
   - If you added a new workflow, moved symbols beyond **±25 lines**, renamed symbols, or changed routes/entities/guards/invariants:
     - Follow `.agents/skills/codebase-index/SKILL.md` (`index-crawler` -> `index-builder` -> `index-auditor` -> `index-registrar`, or targeted card edit + `index-auditor` for small single-card updates) and update `verified_commit` to the latest base/source commit.
     - Run `python scripts/index_doc.py --write` to deterministically recompile `manifest.json` and `INDEX.md`.
   - Confirm `python scripts/index_doc.py --self-check` and `python scripts/index_doc.py --release-ready` both exit `0`.
3. Confirm `git status` excludes `.agents/index/.crawl/`, local credentials/tokens, and untracked runtime state.

### Step 4 — PR Creation, `code-reviewer` Loop & Human Merge Gate
1. **Commit, Rebase & Push:**
   - Commit on the scoped branch (`Closes #N` when linked to an issue).
   - `git fetch origin` and `git rebase origin/main` if `main` moved (re-run verification + `index_doc.py --release-ready` if rebased; on any real conflict, `git rebase --abort` and stop to ask the user).
   - Push `git push -u origin HEAD` (**never** push straight to `main`, **never** force-push) and open a PR targeting `main` via UTF-8 `--body-file`.
2. **Fresh-Context `code-reviewer` (`pro` / `inherit`) Loop:**
   - Spawn `code-reviewer` on the PR diff (checking Guildhall invariants, security boundaries, `manifest.json` drift, and test coverage).
   - If blocking findings are returned: dispatch targeted fix -> re-verify (`verifier` + `index_doc.py --release-ready`) -> push -> re-review (max 3 rounds; escalate to user if unresolved).
3. **Human Merge Gate & Guildhall Handoff:**
   - **Never merge without explicit owner approval.** Once approved, merge via `gh pr merge <N> --merge` (only when explicitly requested).
   - Always close with Guildhall's required completion handoff:
     1. PR link, target branch (`main`), review verdict, automated verification & index-sync status (plus separate visual review summary if UI changed).
     2. Copy-pasteable command(s) to launch Guildhall in the active worktree with GPU mode and maximum model retention:
        - Default: `pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -UseGpu -KeepModelWarmSeconds 900`
        - Replace existing instance on target ports: `pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -UseGpu -KeepModelWarmSeconds 900 -Replace`
        - Side-by-side worktree instance: `pwsh -NoProfile -File .\scripts\startup\Start-Dev.ps1 -UseGpu -KeepModelWarmSeconds 900 -AutoPort`
     3. Migrations run (or explicit "None — no SQL schema changes").
     4. Manual testing notes / functional QC checklist.
