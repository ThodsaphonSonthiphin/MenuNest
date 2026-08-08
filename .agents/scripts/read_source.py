"""
read_source.py — dump a spreadsheet (xlsx/xlsm/csv/tsv) to clean, LLM-readable text.

Used by the `extract-findings` skill so Claude can read tabular sources reliably
(avoids the Windows console encoding crash by forcing UTF-8 output).

Usage:
    python read_source.py "<path-to-file>" [--out <text-file>] [--max-rows N]

For non-tabular input (a .docx/.pdf/.md/.txt or pasted text), you don't need this —
read it directly. This helper exists only because spreadsheets are binary.
"""

import sys
import csv
import argparse

try:
    sys.stdout.reconfigure(encoding="utf-8")  # Python 3.7+; avoids cp1252 crashes on Windows
except Exception:
    pass


def dump_xlsx(path, max_rows):
    import openpyxl
    wb = openpyxl.load_workbook(path, data_only=True)
    lines = []
    for ws in wb.worksheets:
        lines.append(f"=== SHEET: {ws.title}  (dims {ws.dimensions}) ===")
        rows = list(ws.iter_rows(values_only=True))
        lines.append(f"rows: {len(rows)}")
        for i, row in enumerate(rows):
            if max_rows and i > max_rows:
                lines.append(f"... ({len(rows) - i} more rows truncated; rerun with --max-rows 0)")
                break
            cells = ["" if c is None else str(c) for c in row]
            while cells and cells[-1] == "":
                cells.pop()
            if cells:
                lines.append(f"[{i}] " + " | ".join(cells))
        lines.append("")
    return "\n".join(lines)


def dump_csv(path, max_rows, delim):
    lines = []
    with open(path, newline="", encoding="utf-8-sig") as f:
        reader = csv.reader(f, delimiter=delim)
        rows = list(reader)
    lines.append(f"=== {path}  (rows {len(rows)}) ===")
    for i, row in enumerate(rows):
        if max_rows and i > max_rows:
            lines.append(f"... ({len(rows) - i} more rows truncated)")
            break
        lines.append(f"[{i}] " + " | ".join(row))
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path")
    ap.add_argument("--out")
    ap.add_argument("--max-rows", type=int, default=200)
    args = ap.parse_args()

    low = args.path.lower()
    if low.endswith((".xlsx", ".xlsm")):
        text = dump_xlsx(args.path, args.max_rows)
    elif low.endswith(".tsv"):
        text = dump_csv(args.path, args.max_rows, "\t")
    elif low.endswith(".csv"):
        text = dump_csv(args.path, args.max_rows, ",")
    else:
        raise SystemExit(f"unsupported tabular type: {args.path} (read .docx/.pdf/.txt/.md directly instead)")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"wrote {args.out} ({len(text)} chars)")
    else:
        print(text)


if __name__ == "__main__":
    main()
