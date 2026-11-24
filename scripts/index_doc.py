#!/usr/bin/env python3
"""Build or check the Guildhall Codebase Workflow Index in .agents/index/.

Pure Python 3 stdlib validator and manifest/router compiler (Decision 0005).

    python scripts/index_doc.py                  # check schema, anchors, links, byte parity
    python scripts/index_doc.py --write          # compile manifest.json + INDEX.md router block
    python scripts/index_doc.py --release-ready  # check + forbid .crawl/, static uncertainties, TODO/TBD
    python scripts/index_doc.py --dirty          # print workflow IDs changed since verified_commit
    python scripts/index_doc.py --self-check     # run built-in unit self-tests in a temp directory
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]

SLUG_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
SHA_RE = re.compile(r"^[0-9a-f]{7,40}$")
LINE_HINT_RE = re.compile(r"^~L(\d+)(?:-L(\d+))?$")
ROUTER_MARKER_RE = re.compile(
    r"<!-- index-router-start -->\n(.*?)<!-- index-router-end -->",
    re.DOTALL,
)
FENCED_BLOCK_RE = re.compile(r"````.*?````|```.*?```", re.DOTALL)
INLINE_CODE_RE = re.compile(r"`[^`\n]+`")
MD_LINK_RE = re.compile(r"(?<!!)\[([^\]]+)\]\((<[^>\n]+>|[^)\s]+)(?:\s+\"[^\"]*\")?\)")
TODO_TBD_RE = re.compile(r"\b(?:TODO|TBD)\b")

INDEX_LINE_CAP = 120
AREA_LINE_CAP = 100
WORKFLOW_LINE_CAP = 90
ANCHOR_TOLERANCE_LINES = 25

AREA_REQUIRED_KEYS = (
    "area",
    "title",
    "summary",
    "primary_files",
    "related_skills",
    "related_docs",
)
WORKFLOW_REQUIRED_KEYS = (
    "id",
    "title",
    "area",
    "triggers",
    "primary_files",
    "verified_commit",
    "related_skills",
    "related_docs",
    "uncertainties",
)
EXPECTED_SPINE_HEADERS = [
    "Hop",
    "Layer",
    "File",
    "Symbol / Anchor",
    "Line Hint",
    "State / Wire",
    "Invariant / Trap",
]
ALLOWED_LAYERS = {
    "Desktop / UI",
    "Tauri / Rust",
    "Agent Host",
    "SQL / EF",
    "Providers",
    "MCP",
    "Security",
    "Scheduling",
    "Ops / Scripts",
    "CI",
    "Desktop / Tauri",
    "Agent Host / SQL",
    "Agent Host / Providers",
    "Agent Host / MCP",
    "Agent Host / Security",
    "Ops / SQL",
}


@dataclass(frozen=True)
class Hop:
    hop: int
    layer: str
    file: str
    symbol: str
    line_hint: str
    line_start: int
    line_end: int
    state_wire: str
    invariant: str


@dataclass(frozen=True)
class AreaCard:
    path: Path
    area: str
    title: str
    summary: str
    primary_files: list[str]
    related_skills: list[str]
    related_docs: list[str]
    body: str
    line_count: int


@dataclass(frozen=True)
class WorkflowCard:
    path: Path
    id: str
    title: str
    area: str
    triggers: list[str]
    primary_files: list[str]
    verified_commit: str
    related_skills: list[str]
    related_docs: list[str]
    uncertainties: list[dict[str, Any]]
    hops: list[Hop]
    body: str
    line_count: int


def _norm_text(raw: str) -> str:
    return raw.replace("\r\n", "\n").replace("\r", "\n")


def _write_utf8_lf(path: Path, content: str) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def _rel_posix(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def _parse_scalar(val: str, ctx: str) -> str:
    text = val.strip()
    if not text:
        return ""
    if text.startswith('"'):
        if not text.endswith('"') or len(text) < 2:
            raise ValueError(f"{ctx}: unclosed double-quoted string {val!r}")
        try:
            parsed = json.loads(text)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{ctx}: invalid double-quoted string {val!r}: {exc}") from exc
        if not isinstance(parsed, str):
            raise ValueError(f"{ctx}: expected string, got {type(parsed).__name__}")
        return parsed
    if text.startswith("'"):
        if not text.endswith("'") or len(text) < 2:
            raise ValueError(f"{ctx}: unclosed single-quoted string {val!r}")
        return text[1:-1].replace("''", "'")
    return text


def _split_flow_items(inner: str, ctx: str) -> list[str]:
    items: list[str] = []
    buf: list[str] = []
    quote: str | None = None
    escape = False
    for ch in inner:
        if escape:
            buf.append(ch)
            escape = False
            continue
        if quote == '"' and ch == "\\":
            buf.append(ch)
            escape = True
            continue
        if quote is not None:
            buf.append(ch)
            if ch == quote:
                quote = None
            continue
        if ch in ('"', "'"):
            quote = ch
            buf.append(ch)
            continue
        if ch == ",":
            piece = "".join(buf).strip()
            if piece:
                items.append(piece)
            buf = []
            continue
        buf.append(ch)
    if quote is not None:
        raise ValueError(f"{ctx}: unclosed quote in flow collection")
    tail = "".join(buf).strip()
    if tail:
        items.append(tail)
    return items


def _parse_flow_dict(text: str, ctx: str) -> dict[str, Any]:
    try:
        loaded = json.loads(text)
        if isinstance(loaded, dict):
            return loaded
    except json.JSONDecodeError:
        pass
    inner = text.strip()[1:-1].strip()
    if not inner:
        return {}
    out: dict[str, Any] = {}
    for entry in _split_flow_items(inner, ctx):
        if ":" not in entry:
            raise ValueError(f"{ctx}: expected key: value in flow mapping {entry!r}")
        k_raw, v_raw = entry.split(":", 1)
        key = _parse_scalar(k_raw, ctx)
        out[key] = _parse_scalar(v_raw, ctx)
    return out


def parse_frontmatter(text: str, label: str) -> tuple[dict[str, Any], str]:
    """Parse strict YAML frontmatter subset using pure Python stdlib."""
    norm = _norm_text(text)
    if not norm.startswith("---\n"):
        raise ValueError(f"{label}: missing opening '---' YAML frontmatter delimiter")
    end_idx = norm.find("\n---\n", 4)
    if end_idx == -1:
        if norm.endswith("\n---"):
            end_idx = len(norm) - 4
            body = ""
        else:
            raise ValueError(f"{label}: missing closing '---' YAML frontmatter delimiter")
    else:
        body = norm[end_idx + 5 :]
    fm_lines = norm[4:end_idx].split("\n")

    data: dict[str, Any] = {}
    idx = 0
    total = len(fm_lines)
    while idx < total:
        raw = fm_lines[idx]
        line_no = idx + 2
        ctx = f"{label}:L{line_no}"
        if "\t" in raw:
            raise ValueError(f"{ctx}: tabs are not allowed in YAML frontmatter")
        stripped = raw.strip()
        if not stripped or stripped.startswith("#"):
            idx += 1
            continue
        if raw.startswith(" "):
            raise ValueError(f"{ctx}: unexpected indentation outside list block: {raw!r}")
        if ":" not in raw:
            raise ValueError(f"{ctx}: expected 'key: value', got {raw!r}")
        key_part, rest_part = raw.split(":", 1)
        key = key_part.strip()
        if not re.fullmatch(r"[a-z][a-z0-9_]*", key):
            raise ValueError(f"{ctx}: invalid frontmatter key {key!r}")
        if key in data:
            raise ValueError(f"{ctx}: duplicate frontmatter key {key!r}")
        rest = rest_part.strip()
        if rest == "[]":
            data[key] = []
            idx += 1
            continue
        if rest.startswith("[") and rest.endswith("]"):
            inner = rest[1:-1].strip()
            data[key] = [_parse_scalar(x, ctx) for x in _split_flow_items(inner, ctx)] if inner else []
            idx += 1
            continue
        if rest:
            data[key] = _parse_scalar(rest, ctx)
            idx += 1
            continue

        # Block sequence under `key:`
        items: list[Any] = []
        idx += 1
        while idx < total:
            sub = fm_lines[idx]
            sub_no = idx + 2
            sub_ctx = f"{label}:L{sub_no}"
            if "\t" in sub:
                raise ValueError(f"{sub_ctx}: tabs are not allowed in YAML frontmatter")
            sub_strip = sub.strip()
            if not sub_strip or sub_strip.startswith("#"):
                idx += 1
                continue
            if not sub.startswith(" "):
                break
            indent = len(sub) - len(sub.lstrip(" "))
            if not sub_strip.startswith("-"):
                raise ValueError(f"{sub_ctx}: expected '- ' list item under {key!r}, got {sub!r}")
            after_dash = sub_strip[1:].strip()
            if after_dash.startswith("{") and after_dash.endswith("}"):
                items.append(_parse_flow_dict(after_dash, sub_ctx))
                idx += 1
                continue
            # Check if this is a multi-line mapping item: `- boundary: ...`
            if ":" in after_dash and not after_dash.startswith(('"', "'")):
                cand_k, cand_v = after_dash.split(":", 1)
                if re.fullmatch(r"[a-z_][a-z0-9_]*", cand_k.strip()):
                    next_is_map = False
                    peek = idx + 1
                    while peek < total:
                        p_line = fm_lines[peek]
                        p_strip = p_line.strip()
                        if not p_strip or p_strip.startswith("#"):
                            peek += 1
                            continue
                        p_indent = len(p_line) - len(p_line.lstrip(" "))
                        if p_indent > indent and not p_strip.startswith("-") and ":" in p_strip:
                            next_is_map = True
                        break
                    if next_is_map or key == "uncertainties":
                        obj: dict[str, Any] = {cand_k.strip(): _parse_scalar(cand_v, sub_ctx)}
                        idx += 1
                        while idx < total:
                            m_line = fm_lines[idx]
                            m_no = idx + 2
                            m_ctx = f"{label}:L{m_no}"
                            if "\t" in m_line:
                                raise ValueError(f"{m_ctx}: tabs are not allowed in YAML frontmatter")
                            m_strip = m_line.strip()
                            if not m_strip or m_strip.startswith("#"):
                                idx += 1
                                continue
                            m_indent = len(m_line) - len(m_line.lstrip(" "))
                            if m_indent <= indent:
                                break
                            if ":" not in m_strip:
                                raise ValueError(f"{m_ctx}: expected mapping key: value, got {m_line!r}")
                            mk, mv = m_strip.split(":", 1)
                            obj[mk.strip()] = _parse_scalar(mv, m_ctx)
                            idx += 1
                        items.append(obj)
                        continue
            items.append(_parse_scalar(after_dash, sub_ctx))
            idx += 1
        data[key] = items

    return data, body


def _strip_code_ticks(cell: str) -> str:
    val = cell.strip()
    if len(val) >= 2 and val.startswith("`") and val.endswith("`"):
        return val[1:-1].strip()
    return val


def _split_md_row(line: str) -> list[str]:
    text = line.strip()
    if text.startswith("|"):
        text = text[1:]
    if text.endswith("|"):
        text = text[:-1]
    cells: list[str] = []
    buf: list[str] = []
    in_tick = False
    escape = False
    for ch in text:
        if escape:
            buf.append(ch)
            escape = False
            continue
        if ch == "\\":
            escape = True
            continue
        if ch == "`":
            in_tick = not in_tick
            buf.append(ch)
            continue
        if ch == "|" and not in_tick:
            cells.append("".join(buf).strip())
            buf = []
            continue
        buf.append(ch)
    cells.append("".join(buf).strip())
    return cells


def _parse_execution_spine(body: str, label: str) -> list[Hop]:
    lines = body.split("\n")
    sec_idx = None
    for idx, line in enumerate(lines):
        if line.strip() == "## Execution Spine":
            sec_idx = idx
            break
    if sec_idx is None:
        raise ValueError(f"{label}: missing '## Execution Spine' section")

    table_lines: list[tuple[int, str]] = []
    for idx in range(sec_idx + 1, len(lines)):
        stripped = lines[idx].strip()
        if stripped.startswith("## "):
            break
        if stripped.startswith("|"):
            table_lines.append((idx + 1, stripped))
        elif table_lines and stripped:
            break

    if len(table_lines) < 3:
        raise ValueError(f"{label}: '## Execution Spine' must contain a Markdown table with >=1 hop")

    headers = _split_md_row(table_lines[0][1])
    if headers != EXPECTED_SPINE_HEADERS:
        raise ValueError(
            f"{label}: Execution Spine headers {headers!r} do not match expected {EXPECTED_SPINE_HEADERS!r}"
        )

    hops: list[Hop] = []
    for expected_hop, (_line_no, row_text) in enumerate(table_lines[2:], start=1):
        cells = _split_md_row(row_text)
        if len(cells) != 7:
            raise ValueError(f"{label}: hop row {expected_hop} has {len(cells)} columns (expected 7)")
        hop_str, layer_raw, file_raw, sym_raw, hint_raw, state_wire, invariant = cells
        if not hop_str.isdigit() or int(hop_str) != expected_hop:
            raise ValueError(
                f"{label}: hop number {hop_str!r} must be sequential integer {expected_hop}"
            )
        layer = _strip_code_ticks(layer_raw)
        if layer not in ALLOWED_LAYERS:
            raise ValueError(
                f"{label}: hop {expected_hop} layer {layer!r} not in {sorted(ALLOWED_LAYERS)}"
            )
        file_rel = _strip_code_ticks(file_raw)
        symbol = _strip_code_ticks(sym_raw)
        line_hint = _strip_code_ticks(hint_raw)
        if not file_rel:
            raise ValueError(f"{label}: hop {expected_hop} invalid repo-relative file {file_rel!r}")
        _validate_repo_rel_path(file_rel, f"hop {expected_hop}", label)
        if not symbol:
            raise ValueError(f"{label}: hop {expected_hop} empty Symbol / Anchor")
        m_hint = LINE_HINT_RE.match(line_hint)
        if not m_hint:
            raise ValueError(
                f"{label}: hop {expected_hop} Line Hint {line_hint!r} must match '~L<start>' or '~L<start>-L<end>'"
            )
        l_start = int(m_hint.group(1))
        l_end = int(m_hint.group(2)) if m_hint.group(2) else l_start
        if l_start < 1 or l_end < l_start:
            raise ValueError(f"{label}: hop {expected_hop} invalid line range {line_hint!r}")
        if not state_wire.strip():
            raise ValueError(f"{label}: hop {expected_hop} empty State / Wire cell")
        if not invariant.strip():
            raise ValueError(f"{label}: hop {expected_hop} empty Invariant / Trap cell")
        hops.append(
            Hop(
                hop=expected_hop,
                layer=layer,
                file=file_rel,
                symbol=symbol,
                line_hint=line_hint,
                line_start=l_start,
                line_end=l_end,
                state_wire=state_wire.strip(),
                invariant=invariant.strip(),
            )
        )
    return hops


def _validate_str_list(val: Any, key: str, label: str, *, allow_empty: bool) -> list[str]:
    if not isinstance(val, list) or not all(isinstance(x, str) and x.strip() for x in val):
        raise ValueError(f"{label}: frontmatter {key!r} must be a list of non-empty strings")
    if not allow_empty and not val:
        raise ValueError(f"{label}: frontmatter {key!r} must not be empty")
    return [x.strip() for x in val]


def _validate_repo_rel_path(rel: str, key: str, label: str) -> None:
    if (
        "\\" in rel
        or rel.startswith(("./", "/"))
        or ".." in rel.split("/")
        or Path(rel).is_absolute()
        or re.match(r"^[A-Za-z]:", rel)
    ):
        raise ValueError(f"{label}: {key} entry {rel!r} must be a clean forward-slash repo-relative path")


def load_area_card(path: Path, root: Path) -> AreaCard:
    label = _rel_posix(path, root)
    raw = _norm_text(path.read_text(encoding="utf-8"))
    line_count = len(raw.splitlines())
    fm, body = parse_frontmatter(raw, label)
    missing = [k for k in AREA_REQUIRED_KEYS if k not in fm]
    extra = [k for k in fm if k not in AREA_REQUIRED_KEYS]
    if missing:
        raise ValueError(f"{label}: missing frontmatter keys: {', '.join(missing)}")
    if extra:
        raise ValueError(f"{label}: unexpected frontmatter keys: {', '.join(extra)}")

    area = fm["area"]
    title = fm["title"]
    summary = fm["summary"]
    if not isinstance(area, str) or not SLUG_RE.match(area):
        raise ValueError(f"{label}: invalid kebab-case area slug {area!r}")
    if area != path.stem:
        raise ValueError(f"{label}: frontmatter area {area!r} does not match filename {path.name!r}")
    if not isinstance(title, str) or not title.strip():
        raise ValueError(f"{label}: frontmatter 'title' must be a non-empty string")
    if not isinstance(summary, str) or not summary.strip() or len(summary.strip()) > 160:
        raise ValueError(f"{label}: frontmatter 'summary' must be 1..160 chars")

    primary_files = _validate_str_list(fm["primary_files"], "primary_files", label, allow_empty=False)
    related_skills = _validate_str_list(fm["related_skills"], "related_skills", label, allow_empty=True)
    related_docs = _validate_str_list(fm["related_docs"], "related_docs", label, allow_empty=True)
    for rel in primary_files:
        _validate_repo_rel_path(rel, "primary_files", label)
    for rel in related_docs:
        _validate_repo_rel_path(rel, "related_docs", label)

    for required_heading in ("# ", "## Module & File Ownership", "## Workflows", "## Contracts & References"):
        if required_heading not in body:
            raise ValueError(f"{label}: missing required section heading {required_heading!r}")

    return AreaCard(
        path=path,
        area=area,
        title=title.strip(),
        summary=summary.strip(),
        primary_files=primary_files,
        related_skills=related_skills,
        related_docs=related_docs,
        body=body,
        line_count=line_count,
    )


def load_workflow_card(path: Path, root: Path) -> WorkflowCard:
    label = _rel_posix(path, root)
    raw = _norm_text(path.read_text(encoding="utf-8"))
    line_count = len(raw.splitlines())
    fm, body = parse_frontmatter(raw, label)
    missing = [k for k in WORKFLOW_REQUIRED_KEYS if k not in fm]
    extra = [k for k in fm if k not in WORKFLOW_REQUIRED_KEYS]
    if missing:
        raise ValueError(f"{label}: missing frontmatter keys: {', '.join(missing)}")
    if extra:
        raise ValueError(f"{label}: unexpected frontmatter keys: {', '.join(extra)}")

    wf_id = fm["id"]
    title = fm["title"]
    area = fm["area"]
    verified_commit = fm["verified_commit"]
    if not isinstance(wf_id, str) or not SLUG_RE.match(wf_id):
        raise ValueError(f"{label}: invalid kebab-case workflow id {wf_id!r}")
    if wf_id != path.stem:
        raise ValueError(f"{label}: frontmatter id {wf_id!r} does not match filename {path.name!r}")
    if not isinstance(title, str) or not title.strip():
        raise ValueError(f"{label}: frontmatter 'title' must be a non-empty string")
    if not isinstance(area, str) or not SLUG_RE.match(area):
        raise ValueError(f"{label}: invalid frontmatter 'area' {area!r}")
    if not isinstance(verified_commit, str) or not SHA_RE.match(verified_commit):
        raise ValueError(f"{label}: 'verified_commit' {verified_commit!r} must be a 7..40 char lowercase hex SHA")

    triggers = _validate_str_list(fm["triggers"], "triggers", label, allow_empty=False)
    primary_files = _validate_str_list(fm["primary_files"], "primary_files", label, allow_empty=False)
    related_skills = _validate_str_list(fm["related_skills"], "related_skills", label, allow_empty=True)
    related_docs = _validate_str_list(fm["related_docs"], "related_docs", label, allow_empty=True)
    for rel in primary_files:
        _validate_repo_rel_path(rel, "primary_files", label)
    for rel in related_docs:
        _validate_repo_rel_path(rel, "related_docs", label)

    uncertainties_raw = fm["uncertainties"]
    if not isinstance(uncertainties_raw, list):
        raise ValueError(f"{label}: frontmatter 'uncertainties' must be a list")
    uncertainties: list[dict[str, Any]] = []
    for idx, item in enumerate(uncertainties_raw):
        if not isinstance(item, dict):
            raise ValueError(f"{label}: uncertainties[{idx}] must be a mapping")
        allowed_u_keys = {"boundary", "location", "note", "static_or_runtime"}
        bad_keys = sorted(set(item) - allowed_u_keys)
        if bad_keys:
            raise ValueError(f"{label}: uncertainties[{idx}] has unexpected keys: {', '.join(bad_keys)}")
        for req_k in ("boundary", "location", "note"):
            val = item.get(req_k)
            if not isinstance(val, str) or not val.strip():
                raise ValueError(f"{label}: uncertainties[{idx}] missing non-empty {req_k!r}")
        loc = item["location"].strip()
        if "::" not in loc:
            raise ValueError(f"{label}: uncertainties[{idx}] location {loc!r} must be '<file>::<symbol>'")
        u_file_raw, u_sym_raw = loc.split("::", 1)
        u_file = u_file_raw.strip()
        u_sym = u_sym_raw.strip()
        if not u_file or not u_sym:
            raise ValueError(f"{label}: uncertainties[{idx}] location {loc!r} must be '<file>::<symbol>'")
        _validate_repo_rel_path(u_file, "uncertainties.location", label)
        sor = item.get("static_or_runtime")
        if sor is not None and sor not in ("static", "runtime"):
            raise ValueError(
                f"{label}: uncertainties[{idx}] static_or_runtime must be 'static' or 'runtime'"
            )
        uncertainties.append(item)

    for required_heading in (
        "# ",
        "## Execution Spine",
        "## Contracts & Guardrails",
        "## Targeted Verification",
    ):
        if required_heading not in body:
            raise ValueError(f"{label}: missing required section heading {required_heading!r}")

    hops = _parse_execution_spine(body, label)
    return WorkflowCard(
        path=path,
        id=wf_id,
        title=title.strip(),
        area=area,
        triggers=triggers,
        primary_files=primary_files,
        verified_commit=verified_commit,
        related_skills=related_skills,
        related_docs=related_docs,
        uncertainties=uncertainties,
        hops=hops,
        body=body,
        line_count=line_count,
    )


def _find_symbol_lines(lines: list[str], symbol: str) -> list[int]:
    """Return 1-indexed line numbers matching symbol literally or via Qualifier::member."""
    direct = [idx for idx, line in enumerate(lines, start=1) if symbol in line]
    if direct:
        return direct
    for sep in ("::", ".", ":"):
        if sep in symbol:
            qual, member = symbol.rsplit(sep, 1)
            qual = qual.strip()
            member = member.strip()
            if qual and len(member) >= 2 and any(qual in line for line in lines):
                pat = re.compile(rf"\b{re.escape(member)}\b")
                hits = [idx for idx, line in enumerate(lines, start=1) if pat.search(line)]
                if hits:
                    return hits
    return []


def build_manifest(workflows: list[WorkflowCard]) -> dict[str, Any]:
    wf_map: dict[str, Any] = {}
    file_to_wf: dict[str, set[str]] = {}
    sym_to_wf: dict[str, set[str]] = {}

    for wf in sorted(workflows, key=lambda w: w.id):
        anchors: list[str] = []
        seen_anchors: set[str] = set()
        for hop in wf.hops:
            anchor_key = f"{hop.file}::{hop.symbol}"
            if anchor_key not in seen_anchors:
                seen_anchors.add(anchor_key)
                anchors.append(anchor_key)
            sym_to_wf.setdefault(anchor_key, set()).add(wf.id)
            file_to_wf.setdefault(hop.file, set()).add(wf.id)
        for rel in wf.primary_files:
            file_to_wf.setdefault(rel, set()).add(wf.id)

        wf_map[wf.id] = {
            "card": f".agents/index/workflows/{wf.id}.md",
            "title": wf.title,
            "area": wf.area,
            "triggers": list(wf.triggers),
            "primary_files": list(wf.primary_files),
            "anchors": anchors,
            "verified_commit": wf.verified_commit,
            "uncertainties_count": len(wf.uncertainties),
        }

    return {
        "schema_version": 1,
        "workflows": wf_map,
        "file_to_workflows": {k: sorted(v) for k, v in sorted(file_to_wf.items())},
        "symbol_to_workflows": {k: sorted(v) for k, v in sorted(sym_to_wf.items())},
    }


def render_manifest_json(workflows: list[WorkflowCard]) -> str:
    return json.dumps(build_manifest(workflows), indent=2) + "\n"


def _md_escape(text: str) -> str:
    return text.replace("|", "\\|").strip()


def render_router_block(areas: list[AreaCard], workflows: list[WorkflowCard]) -> str:
    sorted_areas = sorted(areas, key=lambda a: a.area)
    sorted_wfs = sorted(workflows, key=lambda w: w.id)
    counts_by_area: dict[str, int] = {}
    for wf in sorted_wfs:
        counts_by_area[wf.area] = counts_by_area.get(wf.area, 0) + 1

    lines: list[str] = [f"### Domain Areas ({len(sorted_areas)})", ""]
    if not sorted_areas:
        lines.append("_No domain area maps registered yet._")
    else:
        lines.append("| Area | Summary | Workflows |")
        lines.append("| :--- | :--- | :--- |")
        for a in sorted_areas:
            cnt = counts_by_area.get(a.area, 0)
            lines.append(
                f"| [`{a.area}`](areas/{a.area}.md) | {_md_escape(a.summary)} | {cnt} |"
            )

    lines.extend(["", f"### Workflows ({len(sorted_wfs)})", ""])
    if not sorted_wfs:
        lines.append("_No workflow cards registered yet._")
    else:
        lines.append("| Workflow | Area | Primary Triggers |")
        lines.append("| :--- | :--- | :--- |")
        for wf in sorted_wfs:
            trig = ", ".join(f"`{_md_escape(t)}`" for t in wf.triggers[:4])
            lines.append(
                f"| [`{wf.id}`](workflows/{wf.id}.md) | [`{wf.area}`](areas/{wf.area}.md) | {trig} |"
            )
    return "\n".join(lines) + "\n"


def replace_router_block(index_text: str, router_body: str) -> str:
    norm = _norm_text(index_text)
    replacement = f"<!-- index-router-start -->\n{router_body}<!-- index-router-end -->"
    updated, count = ROUTER_MARKER_RE.subn(lambda _m: replacement, norm)
    if count != 1:
        raise ValueError(
            ".agents/index/INDEX.md must contain exactly one "
            "<!-- index-router-start --> ... <!-- index-router-end --> block"
        )
    return updated


def _check_markdown_links(md_path: Path, root: Path) -> list[str]:
    errors: list[str] = []
    label = _rel_posix(md_path, root)
    raw = _norm_text(md_path.read_text(encoding="utf-8"))
    stripped = FENCED_BLOCK_RE.sub("", raw)
    # Replace inline code spans with a non-empty token so `[`slug`](target.md)`
    # becomes `[code](target.md)` (still checked) while standalone `` `[ex](fake.md)` ``
    # becomes `code` (ignored).
    stripped = INLINE_CODE_RE.sub("code", stripped)
    for match in MD_LINK_RE.finditer(stripped):
        href = match.group(2).strip()
        if href.startswith("<") and href.endswith(">"):
            href = href[1:-1].strip()
        if not href or href.startswith(("#", "http://", "https://", "mailto:", "conversation://")):
            continue
        target_part = unquote(href.split("#", 1)[0])
        if not target_part:
            continue
        if "\\" in target_part:
            errors.append(f"{label}: relative link {href!r} must use forward slashes")
            continue
        resolved = (md_path.parent / target_part).resolve()
        try:
            resolved.relative_to(root.resolve())
        except ValueError:
            errors.append(f"{label}: link {href!r} resolves outside repository root")
            continue
        if not resolved.exists():
            errors.append(f"{label}: broken relative link {href!r}")
    return errors


def load_all_cards(root: Path) -> tuple[list[AreaCard], list[WorkflowCard], list[str]]:
    index_dir = root / ".agents" / "index"
    areas_dir = index_dir / "areas"
    workflows_dir = index_dir / "workflows"
    areas: list[AreaCard] = []
    workflows: list[WorkflowCard] = []
    errors: list[str] = []

    if areas_dir.is_dir():
        for path in sorted(areas_dir.glob("*.md")):
            try:
                areas.append(load_area_card(path, root))
            except ValueError as exc:
                errors.append(str(exc))

    if workflows_dir.is_dir():
        for path in sorted(workflows_dir.glob("*.md")):
            try:
                workflows.append(load_workflow_card(path, root))
            except ValueError as exc:
                errors.append(str(exc))

    return areas, workflows, errors


def validate_index(root: Path, *, release_ready: bool = False, check_generated: bool = True) -> list[str]:
    errors: list[str] = []
    index_dir = root / ".agents" / "index"
    schema_path = index_dir / "SCHEMA.md"
    index_md_path = index_dir / "INDEX.md"
    manifest_path = index_dir / "manifest.json"
    crawl_dir = index_dir / ".crawl"

    for req in (schema_path, index_md_path, manifest_path):
        if not req.is_file():
            errors.append(f"missing required index file: {req.relative_to(root).as_posix()}")
    if errors:
        return errors

    areas, workflows, load_errors = load_all_cards(root)
    errors.extend(load_errors)

    # Line cap checks
    index_raw = _norm_text(index_md_path.read_text(encoding="utf-8"))
    index_lines = len(index_raw.splitlines())
    if index_lines > INDEX_LINE_CAP:
        errors.append(f".agents/index/INDEX.md has {index_lines} lines (hard cap {INDEX_LINE_CAP})")
    for a in areas:
        if a.line_count > AREA_LINE_CAP:
            errors.append(
                f"{_rel_posix(a.path, root)} has {a.line_count} lines (hard cap {AREA_LINE_CAP})"
            )
    for wf in workflows:
        if wf.line_count > WORKFLOW_LINE_CAP:
            errors.append(
                f"{_rel_posix(wf.path, root)} has {wf.line_count} lines (hard cap {WORKFLOW_LINE_CAP})"
            )

    area_by_slug = {a.area: a for a in areas}
    file_cache: dict[str, list[str]] = {}

    def get_file_lines(rel: str) -> list[str] | None:
        if rel in file_cache:
            return file_cache[rel]
        target = root / rel
        if not target.is_file():
            return None
        lines = _norm_text(target.read_text(encoding="utf-8", errors="replace")).split("\n")
        file_cache[rel] = lines
        return lines

    # Validate Area references, skills, docs, and bidirectional workflow links
    for a in areas:
        a_label = _rel_posix(a.path, root)
        for rel in a.primary_files:
            if not (root / rel).exists():
                errors.append(f"{a_label}: primary_files path {rel!r} does not exist")
        for skill in a.related_skills:
            if not (root / ".agents" / "skills" / skill / "SKILL.md").is_file():
                errors.append(f"{a_label}: related_skills {skill!r} missing .agents/skills/{skill}/SKILL.md")
        for doc in a.related_docs:
            if not (root / doc).exists():
                errors.append(f"{a_label}: related_docs {doc!r} does not exist")

    for wf in workflows:
        w_label = _rel_posix(wf.path, root)
        if wf.area not in area_by_slug:
            errors.append(f"{w_label}: area {wf.area!r} has no .agents/index/areas/{wf.area}.md card")
        else:
            expected_link = f"../workflows/{wf.id}.md"
            if expected_link not in area_by_slug[wf.area].body:
                errors.append(
                    f".agents/index/areas/{wf.area}.md: missing link to {expected_link!r} in ## Workflows"
                )

        primary_set = set(wf.primary_files)
        for rel in wf.primary_files:
            if not (root / rel).is_file():
                errors.append(f"{w_label}: primary_files file {rel!r} does not exist")
        for skill in wf.related_skills:
            if not (root / ".agents" / "skills" / skill / "SKILL.md").is_file():
                errors.append(f"{w_label}: related_skills {skill!r} missing .agents/skills/{skill}/SKILL.md")
        for doc in wf.related_docs:
            if not (root / doc).exists():
                errors.append(f"{w_label}: related_docs {doc!r} does not exist")

        for u_idx, unc in enumerate(wf.uncertainties):
            u_file, _u_sym = unc["location"].split("::", 1)
            u_file = u_file.strip()
            if not (root / u_file).is_file():
                errors.append(f"{w_label}: uncertainties[{u_idx}] location file {u_file!r} does not exist")
            if release_ready and unc.get("static_or_runtime") == "static":
                errors.append(f"{w_label}: uncertainties[{u_idx}] has unresolved static_or_runtime='static'")

        for hop in wf.hops:
            if hop.file not in primary_set:
                errors.append(
                    f"{w_label}: hop {hop.hop} file {hop.file!r} must also be listed in primary_files"
                )
            lines = get_file_lines(hop.file)
            if lines is None:
                errors.append(f"{w_label}: hop {hop.hop} file {hop.file!r} does not exist")
                continue
            if hop.line_start > len(lines):
                errors.append(
                    f"{w_label}: hop {hop.hop} Line Hint {hop.line_hint} exceeds {hop.file} length ({len(lines)} lines)"
                )
                continue
            hit_lines = _find_symbol_lines(lines, hop.symbol)
            if not hit_lines:
                errors.append(
                    f"{w_label}: hop {hop.hop} symbol {hop.symbol!r} not found in {hop.file}"
                )
                continue
            win_lo = max(1, hop.line_start - ANCHOR_TOLERANCE_LINES)
            win_hi = hop.line_end + ANCHOR_TOLERANCE_LINES
            if not any(win_lo <= ln <= win_hi for ln in hit_lines):
                sample = ", ".join(f"L{ln}" for ln in hit_lines[:5])
                errors.append(
                    f"{w_label}: hop {hop.hop} symbol {hop.symbol!r} in {hop.file} found at {sample}, "
                    f"outside {hop.line_hint} (±{ANCHOR_TOLERANCE_LINES} lines)"
                )

    # Check relative Markdown links across all .agents/index/**/*.md files
    for md_file in sorted(index_dir.rglob("*.md")):
        if ".crawl" in md_file.parts:
            continue
        errors.extend(_check_markdown_links(md_file, root))

    # Byte-for-byte parity check on manifest.json and INDEX.md
    if check_generated and not load_errors:
        expected_manifest = render_manifest_json(workflows)
        actual_manifest = _norm_text(manifest_path.read_text(encoding="utf-8"))
        if actual_manifest != expected_manifest:
            errors.append(
                ".agents/index/manifest.json is out of date; run python scripts/index_doc.py --write"
            )
        try:
            expected_index = replace_router_block(index_raw, render_router_block(areas, workflows))
            if index_raw != expected_index:
                errors.append(
                    ".agents/index/INDEX.md router block is out of date; run python scripts/index_doc.py --write"
                )
        except ValueError as exc:
            errors.append(str(exc))

    if release_ready:
        if crawl_dir.exists():
            errors.append(".agents/index/.crawl/ exists in working tree; remove before opening PR")
        if (root / ".git").exists():
            proc = subprocess.run(
                ["git", "-C", str(root), "ls-files", ".agents/index/.crawl"],
                capture_output=True,
                text=True,
                check=False,
            )
            if proc.returncode == 0 and proc.stdout.strip():
                errors.append(".agents/index/.crawl/ has tracked/staged files in git index")

        for card_path in [index_md_path, *(a.path for a in areas), *(w.path for w in workflows)]:
            card_label = _rel_posix(card_path, root)
            card_text = _norm_text(card_path.read_text(encoding="utf-8"))
            clean_text = INLINE_CODE_RE.sub("", FENCED_BLOCK_RE.sub("", card_text))
            m_todo = TODO_TBD_RE.search(clean_text)
            if m_todo:
                errors.append(
                    f"{card_label}: contains placeholder {m_todo.group(0)!r} (forbidden by --release-ready)"
                )

    return errors


def write_index(root: Path) -> list[str]:
    index_dir = root / ".agents" / "index"
    index_md_path = index_dir / "INDEX.md"
    manifest_path = index_dir / "manifest.json"
    if not index_md_path.is_file():
        return [f"missing {index_md_path.relative_to(root).as_posix()}"]

    areas, workflows, load_errors = load_all_cards(root)
    if load_errors:
        return load_errors

    manifest_text = render_manifest_json(workflows)
    index_raw = _norm_text(index_md_path.read_text(encoding="utf-8"))
    try:
        updated_index = replace_router_block(index_raw, render_router_block(areas, workflows))
    except ValueError as exc:
        return [str(exc)]

    _write_utf8_lf(manifest_path, manifest_text)
    _write_utf8_lf(index_md_path, updated_index)
    return validate_index(root, release_ready=False, check_generated=True)


def find_dirty_workflows(root: Path) -> tuple[list[str], list[str]]:
    _areas, workflows, load_errors = load_all_cards(root)
    if load_errors:
        return [], load_errors

    by_commit: dict[str, list[WorkflowCard]] = {}
    for wf in workflows:
        by_commit.setdefault(wf.verified_commit, []).append(wf)

    dirty: set[str] = set()
    errors: list[str] = []
    for commit_sha, group in sorted(by_commit.items()):
        verify = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "-q", "--verify", f"{commit_sha}^{{commit}}"],
            capture_output=True,
            text=True,
            check=False,
        )
        if verify.returncode != 0:
            for wf in group:
                dirty.add(wf.id)
            continue
        all_files = sorted({f for wf in group for f in wf.primary_files})
        # Diff against the working tree (NUL-delimited, unquoted) plus untracked primary_files
        # so committed, staged, unstaged, and brand-new untracked edits all mark cards dirty.
        diff = subprocess.run(
            ["git", "-C", str(root), "-c", "core.quotepath=false", "diff", "-z", "--name-only", commit_sha, "--", *all_files],
            capture_output=True,
            text=True,
            check=False,
        )
        if diff.returncode != 0:
            errors.append(f"git diff failed for {commit_sha}: {diff.stderr.strip()}")
            continue
        changed = {entry.replace("\\", "/") for entry in diff.stdout.split("\0") if entry}
        untracked = subprocess.run(
            ["git", "-C", str(root), "-c", "core.quotepath=false", "ls-files", "--others", "--exclude-standard", "-z", "--", *all_files],
            capture_output=True,
            text=True,
            check=False,
        )
        if untracked.returncode == 0:
            changed.update(entry.replace("\\", "/") for entry in untracked.stdout.split("\0") if entry)
        for wf in group:
            if changed.intersection(wf.primary_files):
                dirty.add(wf.id)

    return sorted(dirty), errors


def run_self_check() -> int:
    """Built-in unit tests exercising schema validation, anchor tolerance, --write, --release-ready, and --dirty."""
    with tempfile.TemporaryDirectory(prefix="guildhall-index-selfcheck-") as tmp:
        root = Path(tmp)

        def write_rel(rel: str, content: str) -> Path:
            p = root / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            _write_utf8_lf(p, content)
            return p

        # Initialize minimal git repo for --dirty and --release-ready tests
        subprocess.run(["git", "-C", str(root), "init", "-q"], check=True)
        subprocess.run(["git", "-C", str(root), "config", "core.autocrlf", "false"], check=True)
        subprocess.run(["git", "-C", str(root), "config", "user.email", "test@example.com"], check=True)
        subprocess.run(["git", "-C", str(root), "config", "user.name", "Test"], check=True)

        write_rel(".agents/skills/codebase-index/SKILL.md", "# codebase-index\n")
        write_rel("docs/coding-standards.md", "# coding standards\n")
        write_rel("docs/masterplan/phase 1/sample.md", "# phase 1 sample\n")
        src_lines = [f"// header line {i}" for i in range(1, 30)]
        src_lines.append("public sealed class SampleHostService")
        src_lines.append("    public void ExecuteAsync() {}")
        src_lines.extend([f"    // body {i}" for i in range(32, 45)])
        write_rel("services/agent-host/SampleHostService.cs", "\n".join(src_lines) + "\n")

        write_rel(".agents/index/SCHEMA.md", "# Schema\n")
        write_rel(
            ".agents/index/INDEX.md",
            "# Index\n\nSee [SCHEMA.md](SCHEMA.md).\n\n<!-- index-router-start -->\nold\n<!-- index-router-end -->\n",
        )
        write_rel(".agents/index/manifest.json", "{}\n")

        # 1. Bootstrap (0 cards) --write + --release-ready
        errs = write_index(root)
        assert not errs, f"bootstrap write failed: {errs}"
        errs = validate_index(root, release_ready=True)
        assert not errs, f"bootstrap release-ready failed: {errs}"

        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "base"], check=True)
        base_sha = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

        # 2. Valid area + workflow card
        write_rel(
            ".agents/index/areas/host-lifecycle.md",
            "---\n"
            "area: host-lifecycle\n"
            "title: Host Lifecycle\n"
            "summary: Loopback host startup and local request guards.\n"
            "primary_files:\n"
            "  - services/agent-host/SampleHostService.cs\n"
            "related_skills:\n"
            "  - codebase-index\n"
            "related_docs:\n"
            "  - docs/coding-standards.md\n"
            "---\n\n"
            "# Host Lifecycle\n\n"
            "Host scope.\n\n"
            "## Module & File Ownership\n\n"
            "| Module / File | Ownership | Shared State / Locks | Entrypoints |\n"
            "| :--- | :--- | :--- | :--- |\n"
            "| `services/agent-host/SampleHostService.cs` | Sample | `IDbContextFactory` | `SampleHostService` |\n\n"
            "## Workflows\n\n"
            "- [`sample-host-flow`](../workflows/sample-host-flow.md) — Sample host flow.\n\n"
            "## Contracts & References\n\n"
            "- [codebase-index](../../skills/codebase-index/SKILL.md)\n"
            "- [coding-standards](../../../docs/coding-standards.md)\n"
            "- [phase-1](<../../../docs/masterplan/phase 1/sample.md>)\n"
            "- [external](<https://example.com>)\n\n"
            "````markdown\n"
            "```text\n"
            "TODO inside 4-backtick fence [fake](missing.md)\n"
            "```\n"
            "````\n",
        )
        card_template = (
            "---\n"
            "id: sample-host-flow\n"
            "title: Sample Host Flow\n"
            "area: host-lifecycle\n"
            "triggers:\n"
            "  - SampleHostService\n"
            "  - /health\n"
            "primary_files:\n"
            "  - services/agent-host/SampleHostService.cs\n"
            f"verified_commit: {base_sha}\n"
            "related_skills:\n"
            "  - codebase-index\n"
            "related_docs:\n"
            "  - docs/coding-standards.md\n"
            "{uncertainties_block}"
            "---\n\n"
            "# Sample Host Flow\n\n"
            "Runs sample host flow.\n\n"
            "## Execution Spine\n\n"
            "| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |\n"
            "| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n"
            "| 1 | Agent Host | `services/agent-host/SampleHostService.cs` | `SampleHostService.ExecuteAsync` | `{line_hint}` | `DI` | Use `IDbContextFactory` |\n\n"
            "## Contracts & Guardrails\n"
            "- See [codebase-index](../../skills/codebase-index/SKILL.md).\n\n"
            "## Targeted Verification\n"
            "- `python scripts/index_doc.py`\n"
        )
        write_rel(
            ".agents/index/workflows/sample-host-flow.md",
            card_template.format(uncertainties_block="uncertainties: []\n", line_hint="~L10-L12"),
        )

        # Symbol is at L30: ~L10-L12 has window [1, 37] -> within ±25 lines -> PASS
        errs = write_index(root)
        assert not errs, f"valid card write failed: {errs}"
        manifest = json.loads((root / ".agents/index/manifest.json").read_text(encoding="utf-8"))
        assert manifest["file_to_workflows"] == {"services/agent-host/SampleHostService.cs": ["sample-host-flow"]}
        assert manifest["symbol_to_workflows"] == {
            "services/agent-host/SampleHostService.cs::SampleHostService.ExecuteAsync": ["sample-host-flow"]
        }

        # 3. Drift >25 lines + broken backtick-labeled link + backslash link + malformed uncertainty path -> FAIL
        write_rel(
            ".agents/index/workflows/sample-host-flow.md",
            card_template.format(uncertainties_block="uncertainties: []\n", line_hint="~L1-L3")
            + "\n- Broken backtick link: [`missing-wf`](missing-wf.md)\n"
            + "- Backslash link: [bad](..\\..\\skills\\codebase-index\\SKILL.md)\n",
        )
        errs = validate_index(root, check_generated=False)
        assert any("outside ~L1-L3" in e for e in errs), f"expected >25L drift error, got {errs}"
        assert any("missing-wf.md" in e for e in errs), f"expected backtick-link error, got {errs}"
        assert any("forward slashes" in e for e in errs), f"expected backslash-link error, got {errs}"

        bad_unc = (
            "uncertainties:\n"
            "  - boundary: \"Bad path\"\n"
            "    location: \"../escape.cs::SampleHostService\"\n"
            "    note: \"Must return error, not crash\"\n"
        )
        write_rel(
            ".agents/index/workflows/sample-host-flow.md",
            card_template.format(uncertainties_block=bad_unc, line_hint="~L28-L35"),
        )
        bad_errs = validate_index(root, check_generated=False)
        assert any("uncertainties.location" in e for e in bad_errs), f"expected clean error, got {bad_errs}"

        # 4. Static uncertainty + .crawl/ + TODO rejected by --release-ready
        static_unc = (
            "uncertainties:\n"
            "  - boundary: \"Static check\"\n"
            "    location: \"services/agent-host/SampleHostService.cs::SampleHostService\"\n"
            "    note: \"Unresolved\"\n"
            "    static_or_runtime: \"static\"\n"
        )
        write_rel(
            ".agents/index/workflows/sample-host-flow.md",
            card_template.format(uncertainties_block=static_unc, line_hint="~L28-L35") + "\nTODO fix\n",
        )
        write_rel(".agents/index/.crawl/S1.json", "{}\n")
        subprocess.run(["git", "-C", str(root), "add", ".agents/index/.crawl/S1.json"], check=True)
        write_index(root)
        rr_errs = validate_index(root, release_ready=True)
        assert any(".crawl/ exists" in e for e in rr_errs), rr_errs
        assert any("tracked/staged" in e for e in rr_errs), rr_errs
        assert any("static_or_runtime='static'" in e for e in rr_errs), rr_errs
        assert any("TODO" in e for e in rr_errs), rr_errs

        # Clean up and test --dirty (both uncommitted and committed)
        subprocess.run(["git", "-C", str(root), "rm", "-q", "-f", ".agents/index/.crawl/S1.json"], check=True)
        if (root / ".agents/index/.crawl").exists():
            (root / ".agents/index/.crawl").rmdir()
        write_rel(
            ".agents/index/workflows/sample-host-flow.md",
            card_template.format(uncertainties_block="uncertainties: []\n", line_hint="~L28-L35"),
        )
        assert not write_index(root)
        assert not validate_index(root, release_ready=True)

        dirty_before, d_errs = find_dirty_workflows(root)
        assert not d_errs and dirty_before == [], (dirty_before, d_errs)

        write_rel("services/agent-host/SampleHostService.cs", "\n".join(src_lines) + "\n// touched\n")
        dirty_uncommitted, d_errs = find_dirty_workflows(root)
        assert not d_errs and dirty_uncommitted == ["sample-host-flow"], (dirty_uncommitted, d_errs)

        subprocess.run(["git", "-C", str(root), "add", "services/agent-host/SampleHostService.cs"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "touch sample"], check=True)
        dirty_after, d_errs = find_dirty_workflows(root)
        assert not d_errs and dirty_after == ["sample-host-flow"], (dirty_after, d_errs)

    print("ok: index_doc.py self-check passed (bootstrap, schema, ±25L anchor, --release-ready, --dirty)")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: parent of scripts/)")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true", help="compile manifest.json and INDEX.md router block")
    mode.add_argument(
        "--release-ready",
        action="store_true",
        help="run all checks plus forbid .crawl/, static uncertainties, and TODO/TBD placeholders",
    )
    mode.add_argument(
        "--dirty",
        action="store_true",
        help="print workflow IDs whose primary_files changed since verified_commit",
    )
    mode.add_argument("--self-check", action="store_true", help="run built-in unit self-tests")
    args = parser.parse_args(argv)

    if args.self_check:
        return run_self_check()

    root = args.root.resolve()
    if args.dirty:
        dirty, errors = find_dirty_workflows(root)
        if errors:
            for err in errors:
                print(f"index dirty error: {err}", file=sys.stderr)
            return 1
        for wf_id in dirty:
            print(wf_id)
        return 0

    if args.write:
        errors = write_index(root)
        if errors:
            print("index_doc --write failed:", file=sys.stderr)
            for err in errors:
                print(f"  {err}", file=sys.stderr)
            return 1
        areas, workflows, _ = load_all_cards(root)
        print(f"updated .agents/index/ ({len(areas)} areas, {len(workflows)} workflows)")
        return 0

    errors = validate_index(root, release_ready=args.release_ready)
    if errors:
        mode_label = "index_doc (--release-ready)" if args.release_ready else "index_doc"
        print(f"{mode_label} check failed:", file=sys.stderr)
        for err in errors:
            print(f"  {err}", file=sys.stderr)
        return 1

    areas, workflows, _ = load_all_cards(root)
    suffix = " (release-ready)" if args.release_ready else ""
    print(f"ok: .agents/index/ matches {len(areas)} areas, {len(workflows)} workflows{suffix}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
