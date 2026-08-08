#!/usr/bin/env python3
"""Post-assembly self-test for an assembled problem-description walkthrough (Phase 2).

There is no build step, so this static check is the safety net that catches a
splice that produced a byte-perfectly-self-contained file that is silently broken.
Run it on every assembled walkthrough before declaring done.

Checks:
  A. Self-contained — no external <script src>/<link href> (data: URIs ok).
  B. MODE is set and modeRenderers[MODE] is registered.
  C. render(step) runs RENDER_HOOKS as its FIRST statement (drawer/hooks fire before paint).
  D. If a term-drilldown drawer is present: closeDrawer defined, pushed to RENDER_HOOKS,
     GLOSSARY present, and every data-term / seeAlso key resolves (drawer composition).
  E. Scenes are clean — no createElement/appendChild and no drawer refs inside scenes[]
     (idempotency + orthogonality).
  F. Source order — engine modeRenderers declaration < pack registration < MODE assignment.

Usage: python check-walkthrough.py ASSEMBLED.html
"""
import re, sys, pathlib


def main(path):
    src = pathlib.Path(path).read_text(encoding='utf-8')
    errs = []

    # A. self-contained
    for m in re.finditer(r'<(script|link)\b[^>]*\b(src|href)\s*=\s*["\']([^"\']+)["\']', src, re.I):
        if not m.group(3).startswith('data:'):
            errs.append(f'external resource not allowed: <{m.group(1)} {m.group(2)}="{m.group(3)}">')

    # A2. no unsubstituted authoring placeholder (the assembler fills the h1/title)
    if '[INSERT' in src:
        errs.append('unsubstituted "[INSERT…]" placeholder remains — pass --title to the assembler')

    # B. MODE + renderer registration (+ the renderer satisfies the registry/clear contract)
    mm = re.search(r"\bMODE\s*=\s*'([\w-]+)'", src)
    if not mm:
        errs.append("MODE is not set (expected MODE = '<mode>')")
    else:
        rm0 = re.search(r"modeRenderers\[\s*'" + re.escape(mm.group(1)) + r"'\s*\]\s*=", src)
        if not rm0:
            errs.append(f"modeRenderers['{mm.group(1)}'] is not registered")
        else:
            window = src[rm0.start():rm0.start() + 5000]
            if 'registry' not in window or not re.search(r'\bclear\s*\(', window):
                errs.append(f"modeRenderers['{mm.group(1)}'] is missing registry/clear (renderer contract)")

    # C. render runs RENDER_HOOKS first
    if not re.search(r'function render\s*\([^)]*\)\s*\{\s*RENDER_HOOKS\b', src):
        errs.append('render() does not run RENDER_HOOKS as its first statement')

    # D. drawer composition (only if a drawer is present)
    if 'id="termDrawer"' in src:
        if 'data-term="' not in src:
            errs.append('drawer assembled but no data-term spans use it — drop --drawer or mark terms')
        if 'function closeDrawer' not in src:
            errs.append('drawer present but closeDrawer() not defined')
        if not re.search(r'RENDER_HOOKS\.push\s*\(\s*closeDrawer\s*\)', src):
            errs.append('drawer present but closeDrawer not pushed to RENDER_HOOKS')
        gm = re.search(r'const GLOSSARY\s*=\s*\{(.*?)\n\};', src, re.S)
        if not gm:
            errs.append('drawer present but GLOSSARY missing')
        else:
            keys = set(re.findall(r"^\s*'([\w-]+)'\s*:\s*\{", gm.group(1), re.M))
            for t in re.findall(r'data-term="([^"]+)"', src):
                if t not in keys:
                    errs.append(f'data-term="{t}" has no GLOSSARY entry')
            for arr in re.findall(r'seeAlso\s*:\s*\[([^\]]*)\]', gm.group(1)):
                for k in re.findall(r"'([\w-]+)'", arr):
                    if k not in keys:
                        errs.append(f'seeAlso "{k}" has no GLOSSARY entry')
    elif re.search(r'data-term="', src):
        # terms marked drillable but no drawer assembled: the .term styling and the click
        # handler both live in the drawer pack, so the spans render as inert plain text.
        errs.append('data-term spans present but no drawer assembled — pass --drawer or remove the spans')

    # E. scenes clean — no DOM-building / drawer references inside scenes (call/usage-shaped
    #    patterns, so narration text that merely mentions a name doesn't false-positive)
    sm = re.search(r'const scenes\s*=\s*\[(.*?)\];\s*TOTAL', src, re.S)
    if not sm:
        errs.append('could not locate `const scenes = [ ... ]; TOTAL`')
    else:
        scenes_body = sm.group(1)
        for pat in (r'createElement\s*\(', r'appendChild\s*\(', r'\bopenTerm\s*\(',
                    r'\bcloseDrawer\s*\(', r'\bhopTerm\s*\(', r'\bGLOSSARY\s*[\[.]',
                    r'\bdrawerStack\b', r"getElementById\(\s*['\"]termDrawer"):
            if re.search(pat, scenes_body):
                errs.append(f'scene code matches /{pat}/ — DOM-building/drawer must stay out of scenes')

        # F2. every id a scene setter targets must resolve to an id="" in the assembled DOM
        #     (catches a typo'd id that would silently no-op). setRowClass/setBadge take a
        #     logical id that maps to row<ID>/badge<ID>; all other setters use the literal id.
        dom_ids = set(re.findall(r'\bid="([^"]+)"', src))
        prefix = {'setRowClass': 'row', 'setBadge': 'badge'}
        id_setters = r"(setNode|setEdge|setComp|setArrow|setLabel|setText|setRowClass|setBadge|setCell|setRule|show|hide)"
        for setter, sid in re.findall(id_setters + r"""\s*\(\s*['"]([^'"]+)['"]""", scenes_body):
            want = prefix.get(setter, '') + sid
            if want not in dom_ids:
                errs.append(f"scene {setter}('{sid}') targets id \"{want}\" with no matching id=\"\" in the DOM (silent no-op)")

    # F. source order
    decl = src.find('const modeRenderers')
    regm = re.search(r"modeRenderers\[\s*'[\w-]+'\s*\]\s*=", src)
    reg = regm.start() if regm else -1
    setm = re.search(r"\bMODE\s*=\s*'[\w-]+'", src)
    setpos = setm.start() if setm else -1
    if decl == -1:
        errs.append('engine `const modeRenderers` declaration missing')
    elif not (decl < reg < setpos):
        errs.append('source order wrong: expect engine modeRenderers decl < pack registration < MODE assignment')

    # G. JS syntax -- node --check the assembled <script> (skip if node absent).
    #    The other checks are regex-shaped and miss real syntax errors (e.g. an
    #    apostrophe inside a single-quoted GLOSSARY string), which ship a blank page.
    import shutil, subprocess, tempfile, os
    _node = shutil.which('node')
    _sj = re.search(r'<script>(.*)</script>', src, re.S)
    if _node and _sj:
        _tf = tempfile.NamedTemporaryFile('w', suffix='.js', delete=False, encoding='utf-8')
        _tf.write(_sj.group(1)); _tf.close()
        _r = subprocess.run([_node, '--check', _tf.name], capture_output=True, text=True)
        os.unlink(_tf.name)
        if _r.returncode != 0:
            _tail = _r.stderr.strip().splitlines()
            errs.append('JS syntax error in <script> (node --check): ' + (_tail[-1] if _tail else 'see node'))

    if errs:
        for e in errs:
            print('FAIL:', e)
        sys.exit(1)
    mode = mm.group(1) if mm else '?'
    drawer = 'with drawer' if 'id="termDrawer"' in src else 'no drawer'
    print(f'OK: {pathlib.Path(path).name} — mode={mode} ({drawer}); self-contained, RENDER_HOOKS-first, scenes clean, order ok')


if __name__ == '__main__':
    main(sys.argv[1])
