---
name: codebase-index
description: >-
  Navigates, maintains, crawls, builds, registers, and audits Guildhall's
  3-tier Codebase Workflow Index (.agents/index/) and deterministic reverse
  lookup manifest (.agents/index/manifest.json) via scripts/index_doc.py. Use
  when locating cross-layer Guildhall workflows under 2,000 tokens, checking
  impacted workflows for edited files, refreshing dirty workflow cards, or
  adding a new indexed workflow or domain area.
---

# Guildhall Codebase Workflow Index

The Codebase Workflow Index (`.agents/index/`, [Decision 0005](../../../docs/decisions/0005-codebase-workflow-index.md)) keeps cross-session workflow lookups under **2,000 tokens** without repeatedly grepping or reading large files (`App.tsx`, `ChatEndpoints.cs`, `ChatRunner.cs`, `GuildhallDbContext.cs`, `ConnectionCatalogService.cs`, `ScheduledTasksPanel.tsx`).

## 1. Fast Consumer Lookup (<2,000 Tokens)

1. **Forward lookup (by intent, HTTP route, Tauri IPC command, EF entity, MCP server, or symbol):**
   - Read [.agents/index/INDEX.md](../../index/INDEX.md) (<=120 lines).
   - Read the matching Area Map (`.agents/index/areas/<area>.md`, <=100 lines) when module ownership, shared locks, or EF entities matter.
   - Read the matching Workflow Card (`.agents/index/workflows/<id>.md`, <=90 lines).
   - Call bounded `read_file(Path, StartLine, EndLine)` directly on each hop's `File` and `Line Hint` (`~Lstart-Lend`).
2. **Reverse lookup (by file or symbol being edited or reviewed):**
   - Inspect `file_to_workflows["<repo-relative-path>"]` or `symbol_to_workflows["<path>::<symbol>"]` in [.agents/index/manifest.json](../../index/manifest.json).
   - Open every listed Workflow Card before changing shared state, locks, EF entities, API contracts, security guards, or symbol anchors.

## 2. Validator Commands (`scripts/index_doc.py`)

Pure Python 3 stdlib (`<0.5s`, zero external packages):

- `python scripts/index_doc.py` — Validates YAML frontmatter, `uncertainties` schema, line caps (`INDEX.md` <=120, `areas/*.md` <=100, `workflows/*.md` <=90), file existence, symbol presence within **±25 lines** of `Line Hint`, relative Markdown links, and byte-for-byte parity of `manifest.json` and `INDEX.md`.
- `python scripts/index_doc.py --write` — Deterministically compiles `.agents/index/manifest.json` and updates `<!-- index-router-start --> ... <!-- index-router-end -->` in `.agents/index/INDEX.md`.
- `python scripts/index_doc.py --release-ready` — Runs all default checks plus fails if `.agents/index/.crawl/` exists (on disk or tracked in git), if any card has `"static"` uncertainties, or if any card contains `TODO`/`TBD` placeholders.
- `python scripts/index_doc.py --dirty` — Prints workflow IDs whose `primary_files` changed since `verified_commit` (including both committed and working-tree edits).
- `python scripts/index_doc.py --self-check` — Runs built-in unit self-tests in an isolated temporary git repository.

## 3. Model Routing & Multi-Agent Pipeline

Canonical role specifications live in [`.agents/agents/`](../../agents) and extend Guildhall's 6-role engineering & index pipeline roster in [`.agents/roster.md`](../../roster.md):

| Role | Agent Spec | Recommended Model Tier | Concurrency | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Orchestrator** | *(Parent session)* | `inherit` / High | 1 | Wave sequencing, shard coordination, escalation, owner gates |
| **Crawler** | `index-crawler` | `flash` | Up to 6 parallel | Read-only single-shard workflow tracing -> `.agents/index/.crawl/<shard>.json` |
| **Builder** | `index-builder` | `flash` | Up to 6 parallel | Zero-recrawl synthesis of `areas/*.md` and `workflows/*.md` from `.crawl/` + `manifest.json` |
| **Registrar** | `index-registrar` | `flash_lite` | 1 (post-shard) | Runs `python scripts/index_doc.py --write` and curates intent triggers & area hints in `INDEX.md` |
| **Auditor** | `index-auditor` | `pro` (or `inherit`) | Up to 6 parallel | Audits built or dirty Index cards against live source symbols, line hints, Guildhall invariants, and `.crawl/` traces |
| **GitHub Manager** | `github-manager` | `flash` *(spawns `code-reviewer` on `pro`/`inherit`)* | 1 per GitHub gate | 3-search gate, UTF-8 issue/PR creation targeting `main`, `code-reviewer` loop, human merge gate |

## 4. Iterative Maintenance: Refreshing Dirty Cards or Adding a Workflow

1. Run `python scripts/index_doc.py --dirty` (or look up edited files in `manifest.json` `file_to_workflows`).
2. For substantial or multi-file changes, dispatch `index-crawler` on the affected shard/workflow to trace live hops into `.agents/index/.crawl/<shard>.json` and resolve every `"static"` uncertainty.
3. Dispatch `index-builder` (zero-recrawl: reads only `.crawl/<shard>.json`, [SCHEMA.md](../../index/SCHEMA.md), and `manifest.json`) to update disjoint `areas/<area>.md` and `workflows/<id>.md` cards with the current `verified_commit` (for small single-hop line shifts, update the card directly and still audit it).
4. Dispatch `index-auditor` against live repository source files until `PASS`.
5. Dispatch `index-registrar` (`python scripts/index_doc.py --write`), remove `.agents/index/.crawl/`, and confirm `python scripts/index_doc.py --release-ready` exits `0`.
