#!/usr/bin/env python3
"""check_doc_provenance.py — sa-doc faithfulness gate (Gate B).

Trace every HARD FACT (number / money / percentage / date digit) in the
generated document back to a value in the model — enforcing sa-doc's
"prose connects, never introduces" rule mechanically, because validate_model.py
never sees the source and so cannot. This closes the fabrication class the
Study→Design→Verify audit flagged: invented metrics, prices, sizes, amounts,
dates that read as plausible but appear in no model value.

Scope, deliberately narrow to stay low-false-positive: only NUMERIC tokens are
checked. Soft-prose fabrication (an invented interest, a mechanism word) is left
to the template wording (Source-or-TBD). Structural numbers — heading/section
numbers, ordered-list markers, id suffixes (FR-1, UC-2, TC-UC-ORDER-1), table
index/step cells — are exempt so the clean bookstore document is not flagged.

Usage:
  python check_doc_provenance.py <doc.md> <sa-model.yaml> [--source FILE ...] [--strict]

Report mode (default): prints findings, exit 0 — treat each like a validator
warning (remove the fabrication, or add the real value to the model). --strict
makes any untraceable token exit 1. Exit 2 = cannot run.
"""
import argparse
import re
import sys

try:
    import yaml
except ImportError:  # pragma: no cover
    print("ERROR: PyYAML is required. Install with: pip install pyyaml")
    sys.exit(2)

# A number: digits with optional thousands commas and an optional decimal part.
NUM = re.compile(r"\d[\d,]*(?:\.\d+)?")


def normalize(tok):
    """Canonical numeric form: drop thousands separators and a trailing %, and
    collapse integers so 07 == 7 and 20,000 == 20000. Decimals stay literal."""
    t = tok.replace(",", "").rstrip("%")
    return str(int(t)) if re.fullmatch(r"\d+", t) else t


def numbers_in(text):
    return {normalize(m.group()) for m in NUM.finditer(str(text))}


def model_numbers(model):
    """Every numeric value reachable in the model, from any scalar leaf."""
    out = set()

    def walk(node):
        if isinstance(node, dict):
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)
        elif node is not None:
            out.update(numbers_in(node))

    walk(model)
    return out


def _strip_noise(md):
    """Remove regions that legitimately carry non-prose numbers: fenced code and
    Mermaid blocks (diagrams are generated from the model), inline code, and the
    sa-doc HTML-comment markers."""
    md = re.sub(r"```.*?```", "", md, flags=re.S)
    md = re.sub(r"`[^`]*`", "", md)
    md = re.sub(r"<!--.*?-->", "", md, flags=re.S)
    return md


def _is_table_separator(line):
    return bool(re.fullmatch(r"\s*\|?[\s:|\-]+\|?\s*", line)) and "-" in line


def _strip_leading_markers(line):
    """Drop a heading marker and any leading section/list number so section
    titles (## 7.1 …) and ordered-list items (1. …) don't read as facts."""
    line = re.sub(r"^\s{0,3}#{1,6}\s+", "", line)
    line = re.sub(r"^\s*\d+(?:\.\d+)*[.)]?\s+", "", line)
    return line


def _bare_int_cells(line):
    """Values of table cells that are exactly an integer — row/step/field
    indices (the 13-field '#' column, main-flow step numbers). Exempt: real
    data values carry units/decimals/commas or live in the model already."""
    if "|" not in line:
        return set()
    return {str(int(c.strip())) for c in line.split("|")
            if re.fullmatch(r"\d+", c.strip())}


def doc_findings(md, allowed):
    """Numeric tokens in prose whose value is in neither the model nor an
    allowlisted structural context."""
    findings = []
    md = _strip_noise(md)
    for lineno, raw in enumerate(md.splitlines(), 1):
        if _is_table_separator(raw):
            continue
        exempt = _bare_int_cells(raw)
        line = _strip_leading_markers(raw)
        for m in NUM.finditer(line):
            start = m.start()
            prev = line[start - 1] if start > 0 else ""
            prev2 = line[start - 2] if start > 1 else ""
            # id-embedded digit: FR-1, UC2, TC-UC-ORDER-1, P1, SEC-1 …
            if prev.isalpha() or (prev == "-" and prev2.isalpha()):
                continue
            norm = normalize(m.group())
            if norm in allowed or norm in exempt:
                continue
            findings.append((lineno, m.group(), raw.strip()[:90]))
    return findings


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("doc")
    ap.add_argument("model")
    ap.add_argument("--source", nargs="*", default=[],
                    help="input file(s); a token found here is accepted too")
    ap.add_argument("--strict", action="store_true",
                    help="exit 1 if any untraceable token is found")
    args = ap.parse_args(argv)

    # The document is bilingual (Thai/English); a Windows cp1252 console would
    # crash printing Thai context. Force UTF-8 so findings always print.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):  # pragma: no cover
        pass

    with open(args.model, encoding="utf-8") as f:
        model = yaml.safe_load(f)
    with open(args.doc, encoding="utf-8") as f:
        md = f.read()

    allowed = model_numbers(model)
    for path in args.source:
        with open(path, encoding="utf-8") as f:
            allowed |= numbers_in(f.read())

    findings = doc_findings(md, allowed)
    if not findings:
        print("OK: every hard fact in the document traces to a model value "
              f"({len(allowed)} distinct numbers checked).")
        return 0

    print(f"{len(findings)} untraceable hard fact(s) - each is a fabrication to "
          f"remove, or a real value missing from the model:")
    for lineno, tok, ctx in findings:
        print(f"  line {lineno}: '{tok}' not in the model - {ctx}")
    return 1 if args.strict else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
