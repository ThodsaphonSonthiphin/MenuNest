#!/usr/bin/env python3
"""Tests for render_doc.py. Run: python test_render_doc.py"""
import json
import os
import subprocess
import sys
import tempfile

import render_doc
from render_doc import (build_html, find_browser, main,
                        _apply_markers, _detect_lang)

MD = """# ตัวอย่าง

```mermaid
flowchart TD
    A["เริ่ม"] --> B["จบ"]
```

ข้อความไทยพร้อม **ตัวหนา**
"""

MD_WITH_FURNITURE = """# รายงาน

<!-- sa-doc:pagebreak -->
<!-- sa-doc:toc -->
<!-- sa-doc:pagebreak -->

## 1. บทนำ

| a | b |
|---|---|
| 1 | 2 |

```mermaid
flowchart TD
    A --> B
```
"""


def test_build_html_embeds_markdown_as_json():
    html = build_html(MD, "ตัวอย่าง", None, None)
    assert json.dumps(MD) in html          # md embedded as a JS string
    assert "cdn.jsdelivr.net/npm/marked" in html
    assert "cdn.jsdelivr.net/npm/mermaid" in html
    assert "Sarabun" in html               # Thai-capable font stack
    assert "<title>ตัวอย่าง</title>" in html


def test_build_html_offline_inlines_scripts():
    html = build_html(MD, "t", "/*MARKED*/", "/*MERMAID*/")
    assert "/*MARKED*/" in html and "/*MERMAID*/" in html
    assert "cdn.jsdelivr.net" not in html


def test_build_html_escapes_title():
    out = build_html(MD, "a<b>&c", None, None)
    assert "<title>a<b>&c</title>" not in out
    assert "&lt;b&gt;" in out
    assert "&amp;" in out


def test_cli_writes_html():
    with tempfile.TemporaryDirectory() as d:
        src = os.path.join(d, "doc.md")
        with open(src, "w", encoding="utf-8") as f:
            f.write(MD)
        rc = main([src, "--out-dir", d])
        assert rc == 0
        assert os.path.exists(os.path.join(d, "doc.html"))


def test_find_browser_returns_path_or_none():
    b = find_browser()
    assert b is None or os.path.exists(b)


def test_build_html_escapes_script_close():
    md = "before\n\n```html\n<script>alert(1)</script>\n```\n\nafter\n"
    html = build_html(md, "t", None, None)
    raw_line = next(
        line for line in html.splitlines() if line.strip().startswith("const raw")
    )
    assert "</script>" not in raw_line   # would prematurely close the <script> block
    assert "<\\/script>" in raw_line     # escaped form parses to the same JS string


def test_detect_lang():
    assert _detect_lang("มีภาษาไทย") == "th"
    assert _detect_lang("only english here") == "en"


def test_apply_markers_expands_furniture():
    out = _apply_markers("x <!-- sa-doc:pagebreak --> y <!-- sa-doc:toc --> z")
    assert '<div class="sa-pagebreak"></div>' in out
    assert '<nav id="sa-toc"></nav>' in out
    assert "sa-doc:" not in out          # every marker was consumed


def test_build_html_no_markers_leaves_markdown_verbatim():
    # documents without furniture markers must embed byte-for-byte (the escape
    # of "</" is the only transform), so plain Markdown still renders.
    assert json.dumps(MD) in build_html(MD, "t", None, None)


def test_build_html_furniture_produces_toc_and_captions():
    html = build_html(MD_WITH_FURNITURE, "รายงาน", None, None)
    assert 'getElementById("sa-toc")' in html   # TOC builder wired to the mount
    assert "sa-figcaption" in html              # figure numbering wired
    assert "sa-tablecaption" in html            # table numbering wired
    assert "รูปที่" in html and "ตารางที่" in html   # Thai captions (auto-detected)
    assert "data-sa-render" in html             # render-completion flag


def test_build_html_lang_override_switches_captions():
    # captions are baked at build time, so only the chosen language appears
    html = build_html(MD_WITH_FURNITURE, "t", None, None, lang="en")
    assert "Figure" in html and "Table of Contents" in html
    assert "รูปที่" not in html


def test_print_pdf_page_numbers_toggles_header_footer():
    calls = {}

    def fake_run(cmd, **kwargs):
        calls["cmd"] = cmd

        class R:
            pass
        return R()

    orig = render_doc.subprocess.run
    render_doc.subprocess.run = fake_run
    try:
        render_doc.print_pdf("browser", "in.html", "out.pdf", page_numbers=False)
        assert "--no-pdf-header-footer" in calls["cmd"]
        render_doc.print_pdf("browser", "in.html", "out.pdf", page_numbers=True)
        assert "--no-pdf-header-footer" not in calls["cmd"]
    finally:
        render_doc.subprocess.run = orig


def test_cli_pdf_failure_degrades_gracefully():
    orig_find_browser = render_doc.find_browser
    orig_print_pdf = render_doc.print_pdf
    render_doc.find_browser = lambda: "dummy-browser"

    def _raise(*args, **kwargs):
        raise subprocess.CalledProcessError(1, "x")

    render_doc.print_pdf = _raise
    try:
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "doc.md")
            with open(src, "w", encoding="utf-8") as f:
                f.write(MD)
            rc = main([src, "--pdf", "--out-dir", d])
            assert rc == 0
            assert os.path.exists(os.path.join(d, "doc.html"))
    finally:
        render_doc.find_browser = orig_find_browser
        render_doc.print_pdf = orig_print_pdf


TESTS = [v for k, v in sorted(globals().items()) if k.startswith("test_")]

if __name__ == "__main__":
    failed = 0
    for t in TESTS:
        try:
            t()
            print(f"PASS  {t.__name__}")
        except AssertionError as e:
            failed += 1
            print(f"FAIL  {t.__name__}: {e}")
    print(f"{len(TESTS) - failed}/{len(TESTS)} passed")
    sys.exit(1 if failed else 0)
