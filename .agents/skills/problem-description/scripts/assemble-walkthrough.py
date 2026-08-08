#!/usr/bin/env python3
"""Assemble a self-contained problem-description walkthrough from the single-source
references (Phase 2 mode framework).

A generated walkthrough = engine § + ONE mode pack § + (optional) drawer § + a
bootstrap (authored scenes[]+GLOSSARY, or the mode pack's DEMO-BOOTSTRAP for
verification). This script does the deterministic splice the skill would otherwise
hand-perform, so the output is reproducible and the post-assembly self-test
(check-walkthrough.py) has a stable target.

Usage:
  python assemble-walkthrough.py --engine ENGINE.html --mode MODE_PACK.html \
      --out OUT.html [--drawer term-drilldown.html] [--demo | --bootstrap FILE]

  --demo        use the mode pack's DEMO-BOOTSTRAP block as the bootstrap (verification)
  --bootstrap   a file whose content is the authored bootstrap (MODE=…; alias; scenes[]; …)

Section markers (mirrors term-drilldown.html / walkthrough-engine.html):
  CSS/JS:  /* ===== §NAME ... ===== */  ...  /* ===== end §NAME ===== */
  HTML:    <!-- ===== §NAME ... ===== -->  ...  <!-- ===== end §NAME ===== -->
"""
import argparse, re, sys, pathlib


def _section(text, name, kind, src):
    if kind == 'html':
        op, cl = r'<!--\s*=====\s*§' + name + r'\b[^\n]*?-->', r'<!--\s*=====\s*end\s*§' + name + r'\b[^\n]*?-->'
    else:
        op, cl = r'/\*\s*=====\s*§' + name + r'\b[^\n]*?\*/', r'/\*\s*=====\s*end\s*§' + name + r'\b[^\n]*?\*/'
    m = re.search(op + r'(.*?)' + cl, text, re.S)
    if not m:
        sys.exit(f'ERROR: §{name} ({kind}) section not found in {src}')
    return m.group(1).strip('\n')


def _between(text, start, end, src, what):
    m = re.search(start + r'(.*?)' + end, text, re.S)
    if not m:
        sys.exit(f'ERROR: {what} not found in {src}')
    return m.group(1).strip('\n')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--engine', required=True)
    ap.add_argument('--mode', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--drawer')
    ap.add_argument('--demo', action='store_true')
    ap.add_argument('--bootstrap')
    ap.add_argument('--title', default=None)
    a = ap.parse_args()

    eng = pathlib.Path(a.engine).read_text(encoding='utf-8')
    mode = pathlib.Path(a.mode).read_text(encoding='utf-8')
    draw = pathlib.Path(a.drawer).read_text(encoding='utf-8') if a.drawer else None

    eng_css, eng_html, eng_js = _section(eng, 'CSS', 'css', a.engine), _section(eng, 'HTML', 'html', a.engine), _section(eng, 'JS', 'js', a.engine)
    mode_css, mode_html, mode_js = _section(mode, 'CSS', 'css', a.mode), _section(mode, 'HTML', 'html', a.mode), _section(mode, 'JS', 'js', a.mode)
    draw_css = draw_html = draw_js = ''
    if draw:
        draw_css = _section(draw, 'CSS', 'css', a.drawer)
        draw_html = _section(draw, 'HTML', 'html', a.drawer)
        draw_js = _section(draw, 'JS', 'js', a.drawer)

    if a.demo:
        bootstrap = _between(mode, r'/\*\s*=====\s*DEMO-BOOTSTRAP START[^\n]*?\*/', r'/\*\s*=====\s*DEMO-BOOTSTRAP END[^\n]*?\*/', a.mode, 'DEMO-BOOTSTRAP')
    elif a.bootstrap:
        bootstrap = pathlib.Path(a.bootstrap).read_text(encoding='utf-8')
    else:
        sys.exit('ERROR: pass --demo or --bootstrap FILE')

    # Process the engine §HTML: drop the DEMO-ONLY content block, insert the mode
    # pack's §HTML at the MODE CONTENT slot, and the drawer §HTML at its marker.
    html = re.sub(r'<!--\s*DEMO ONLY content.*?<!--\s*end DEMO ONLY\s*-->', '', eng_html, flags=re.S)
    # Function replacement so mode_html is treated literally (a raw replacement string would
    # interpret backslashes and \1/\g<> group refs and could corrupt a future pack's §HTML).
    html = re.sub(r'(<!--\s*=====\s*MODE CONTENT[^\n]*?-->)', lambda m: m.group(1) + '\n' + mode_html, html, count=1)
    if draw_html:
        html = html.replace('<!-- term drill-down drawer §HTML inlined here at generation -->', draw_html)

    # Fill BOTH the browser-tab <title> and the on-screen <h1>. The engine ships the h1 as a
    # placeholder; --title (falling back to the out stem) is the single source for both, so a
    # generated walkthrough never displays "[INSERT: walkthrough title]".
    title = a.title or pathlib.Path(a.out).stem
    html = html.replace('[INSERT: walkthrough title]', title)

    js_parts = [eng_js]
    if draw_js:
        js_parts.append(draw_js)
    js_parts.append(mode_js)
    js_parts.append(bootstrap)
    js = '\n\n'.join(js_parts)

    css = '\n'.join([eng_css, mode_css] + ([draw_css] if draw_css else []))

    out = (
        '<!DOCTYPE html>\n<html lang="th"><head><meta charset="UTF-8">\n'
        f'<title>{title}</title>\n<style>\n{css}\n</style></head>\n<body>\n'
        f'{html}\n<script>\n{js}\n</script>\n</body></html>\n'
    )
    pathlib.Path(a.out).write_text(out, encoding='utf-8')
    print(f'assembled -> {a.out} (engine + {pathlib.Path(a.mode).stem}' + (' + drawer' if draw else '') + ')')


if __name__ == '__main__':
    main()
