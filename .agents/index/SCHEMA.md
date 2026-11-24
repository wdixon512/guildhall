# Guildhall Codebase Workflow Index Schema (`schema_version: 1`)

This file is the normative specification for `.agents/index/` ([Decision 0005](../../docs/decisions/0005-codebase-workflow-index.md)). `python scripts/index_doc.py` enforces every rule below.

## 1. Hard Line & Token Caps

Target lookup cost: **<2,000 tokens** (`INDEX.md` + 1 Area Map + 1 Workflow Card + bounded `read_file` hops).

| Tier | Path | Hard Cap | Purpose |
| :--- | :--- | :--- | :--- |
| **Tier 0** | `.agents/index/INDEX.md` | **≤120 lines** | Quick-start lookup protocol, curated intent triggers, and auto-generated router tables (`<!-- index-router-start --> ... <!-- index-router-end -->`). |
| **Tier 1** | `.agents/index/areas/<area>.md` | **≤100 lines** | Domain module ownership table, shared state/locks/entities, public entrypoints, workflow card links, and skill/doc links (never duplicate prose). |
| **Tier 2** | `.agents/index/workflows/<id>.md` | **≤90 lines** | Hop-by-hop execution spine anchored to files, symbols, and `~Lstart-Lend` hints (`±25` line tolerance), plus guardrails and verification commands. |
| **Graph** | `.agents/index/manifest.json` | Deterministic | Compiled by `python scripts/index_doc.py --write`: `workflows`, `file_to_workflows`, and `symbol_to_workflows`. |

## 2. Allowed `Layer` Values

Every `Execution Spine` hop must use one of these Guildhall layers:
- Single-tier: `Desktop / UI`, `Tauri / Rust`, `Agent Host`, `SQL / EF`, `Providers`, `MCP`, `Security`, `Scheduling`, `Ops / Scripts`, `CI`
- Cross-boundary: `Desktop / Tauri`, `Agent Host / SQL`, `Agent Host / Providers`, `Agent Host / MCP`, `Agent Host / Security`, `Ops / SQL`

## 3. Tier 1 Area Map Format (`.agents/index/areas/<area>.md`)

Filename stem `<area>` must match `^[a-z0-9]+(?:-[a-z0-9]+)*$` and equal frontmatter `area`.

```markdown
---
area: <area-slug>
title: <Plain-English Area Title>
summary: <One-line domain summary (<=160 chars) rendered in INDEX.md>
primary_files:
  - <repo-relative/path>
related_skills:
  - <skill-name>
related_docs:
  - <repo-relative-doc-path>
---

# <Title>

One-sentence domain scope and boundary summary.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `services/agent-host/Example.cs` | Concise role | Entities, caches, locks, DPAPI keys | Key methods / endpoints |

## Workflows

- [`<workflow-id>`](../workflows/<workflow-id>.md) — One-line description.

## Contracts & References

- Links to `../../skills/<skill>/SKILL.md`, `../../../docs/coding-standards.md`, and `../../../docs/masterplan/*`.
```

Every workflow card whose frontmatter sets `area: <area-slug>` **must** be linked in that area map's `## Workflows` section.

## 4. Tier 2 Workflow Card Format (`.agents/index/workflows/<id>.md`)

Filename stem `<id>` must match `^[a-z0-9]+(?:-[a-z0-9]+)*$` and equal frontmatter `id`. No multi-paragraph prose; use compact tables and single-line bullets only.

```markdown
---
id: <kebab-case-id>
title: <Plain-English Workflow Title>
area: <area-slug>
triggers:
  - <keyword, HTTP route, Tauri IPC command, EF entity, or C#/TS symbol>
primary_files:
  - <repo-relative/path>
verified_commit: <git-commit-sha>
related_skills:
  - <skill-name>
related_docs:
  - <repo-relative-doc-path>
uncertainties: []
---

# <Title>

One-sentence summary of workflow purpose, entrypoint, and output.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host | `services/agent-host/Api/ChatEndpoints.cs` | `ChatEndpoints.MapChat` | `~L15-L60` | `POST /conversations/{id}/messages` | `LocalApiBoundary` bearer token + `MutationGate` |

## Contracts & Guardrails
- Concise bullets linking (never copying) applicable `../../skills/<skill>/SKILL.md`, `../../../docs/coding-standards.md`, and `../../../docs/masterplan/*`.

## Targeted Verification
- `<exact fast dotnet test, npm test, cargo test, or script command>`
```

### Anchor & `primary_files` Rules
- Every `primary_files` entry and every `Execution Spine` `File` cell must be a forward-slash repo-relative file path existing in the checkout, and every hop `File` must appear in `primary_files`.
- `Hop` numbers must be sequential integers `1..N` (`N >= 1`).
- `Symbol / Anchor` must occur inside `File` (either literally or via `Qualifier::member` / `Qualifier.member` where `Qualifier` is in `File` and `member` is a word-boundary identifier) within **±25 lines** of `Line Hint` (`~Lstart` or `~Lstart-Lend`).
- `related_skills` entries must match folder names under `.agents/skills/<name>/SKILL.md`. `related_docs` entries must exist in the checkout.

## 5. `uncertainties` Governance Rule

- **Zero Static Ambiguities:** Questions answerable from static repository code **must never** remain in `uncertainties`; `index-crawler` resolves them before `index-builder` authors the card.
- **Runtime / External Boundaries Only:** An entry in `uncertainties` is permitted **only** for genuine runtime-only or external-environment boundaries outside the repository checkout (for example, Windows DPAPI files under `%LOCALAPPDATA%\Guildhall\`, local SQL Server instance permissions, or local Ollama model manifests), formatted as:
  ```yaml
  uncertainties:
    - boundary: "Local DPAPI credential store"
      location: "services/agent-host/Configuration/DpapiCredentialStore.cs::DpapiCredentialStore"
      note: "Encrypted *.dpapi files live under %LOCALAPPDATA%\\Guildhall\\credentials outside git"
      static_or_runtime: "runtime"
  ```
- `python scripts/index_doc.py --release-ready` fails if `.agents/index/.crawl/` exists, if any card carries `"static"` uncertainties, or if any card contains `TODO` / `TBD` placeholders.

## 6. Transient Crawl Schema (`.agents/index/.crawl/<shard>.json`)

`index-crawler` writes UTF-8 JSON with `shard_id`, `head_commit`, `area_summary`, `workflows[]` (`id`, `title`, `area`, `summary`, `triggers`, `primary_files`, `hops[]` with `hop`, `layer`, `file`, `symbol`, `line_start`, `line_end`, `state_wire`, `invariant`, `related_skills`, `related_docs`, `verification_commands`), and `uncertainties[]` (`workflow_id`, `location`, `question`, `reason_unresolved`, `static_or_runtime`: `"static"` | `"runtime"`). Remove `.agents/index/.crawl/` before opening a PR (`--release-ready` rejects it).
