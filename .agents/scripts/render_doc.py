#!/usr/bin/env python3
"""render_doc.py — sa-doc renderer: Markdown -> self-contained HTML -> optional PDF.

Usage:
  python render_doc.py <input.md> [--pdf] [--out-dir DIR]
                       [--marked-js FILE] [--mermaid-js FILE]

The HTML renders the Markdown client-side (marked.js) and the Mermaid blocks
(mermaid.js). Default script sources are CDN; pass local files for offline use.
PDF uses headless Edge/Chrome with --virtual-time-budget so client-side
rendering finishes before printing. Missing browser degrades to HTML + advice,
never a failure.
"""
import argparse
import html
import json
import pathlib
import shutil
import subprocess
import sys

MARKED_CDN = "https://cdn.jsdelivr.net/npm/marked/marked.min.js"
MERMAID_CDN = "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"

CAPTIONS = {
    "th": {"toc": "สารบัญ", "fig": "รูปที่", "tbl": "ตารางที่"},
    "en": {"toc": "Table of Contents", "fig": "Figure", "tbl": "Table"},
}

WINDOWS_BROWSER_PATHS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
]

CSS = """
@page { size: A4; margin: 20mm 18mm; }
body { font-family: "Sarabun", "Leelawadee UI", Tahoma, sans-serif;
       font-size: 11pt; line-height: 1.6; max-width: 180mm; margin: 0 auto;
       color: #1a1a1a; }
h1 { page-break-before: always; }
h1:first-of-type { page-break-before: avoid; }
h1, h2, h3 { line-height: 1.3; }
table { border-collapse: collapse; width: 100%; margin: 0.8em 0; }
th, td { border: 1px solid #999; padding: 4px 8px; text-align: left;
         vertical-align: top; }
th { background: #f0f0f0; }
pre { background: #f6f6f6; padding: 8px; overflow-x: auto; }
.mermaid { display: flex; justify-content: center; margin: 1em 0;
           page-break-inside: avoid; }
h1:first-of-type { text-align: center; font-size: 2em; margin-top: 2.5em; }
.sa-pagebreak { page-break-after: always; }
nav#sa-toc { page-break-inside: avoid; margin: 1.5em 0; }
nav#sa-toc .sa-toc-title { font-size: 1.5em; font-weight: bold;
                           text-align: center; margin-bottom: 0.6em; }
nav#sa-toc ul { list-style: none; padding-left: 1.2em; margin: 0.2em 0; }
nav#sa-toc a { text-decoration: none; color: inherit; }
nav#sa-toc .sa-toc-h1 > a { font-weight: bold; }
.sa-figcaption { text-align: center; font-size: 0.9em; color: #555;
                 margin: 0.3em 0 1.1em; }
.sa-tablecaption { text-align: center; font-size: 0.9em; color: #555;
                   margin: 1.1em 0 0.3em; }
"""


def _script_tag(inline_body, cdn_url):
    if inline_body is not None:
        return f"<script>{inline_body}</script>"
    return f'<script src="{cdn_url}"></script>'


def _detect_lang(md_text):
    """Thai if the document contains any character in the Thai Unicode block,
    else English. Used only to localize auto-generated captions."""
    return "th" if any("฀" <= ch <= "๿" for ch in md_text) else "en"


def _apply_markers(md_text):
    """Turn the sa-doc furniture markers into HTML the page can style/fill.
    Markers are HTML comments so a Markdown viewer that never runs this script
    still shows a clean document (comments are invisible)."""
    return (md_text
            .replace("<!-- sa-doc:pagebreak -->", '<div class="sa-pagebreak"></div>')
            .replace("<!-- sa-doc:toc -->", '<nav id="sa-toc"></nav>'))


def build_html(md_text, title, marked_js, mermaid_js, lang=None):
    lang = lang or _detect_lang(md_text)
    md_text = _apply_markers(md_text)
    # json.dumps() does not escape "/", so a literal "</script>" inside md_text
    # would prematurely close this inline <script> block in the browser. Escaping
    # "</" to "<\/" is valid JS/JSON and parses back to the identical string.
    raw_json = json.dumps(md_text).replace("</", "<\\/")
    cap_json = json.dumps(CAPTIONS.get(lang, CAPTIONS["en"]),
                          ensure_ascii=False).replace("</", "<\\/")
    return f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>{html.escape(title)}</title>
<style>{CSS}</style>
</head>
<body>
<div id="content"></div>
{_script_tag(marked_js, MARKED_CDN)}
{_script_tag(mermaid_js, MERMAID_CDN)}
<script>
const raw = {raw_json};
const CAP = {cap_json};
document.getElementById("content").innerHTML = marked.parse(raw);
document.querySelectorAll("pre code.language-mermaid").forEach(code => {{
  const div = document.createElement("div");
  div.className = "mermaid";
  div.textContent = code.textContent;
  code.closest("pre").replaceWith(div);
}});
// Table of contents from the headings, if the doc asked for one.
const toc = document.getElementById("sa-toc");
if (toc) {{
  const heads = document.querySelectorAll("#content h1, #content h2, #content h3");
  const list = document.createElement("ul");
  let n = 0;
  heads.forEach(h => {{
    if (!h.id) h.id = "sa-sec-" + (++n);
    const li = document.createElement("li");
    li.className = "sa-toc-" + h.tagName.toLowerCase();
    const a = document.createElement("a");
    a.href = "#" + h.id;
    a.textContent = h.textContent;
    li.appendChild(a);
    list.appendChild(li);
  }});
  const heading = document.createElement("div");
  heading.className = "sa-toc-title";
  heading.textContent = CAP.toc;
  toc.appendChild(heading);
  toc.appendChild(list);
}}
// Auto-number figures (Mermaid) and tables with localized captions.
let fig = 0;
document.querySelectorAll("#content .mermaid").forEach(m => {{
  const cap = document.createElement("p");
  cap.className = "sa-figcaption";
  cap.textContent = CAP.fig + " " + (++fig);
  m.parentNode.insertBefore(cap, m.nextSibling);
}});
let tbl = 0;
document.querySelectorAll("#content table").forEach(t => {{
  const cap = document.createElement("p");
  cap.className = "sa-tablecaption";
  cap.textContent = CAP.tbl + " " + (++tbl);
  t.parentNode.insertBefore(cap, t);
}});
// Render Mermaid, then flag completion so a PDF driver can wait for it
// (headless print also honours --virtual-time-budget as a hard ceiling).
mermaid.initialize({{ startOnLoad: false, theme: "neutral" }});
mermaid.run()
  .then(() => document.documentElement.setAttribute("data-sa-render", "done"))
  .catch(() => document.documentElement.setAttribute("data-sa-render", "error"));
</script>
</body>
</html>
"""


def find_browser():
    for name in ("msedge", "chrome", "chromium", "chromium-browser"):
        path = shutil.which(name)
        if path:
            return path
    for path in WINDOWS_BROWSER_PATHS:
        if pathlib.Path(path).exists():
            return path
    return None


def print_pdf(browser, html_path, pdf_path, page_numbers=False):
    # --virtual-time-budget is the ceiling the headless browser waits before
    # printing; 30s lets a diagram-heavy document finish client-side rendering
    # (build_html also flags completion via data-sa-render for CDP drivers).
    cmd = [browser, "--headless", "--disable-gpu",
           "--virtual-time-budget=30000",
           f"--print-to-pdf={pdf_path}", pathlib.Path(html_path).resolve().as_uri()]
    if not page_numbers:
        # default: no browser header/footer. --page-numbers keeps Chrome's
        # footer (page N/M) at the cost of also showing the date/URL.
        cmd.insert(3, "--no-pdf-header-footer")
    subprocess.run(cmd, check=True, capture_output=True)


def _read_optional(path):
    if path is None:
        return None
    return pathlib.Path(path).read_text(encoding="utf-8")


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("input")
    ap.add_argument("--pdf", action="store_true")
    ap.add_argument("--out-dir", default=None)
    ap.add_argument("--marked-js", default=None)
    ap.add_argument("--mermaid-js", default=None)
    ap.add_argument("--lang", choices=["th", "en"], default=None,
                    help="caption language; auto-detected from the document "
                         "when omitted")
    ap.add_argument("--page-numbers", action="store_true",
                    help="keep the browser footer (page N/M) in the PDF")
    args = ap.parse_args(argv)

    # output paths/titles may contain Thai; keep a cp1252 console from crashing.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):  # pragma: no cover
        pass

    src = pathlib.Path(args.input)
    out_dir = pathlib.Path(args.out_dir) if args.out_dir else src.parent
    out_dir.mkdir(parents=True, exist_ok=True)

    md_text = src.read_text(encoding="utf-8")
    title = src.stem
    for line in md_text.splitlines():
        if line.startswith("# "):
            title = line[2:].strip()
            break

    html_path = out_dir / f"{src.stem}.html"
    html_path.write_text(
        build_html(md_text, title,
                   _read_optional(args.marked_js),
                   _read_optional(args.mermaid_js),
                   lang=args.lang),
        encoding="utf-8")
    print(f"HTML: {html_path}")

    if args.pdf:
        browser = find_browser()
        if browser is None:
            print("No Edge/Chrome/Chromium found - open the HTML in a browser "
                  "and print to PDF (Ctrl+P).")
        else:
            pdf_path = out_dir / f"{src.stem}.pdf"
            try:
                print_pdf(browser, html_path, pdf_path, args.page_numbers)
                print(f"PDF:  {pdf_path}")
            except (subprocess.CalledProcessError, OSError) as exc:
                print(f"PDF generation failed ({exc}) - the HTML is ready at "
                      f"{html_path}; open it in a browser and print to PDF "
                      "manually (Ctrl+P).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
