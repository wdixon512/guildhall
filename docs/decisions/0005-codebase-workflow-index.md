# Decision 0005 — Codebase workflow index and `/work-work` orchestration

Status: approved and implemented on November 24, 2026 (tracked in `#11`).

## Context & problem statement

Guildhall spans a React 19 + TypeScript desktop UI (`apps/desktop/src/`), a Tauri 2 Rust desktop shell (`apps/desktop/src-tauri/`), an ASP.NET Core on `.NET 10` Agent Host (`services/agent-host/` across `Api`, `Chat`, `Configuration`, `Health`, `Mcp`, `Persistence`, `Preambles`, `Providers`, `Scheduling`, `Security`, `Workspaces`), SQL Server / `EF Core 10` migrations (`Persistence/Migrations/`), PowerShell multi-instance and database scripts (`scripts/`), and unit/SQL integration test suites (`tests/Guildhall.AgentHost.Tests/`).

Without a structured workflow index:

- Coding agents repeatedly run broad codebase greps and read large files (`App.tsx`, `ChatEndpoints.cs`, `ChatRunner.cs`, `GuildhallDbContext.cs`, `ConnectionCatalogService.cs`, `ScheduledTasksPanel.tsx`) from scratch on every session, wasting tokens before making a single edit.
- Cross-layer execution spines (for example, Tauri `bootstrap` -> `LocalApiBoundary` -> `ChatEndpoints` -> `ChatRunner` -> `ToolPermissionGate` -> `ToolRollbackService` -> `RunTraceList`) must be rediscovered each time.
- Subtle Guildhall invariants (`ILazyLoader` without proxy packages, deferred `ChatAttachments` `varbinary(max)` loading, `guildhall_runtime` DDL-free split, `SqlReadOnlyGuard` blocking `GuildhallDev`, `ToolPermissionGate` Tier 3 HITL enforcement, `EnvironmentScrubber`, `ChatRunner.MutationGate`, OCC `Revision` tokens, and `PreambleCacheService` 50–100 MB LRU bounds) can be missed when editing a single file in isolation.
- PR reviewers lack a deterministic reverse map from edited files and symbols back to all affected multi-layer workflows.

## Decision

### 1. Centralized 3-tier Markdown index (`.agents/index/`)

Establish a symbol-anchored, token-bounded 3-tier index in `.agents/index/` targeting **<2,000 tokens per workflow lookup**:

1. **Tier 0 Router (`.agents/index/INDEX.md`, hard cap <=120 lines):** Lookup protocol, curated high-signal intent triggers, 1-line area routing hints, and deterministic router tables delimited by `<!-- index-router-start --> ... <!-- index-router-end -->`.
2. **Tier 1 Domain Area Maps (`.agents/index/areas/<area>.md`, hard cap <=100 lines each):** Domain module ownership table, shared state/locks/entities, entrypoints, bidirectional workflow links, and links (never copied prose) to `.agents/skills/*`, `docs/coding-standards.md`, and `docs/masterplan/*`.
3. **Tier 2 Workflow Cards (`.agents/index/workflows/<id>.md`, hard cap <=90 lines each):** YAML frontmatter (`id`, `title`, `area`, `triggers`, `primary_files`, `verified_commit`, `related_skills`, `related_docs`, `uncertainties`), a 7-column `## Execution Spine` table (`Hop`, `Layer`, `File`, `Symbol / Anchor`, `Line Hint`, `State / Wire`, `Invariant / Trap`), `## Contracts & Guardrails`, and `## Targeted Verification`.

Full schema rules live in [`.agents/index/SCHEMA.md`](../../.agents/index/SCHEMA.md).

### 2. Pure-stdlib validator & reverse-lookup manifest (`scripts/index_doc.py`)

`python scripts/index_doc.py --write` (pure Python 3 stdlib, `<0.5s`, zero external dependencies) deterministically compiles `.agents/index/manifest.json` (`schema_version: 1`) containing:

- `workflows`: Metadata, verified symbol anchors, `verified_commit`, and `uncertainties_count` keyed by workflow ID.
- `file_to_workflows`: Sorted map from repo-relative file path to every workflow card referencing it.
- `symbol_to_workflows`: Sorted map from `<repo-relative-file>::<symbol>` to every workflow card anchored at that symbol.

`python scripts/index_doc.py` enforces frontmatter schemas, line caps, allowed Guildhall layers, file existence, symbol presence within **±25 lines** of `~Lstart-Lend`, relative Markdown links, and byte-for-byte parity of `manifest.json` and `INDEX.md`. `--release-ready` additionally rejects leftover `.agents/index/.crawl/` files, unresolved `"static"` uncertainties, and `TODO`/`TBD` placeholders. `--dirty` diffs `<verified_commit>` against the working tree across `primary_files` to flag stale cards immediately before committing.

### 3. Multi-agent maintenance & `/work-work` workflow

- Canonical index roles live in `.agents/agents/{index-crawler,index-builder,index-auditor,index-registrar,github-manager}.md` and are coordinated by `.agents/skills/codebase-index/SKILL.md` and `.agents/skills/work-work/SKILL.md`.
- Static source-code ambiguities must be resolved by `index-crawler` before card synthesis; `uncertainties` is reserved strictly for genuine runtime/external boundaries (such as `%LOCALAPPDATA%\Guildhall\` DPAPI files or local SQL Server / Ollama runtime configuration).
- `.github/workflows/ci.yml` runs `python3 scripts/index_doc.py --self-check` and `python3 scripts/index_doc.py --release-ready` (`<1s`) and explicitly includes `.agents/index/**` under `paths:` (after `!**/*.md`) so index-only PRs remain validated without breaking `docs/**` cost savings.

## Consequences

- **Positive:** Agents navigate Guildhall in `<2,000` tokens before grepping; reverse lookup via `manifest.json` exposes cross-layer blast radius before edits; CI catches drifted anchors or stale `manifest.json` automatically with zero added NuGet/npm/Cargo dependencies and zero runtime overhead.
- **Tradeoffs:** PRs that rename, delete, or shift indexed symbols beyond `±25` lines must refresh the affected workflow card(s) and run `python scripts/index_doc.py --write` before opening a PR (handled iteratively inside `/work-work` Phase 3).
