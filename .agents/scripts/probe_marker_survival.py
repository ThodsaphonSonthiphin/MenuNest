#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""decision-map phase-2 verification probe: does the key marker survive the tracker?

Settles the ONE bet the whole `key` join rests on, before any join code is written:
does `<!-- decision-map:key:<key> -->`, written into an item body, come back
byte-identical after an API round trip, a rich-text/web-UI edit, and a close/reopen?

Specified by: plugins/decision-map/references/data-contracts.md,
"Before building the join: verify it -- phase 2's first step" (six steps).
This script runs those six, plus two the contract does not ask for and should:

  1  marker round trip through the API (write, read, byte-compare)
  2  a key containing `--` (forbidden by the contract) + a conforming key
  3  edit the body in the WEB UI, then re-read          <- human, two-phase
  4  close the item and reopen it, then re-read
  5  the real join call shape end to end (ADO: WIQL -> workitemsbatch/200;
     GitHub: paginated sub-issue listing)
  6  GitHub backend availability (native sub-issues + native issue dependencies)
  7  ESCAPED-FORM round trip -- does `&lt;!-- decision-map:` survive, or does the
     tracker re-encode it (`&amp;lt;`, breaking the byte-identical no-op) or DECODE
     it back to a live marker (forging a second key marker)?   [added]
  R2 does the marker survive in a COMMENT?  Decides whether fallback rung 2 is
     even available if the body markers lose.                   [added]

Language: Python 3, stdlib only + subprocess to `gh` / `az`. Chosen because the
decision-map ops script this probe informs (`scripts/local_map_ops.py`) is Python,
so the phase-2 tracker backend that consumes this verdict is Python too.

======================================================================
SAFETY
======================================================================
* DRY RUN IS THE DEFAULT. Without `--yes` nothing is created anywhere.
* `--yes` AND an explicit `--repo` / `--org`+`--project` are both required to write.
* Everything created is titled  "[decision-map-probe] ... <run-id>"  and carries a
  self-describing body. No labels and no tags are created (the probe deliberately
  does not touch the label/tag namespace).
* Cleanup always runs, including on failure (try/finally). `cleanup` closes items;
  `--purge` additionally DELETES them (GitHub: GraphQL deleteIssue; ADO: recycle-bin
  delete). On a PUBLIC repo, prefer `--purge`.
* Re-runnable: state lives in a JSON file keyed by run id; `setup` reuses items that
  the state file already names instead of creating duplicates. Never leaves orphans.

======================================================================
USAGE (Windows: PowerShell or Git Bash; paths with spaces are fine -- quote them)
======================================================================
  # 0. see exactly what it would do; makes NO writes
  python probe_marker_survival.py --tracker github --repo OWNER/REPO --dry-run

  # 1. create the probe items and run every API-only step
  python probe_marker_survival.py --tracker github --repo OWNER/REPO --yes \
      --phase setup --out-dir "C:/scratch/probe"

  # 2. do the human step the script prints, in the browser, then:
  python probe_marker_survival.py --tracker github --repo OWNER/REPO --yes \
      --phase verify --out-dir "C:/scratch/probe"

  # 3. always
  python probe_marker_survival.py --tracker github --repo OWNER/REPO --yes \
      --phase cleanup --purge --out-dir "C:/scratch/probe"

  # non-interactive: setup + verify + cleanup in one shot, step 3 marked skipped
  python probe_marker_survival.py --tracker github --repo OWNER/REPO --yes \
      --phase auto --out-dir "C:/scratch/probe"

  # ADO (needs AZDO_ORG/AZDO_PROJECT or --org/--project, and az login or AZDO_PAT)
  python probe_marker_survival.py --tracker ado --org MyOrg --project MyProj --yes \
      --phase setup --out-dir "C:/scratch/probe"

Exit codes (same three-way split local_map_ops.py uses):
  0  the probe ran and wrote a verdict (a FAILED step is still exit 0 -- read the verdict)
  2  known, actionable failure (missing credentials, bad args, tracker refused)
  1  unhandled crash, traceback printed -- the tool is broken
"""

from __future__ import annotations

import argparse
import base64
import datetime as _dt
import io
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

# Windows console is cp1252; the probe prints U+2192 and raw marker bytes.
if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

EXIT_OK, EXIT_CRASH, EXIT_ERROR = 0, 1, 2

# ---------------------------------------------------------------------------
# Marker constants -- MUST stay byte-identical to local_map_ops.py:146/152.
# If these drift, the probe verifies something the backend does not write.
# ---------------------------------------------------------------------------
MARKER_PREFIX = "<!-- decision-map:"
MARKER_ESCAPED_PREFIX = "&lt;!-- decision-map:"

KEY_OK = "probe-key-alpha"          # conforming: [A-Za-z0-9][A-Za-z0-9_-]* and no `--`
KEY_BAD = "probe--key-beta"         # deliberately violates the no-`--` rule (step 2)
FORGED_KEY = "probe-forged-by-user" # appears only inside ESCAPED user text (step 7)

# Any decision-map marker comment, however the tracker reflows the text around it.
# Deliberately permissive: it must still match a marker the tracker has mangled,
# otherwise "mangled" is indistinguishable from "absent".
RE_ANY_MARKER = re.compile(r"<!--.{0,8}?decision-map:.*?-->", re.DOTALL)
RE_ESCAPED_MARKER = re.compile(r"&(?:amp;)*lt;!--.{0,8}?decision-map:.*?-->", re.DOTALL)
RE_KEY_MARKER = re.compile(r"<!--\s*decision-map:key:([^\s>]*)\s*-->")


class ProbeError(Exception):
    """A known, actionable failure: one line on stderr, exit 2."""


# ---------------------------------------------------------------------------
# byte-exact comparison helpers
# ---------------------------------------------------------------------------
def norm_eol(s: str) -> str:
    """CRLF -> LF. GitHub web-UI textareas submit CRLF while the API submits LF and
    both coexist in one repo, so a raw byte-compare fails on line endings alone and
    would be misread as 'the marker did not survive'. Normalise, then compare bytes.
    ADO's HTML editor reflows whitespace for the same reason."""
    return s.replace("\r\n", "\n").replace("\r", "\n")


def markers_of(text: str | None) -> list[str]:
    """Ordered list of every decision-map marker comment in `text`.

    This -- not a whole-body compare -- is the canonical survival test: it is exactly
    what the join reads, and it is immune to a rich-text editor reflowing the prose
    around the marker (which is a cosmetic change, not a broken join)."""
    return RE_ANY_MARKER.findall(norm_eol(text or ""))


def escaped_markers_of(text: str | None) -> list[str]:
    return RE_ESCAPED_MARKER.findall(norm_eol(text or ""))


def hexdump(s: str, limit: int = 240) -> str:
    b = s.encode("utf-8")[:limit]
    return " ".join(f"{x:02x}" for x in b)


def byte_compare(label: str, expected: str, actual: str) -> tuple[bool, dict]:
    """Byte-compare two strings; on mismatch, report the ACTUAL bytes, never a summary."""
    eb, ab = expected.encode("utf-8"), actual.encode("utf-8")
    if eb == ab:
        return True, {"what": label, "match": True, "value": expected}
    idx = next((i for i in range(min(len(eb), len(ab))) if eb[i] != ab[i]), min(len(eb), len(ab)))
    return False, {
        "what": label,
        "match": False,
        "firstDifferingByte": idx,
        "expected": {"repr": repr(expected), "hex": hexdump(expected), "len": len(eb)},
        "actual": {"repr": repr(actual), "hex": hexdump(actual), "len": len(ab)},
    }


def compare_marker_lists(label: str, expected: list[str], actual: list[str]) -> tuple[bool, dict]:
    if expected == actual:
        return True, {"what": label, "match": True, "markers": expected}
    ev = {"what": label, "match": False,
          "expectedCount": len(expected), "actualCount": len(actual),
          "expected": [{"repr": repr(m), "hex": hexdump(m)} for m in expected],
          "actual": [{"repr": repr(m), "hex": hexdump(m)} for m in actual]}
    missing = [m for m in expected if m not in actual]
    extra = [m for m in actual if m not in expected]
    if missing:
        ev["missing"] = [repr(m) for m in missing]
    if extra:
        ev["unexpectedlyPresent"] = [repr(m) for m in extra]
    return False, ev


# ---------------------------------------------------------------------------
# probe bodies
# ---------------------------------------------------------------------------
def map_body(run_id: str, tracker: str) -> str:
    lines = [
        f"decision-map phase-2 marker-survival PROBE -- run {run_id}.",
        "Created by scripts/probe_marker_survival.py. It will be closed or deleted by",
        "the same script. Safe to ignore. If you were asked to edit it, follow the",
        "instructions the script printed and change ONLY the prose, never the",
        "<!-- decision-map:... --> comments.",
        "",
        "<!-- decision-map:fog:start -->",
        "- probe fog line",
        "<!-- decision-map:fog:end -->",
        "<!-- decision-map:scope:start -->",
        "- probe scope line",
        "<!-- decision-map:scope:end -->",
    ]
    return to_tracker_text("\n".join(lines), tracker)


def ticket_body(run_id: str, key: str, tracker: str) -> str:
    """One ticket body carrying: the tool-owned key marker, a gist region, and -- as
    ordinary USER text -- the ESCAPED form. If the tracker decodes that entity, the
    body now holds TWO key markers, which the contract calls a hard error: step 7
    demonstrates the collapse directly rather than arguing about it."""
    lines = [
        f"<!-- decision-map:key:{key} -->",
        f"decision-map phase-2 marker-survival PROBE -- run {run_id}, key `{key}`.",
        "Created by a script; it will be closed or deleted. Do not edit unless asked.",
        "",
        "<!-- decision-map:gist:start -->",
        "probe gist line -- one flattened line, as frontmatter would hold it",
        "<!-- decision-map:gist:end -->",
        "",
        "User-supplied text under test (must survive byte-identical, still escaped):",
        f"{MARKER_ESCAPED_PREFIX}key:{FORGED_KEY} -->",
    ]
    return to_tracker_text("\n".join(lines), tracker)


def to_tracker_text(plain: str, tracker: str) -> str:
    """GitHub bodies are Markdown -> send as-is. ADO System.Description is an HTML
    field -> wrap each line in <div> so the Boards editor round-trips something it
    recognises. Marker comments are left on their own line either way."""
    if tracker != "ado":
        return plain
    out = []
    for ln in plain.split("\n"):
        out.append(ln if ln.startswith("<!--") else (f"<div>{ln}</div>" if ln else "<div><br></div>"))
    return "\n".join(out)


COMMENT_MANIFEST = (
    "decision-map probe -- fallback rung 2 carrier test.\n"
    "<!-- decision-map:manifest:start -->\n"
    f"{KEY_OK}=<id>\n"
    "<!-- decision-map:manifest:end -->"
)


# ---------------------------------------------------------------------------
# tracker adapters
# ---------------------------------------------------------------------------
class Tracker:
    name = "?"

    def __init__(self, args):
        self.args = args
        self.write_delay = args.write_delay
        self.calls = 0

    def _throttle(self):
        """GitHub's secondary limit is 80 content-generating requests/minute; ADO
        throttles on TSTUs. The probe issues ~12 writes so it is nowhere near either,
        but phase-2 `chart` issues ~3n and WILL trip it past ~26 tickets, so the
        throttle lives here where it gets exercised first."""
        if self.write_delay:
            time.sleep(self.write_delay)

    # --- interface ---------------------------------------------------------
    def preflight(self) -> dict: raise NotImplementedError
    def create_item(self, title, body, kind) -> dict: raise NotImplementedError
    def get_body(self, item) -> str | None: raise NotImplementedError
    def set_body(self, item, body) -> None: raise NotImplementedError
    def close_item(self, item) -> None: raise NotImplementedError
    def reopen_item(self, item) -> None: raise NotImplementedError
    def read_state(self, item) -> str: raise NotImplementedError
    def link_child(self, parent, child) -> None: raise NotImplementedError
    def link_blocked_by(self, item, blocker) -> dict: raise NotImplementedError
    def join_pass(self, parent) -> dict: raise NotImplementedError
    def add_comment(self, item, text) -> dict: raise NotImplementedError
    def get_comments(self, item) -> list[dict]: raise NotImplementedError
    def dispose(self, item, purge: bool) -> str: raise NotImplementedError
    def web_edit_instructions(self, item) -> list[str]: raise NotImplementedError


# ------------------------------- GitHub ------------------------------------
class GitHubTracker(Tracker):
    name = "github"
    API_VERSION = "2026-03-10"  # pinned for determinism; NOT a gating prerequisite --
                                # both endpoint families serve under the default 2022-11-28.

    def __init__(self, args):
        super().__init__(args)
        if not args.repo or "/" not in args.repo:
            raise ProbeError("github needs --repo OWNER/REPO (explicit, never inferred "
                             "from the current git remote -- this script writes to it)")
        self.repo = args.repo

    # gh is driven with --input - so every JSON payload travels on stdin. PowerShell
    # 5.1 mangles native-exe args containing quotes; stdin sidesteps that entirely.
    def gh(self, path, method=None, payload=None, graphql=False, allow_fail=False,
           versioned=True, retries=3):
        cmd = ["gh", "api"]
        if versioned and not graphql:
            cmd += ["-H", f"X-GitHub-Api-Version: {self.API_VERSION}"]
        if method:
            cmd += ["--method", method]
        cmd.append("graphql" if graphql else path)
        if payload is not None:
            cmd += ["--input", "-"]
        data = json.dumps(payload) if payload is not None else None
        for attempt in range(retries):
            self.calls += 1
            p = subprocess.run(cmd, input=data, capture_output=True, text=True,
                               encoding="utf-8", errors="replace", timeout=120)
            if p.returncode == 0:
                break
            err = (p.stderr or "") + (p.stdout or "")
            if ("secondary rate limit" in err.lower() or "rate limit" in err.lower()) \
                    and attempt < retries - 1:
                wait = 60 * (attempt + 1)
                print(f"  rate limited; sleeping {wait}s then retrying", file=sys.stderr)
                time.sleep(wait)
                continue
            if allow_fail:
                return {"__error__": err.strip(), "__status__": p.returncode}
            raise ProbeError(f"gh api {' '.join(cmd[2:])} failed: {err.strip()[:800]}")
        try:
            return json.loads(p.stdout) if p.stdout.strip() else {}
        except json.JSONDecodeError:
            return {"__raw__": p.stdout}

    def preflight(self):
        v = subprocess.run(["gh", "--version"], capture_output=True, text=True)
        if v.returncode != 0:
            raise ProbeError("`gh` is not on PATH -- install GitHub CLI or use --tracker ado")
        ver = (v.stdout or "").splitlines()[0] if v.stdout else "?"
        st = subprocess.run(["gh", "auth", "status"], capture_output=True, text=True)
        if st.returncode != 0:
            raise ProbeError("`gh auth status` failed -- run `gh auth login` first")
        repo = self.gh(f"repos/{self.repo}")
        rate = self.gh("rate_limit")
        return {
            "ghVersion": ver,
            # gh < 2.94.0 has no --parent/--blocked-by/--json blockedBy; phase 2 must
            # go through `gh api` (as this probe does) or pin gh >= 2.94.0.
            "ghHasFirstClassSubIssueFlags": _ver_ge(ver, (2, 94, 0)),
            "repoPrivate": repo.get("private"),
            "repoPermissions": repo.get("permissions"),
            "ownerType": (repo.get("owner") or {}).get("type"),
            "coreRateRemaining": ((rate.get("resources") or {}).get("core") or {}).get("remaining"),
        }

    def create_item(self, title, body, kind):
        r = self.gh(f"repos/{self.repo}/issues", "POST", {"title": title, "body": body})
        self._throttle()
        # Mutations are keyed on the DATABASE id, not the issue number: cli/cli#12439
        # has number 12439 and id 3789102272. Cache both -- resolving one from the
        # other later costs an extra GET per edge.
        return {"kind": kind, "number": r["number"], "id": r["id"],
                "nodeId": r["node_id"], "url": r["html_url"]}

    def _issue(self, item):
        return self.gh(f"repos/{self.repo}/issues/{item['number']}")

    def get_body(self, item):
        # `body` can be JSON null on a real issue, not just "". Any direct regex on it
        # throws -- the join must treat null as "no marker", which for a labelled child
        # is the contract's LOUD ERROR, not a silent skip.
        return self._issue(item).get("body")

    def set_body(self, item, body):
        self.gh(f"repos/{self.repo}/issues/{item['number']}", "PATCH", {"body": body})
        self._throttle()

    def close_item(self, item):
        self.gh(f"repos/{self.repo}/issues/{item['number']}", "PATCH",
                {"state": "closed", "state_reason": "not_planned"})
        self._throttle()

    def reopen_item(self, item):
        self.gh(f"repos/{self.repo}/issues/{item['number']}", "PATCH", {"state": "open"})
        self._throttle()

    def read_state(self, item):
        i = self._issue(item)
        # Read `state` ALONE. state_reason is nullable on closed issues and its enum
        # (COMPLETED/NOT_PLANNED/DUPLICATE/REOPENED) is open to extension.
        return i.get("state")

    def link_child(self, parent, child):
        self.gh(f"repos/{self.repo}/issues/{parent['number']}/sub_issues", "POST",
                {"sub_issue_id": child["id"]})
        self._throttle()

    def link_blocked_by(self, item, blocker):
        r = self.gh(f"repos/{self.repo}/issues/{item['number']}/dependencies/blocked_by",
                    "POST", {"issue_id": blocker["id"]}, allow_fail=True)
        self._throttle()
        return r

    def join_pass(self, parent):
        """The whole join, as phase 2 would issue it: ONE paginated enumeration.
        The sub-issue listing returns full Issue objects INCLUDING body, so there is
        no follow-up per-child body fetch."""
        children, page, per_page = [], 1, 100
        while True:
            batch = self.gh(f"repos/{self.repo}/issues/{parent['number']}"
                            f"/sub_issues?per_page={per_page}&page={page}")
            if not isinstance(batch, list) or not batch:
                break
            children += batch
            if len(batch) < per_page:
                break
            page += 1
            if page > 20:
                break
        summary = self._issue(parent).get("sub_issues_summary")
        return {
            "calls": page,
            "childCount": len(children),
            "bodiesArrivedInListing": all("body" in c for c in children) if children else None,
            "nullBodies": [c["number"] for c in children if c.get("body") is None],
            "subIssuesSummary": summary,
            "children": [{"number": c["number"], "id": c["id"], "state": c.get("state"),
                          "stateReason": c.get("state_reason"),
                          "markers": markers_of(c.get("body"))} for c in children],
        }

    def add_comment(self, item, text):
        r = self.gh(f"repos/{self.repo}/issues/{item['number']}/comments", "POST", {"body": text})
        self._throttle()
        return {"id": r.get("id"), "url": r.get("html_url")}

    def get_comments(self, item):
        r = self.gh(f"repos/{self.repo}/issues/{item['number']}/comments?per_page=100")
        return [{"id": c.get("id"), "text": c.get("body")} for c in (r if isinstance(r, list) else [])]

    def capability_report(self):
        """Step 6, restated: the contract's 'check sub-issue API availability, and
        exercise the task-list fallback if unavailable' is obsolete -- sub-issues went
        GA 2025-04-09 and native issue dependencies GA 2025-08-21. So assert both are
        live and record the limits, instead of exercising a dead fallback."""
        out = {}
        for label, path in (("subIssues", "sub_issues"),
                            ("blockedBy", "dependencies/blocked_by"),
                            ("blocking", "dependencies/blocking")):
            r = self.gh(f"repos/{self.repo}/issues/1/{path}", allow_fail=True)
            out[label + "Endpoint"] = "error" if isinstance(r, dict) and "__error__" in r else "ok"
        out["documentedLimits"] = {
            "subIssuesPerParent": 100,       # hard ceiling on decision-map map size
            "nestingLevels": 8,
            "dependenciesPerRelationshipType": 50,
            "secondaryWriteLimitPerMinute": 80,
        }
        out["taskListFallbackNeeded"] = False
        return out

    def dispose(self, item, purge):
        if purge:
            r = self.gh(None, graphql=True, payload={
                "query": "mutation($id:ID!){ deleteIssue(input:{issueId:$id}){ clientMutationId } }",
                "variables": {"id": item["nodeId"]}}, allow_fail=True)
            self._throttle()
            if isinstance(r, dict) and "__error__" not in r and not r.get("errors"):
                return "deleted"
            return f"delete-failed-then-closed: {str(r)[:200]}"
        self.gh(f"repos/{self.repo}/issues/{item['number']}", "PATCH",
                {"state": "closed", "state_reason": "not_planned"}, allow_fail=True)
        self._throttle()
        return "closed"

    def web_edit_instructions(self, item):
        return [
            f"1. Open {item['url']}",
            "2. Click the '...' menu on the issue body -> Edit.",
            "3. Add ONE new line of ordinary prose (e.g. 'edited by hand') at the END.",
            "4. Do NOT touch any <!-- decision-map:... --> line and do NOT touch the",
            "   '&lt;!-- decision-map:key:...' line -- those are what is under test.",
            "5. Click 'Update comment'/'Save'. The browser textarea submits CRLF; that",
            "   is expected and the probe normalises it before comparing.",
        ]


def _ver_ge(version_line: str, want: tuple) -> bool:
    m = re.search(r"(\d+)\.(\d+)\.(\d+)", version_line or "")
    return bool(m) and tuple(int(x) for x in m.groups()) >= want


# --------------------------------- ADO -------------------------------------
class AdoTracker(Tracker):
    name = "ado"
    API = "7.1"
    ADO_RESOURCE = "499b84ac-1321-427f-aa17-267ca6975798"

    def __init__(self, args):
        super().__init__(args)
        self.org = args.org or os.environ.get("AZDO_ORG")
        self.project = args.project or os.environ.get("AZDO_PROJECT")
        if not self.org or not self.project:
            raise ProbeError(
                "ado needs an org and a project: pass --org/--project or set "
                "AZDO_ORG/AZDO_PROJECT. Both are BARE NAMES (e.g. Cartagena365, "
                "GlassHull) -- not URLs, and not the Azure subscription or tenant.")
        self.map_type = args.ado_map_type
        # `Task` exists in ALL FOUR default processes. `Issue` does not -- Scrum has
        # Impediment instead -- so the probe never defaults to a type that fails on Scrum.
        self.ticket_type = args.ado_ticket_type
        self._token = None
        self._states_cache = {}

    def _auth_header(self):
        if self._token:
            return self._token
        pat = os.environ.get("AZDO_PAT")
        if pat:
            self._token = "Basic " + base64.b64encode(f":{pat}".encode()).decode()
            return self._token
        p = subprocess.run(["az", "account", "get-access-token", "--resource", self.ADO_RESOURCE,
                            "-o", "json"], capture_output=True, text=True)
        if p.returncode != 0:
            raise ProbeError(
                "no Azure DevOps credential: set AZDO_PAT, or `az login` with an account "
                f"that can reach org '{self.org}'. `az account get-access-token` said: "
                f"{(p.stderr or '').strip()[:300]}")
        self._token = "Bearer " + json.loads(p.stdout)["accessToken"]
        return self._token

    def req(self, path, method="GET", payload=None, ctype="application/json",
            api=None, project_scoped=True, allow_fail=False):
        base = f"https://dev.azure.com/{self.org}/"
        if project_scoped:
            base += f"{urllib.parse.quote(self.project)}/"
        sep = "&" if "?" in path else "?"
        url = f"{base}_apis/{path}{sep}api-version={api or self.API}"
        data = json.dumps(payload).encode("utf-8") if payload is not None else None
        r = urllib.request.Request(url, data=data, method=method)
        r.add_header("Authorization", self._auth_header())
        r.add_header("Accept", "application/json")
        if data:
            r.add_header("Content-Type", ctype)
        self.calls += 1
        try:
            with urllib.request.urlopen(r, timeout=120) as resp:
                raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw.strip() else {}
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", "replace")[:800]
            if allow_fail:
                return {"__error__": body, "__status__": e.code}
            if e.code in (401, 403):
                raise ProbeError(
                    f"Azure DevOps returned {e.code} for {url}. The token is valid but not "
                    f"for org '{self.org}' -- `az` is often logged into a personal tenant "
                    f"with no ADO org. Use AZDO_PAT, or `az login --tenant <work-tenant>`.")
            raise ProbeError(f"ADO {method} {url} -> HTTP {e.code}: {body}")
        except urllib.error.URLError as e:
            raise ProbeError(f"ADO {method} {url} unreachable: {e}")

    def preflight(self):
        proj = self.req(f"projects/{urllib.parse.quote(self.project)}", project_scoped=False)
        rel = self.req("wit/workitemrelationtypes", project_scoped=False)
        # Learn contradicts itself on link-type spelling (Dependency-Predecessor vs
        # Dependency-Reverse). Only the org's own referenceName strings are safe to use.
        names = sorted(x.get("referenceName", "") for x in rel.get("value", []))
        return {"projectId": proj.get("id"),
                "processTemplate": (proj.get("capabilities") or {}).get("processTemplate"),
                "relationTypeReferenceNames": names,
                "completedStates": {t: self._completed_state(t)
                                    for t in (self.map_type, self.ticket_type)}}

    def _completed_state(self, wit_type):
        """Resolve the close target by state CATEGORY, never by the literal name.
        Names differ per process (Basic/Scrum: Done; Agile/CMMI: Closed) and a
        customised process can add its own -- which is exactly what the contract's own
        read rule warns against, yet its write rule does by name."""
        if wit_type in self._states_cache:
            return self._states_cache[wit_type]
        r = self.req(f"wit/workitemtypes/{urllib.parse.quote(wit_type)}/states", allow_fail=True)
        got = None
        for s in (r.get("value") or []):
            if s.get("category") == "Completed":
                got = s.get("name")
                break
        self._states_cache[wit_type] = got
        return got

    def create_item(self, title, body, kind):
        wit = self.map_type if kind == "map" else self.ticket_type
        r = self.req(f"wit/workitems/${urllib.parse.quote(wit)}", "POST",
                     [{"op": "add", "path": "/fields/System.Title", "value": title},
                      {"op": "add", "path": "/fields/System.Description", "value": body}],
                     ctype="application/json-patch+json")
        self._throttle()
        return {"kind": kind, "number": r["id"], "id": r["id"], "rev": r.get("rev"),
                "type": wit, "apiUrl": r["url"],
                "url": f"https://dev.azure.com/{self.org}/{urllib.parse.quote(self.project)}"
                       f"/_workitems/edit/{r['id']}"}

    def _wi(self, item, expand=None):
        q = f"wit/workitems/{item['number']}"
        if expand:
            q += f"?$expand={expand}"
        return self.req(q)

    def get_body(self, item):
        return (self._wi(item).get("fields") or {}).get("System.Description")

    def set_body(self, item, body):
        rev = self._wi(item).get("rev")
        # `op: test` on /rev is optimistic concurrency: without it a read-modify-write
        # of the description silently clobbers a concurrent human edit.
        self.req(f"wit/workitems/{item['number']}", "PATCH",
                 [{"op": "test", "path": "/rev", "value": rev},
                  {"op": "replace", "path": "/fields/System.Description", "value": body}],
                 ctype="application/json-patch+json")
        self._throttle()

    def _set_state(self, item, state):
        self.req(f"wit/workitems/{item['number']}", "PATCH",
                 [{"op": "replace", "path": "/fields/System.State", "value": state}],
                 ctype="application/json-patch+json")
        self._throttle()

    def close_item(self, item):
        target = self._completed_state(item.get("type", self.ticket_type)) or "Done"
        self._set_state(item, target)

    def reopen_item(self, item):
        r = self.req(f"wit/workitemtypes/{urllib.parse.quote(item.get('type', self.ticket_type))}"
                     f"/states", allow_fail=True)
        first = next((s["name"] for s in (r.get("value") or [])
                      if s.get("category") == "Proposed"), None)
        self._set_state(item, first or "New")

    def read_state(self, item):
        return (self._wi(item).get("fields") or {}).get("System.State")

    def link_child(self, parent, child):
        self.req(f"wit/workitems/{child['number']}", "PATCH",
                 [{"op": "add", "path": "/relations/-",
                   "value": {"rel": "System.LinkTypes.Hierarchy-Reverse", "url": parent["apiUrl"]}}],
                 ctype="application/json-patch+json")
        self._throttle()

    def link_blocked_by(self, item, blocker):
        # ADO has no 'blocked by'. Predecessor (Dependency-Reverse) on the blocked item,
        # target = the blocker, is the exact semantic equivalent.
        r = self.req(f"wit/workitems/{item['number']}", "PATCH",
                     [{"op": "add", "path": "/relations/-",
                       "value": {"rel": "System.LinkTypes.Dependency-Reverse",
                                 "url": blocker["apiUrl"]}}],
                     ctype="application/json-patch+json", allow_fail=True)
        self._throttle()
        return {"ok": "__error__" not in r}

    def join_pass(self, parent):
        """The real call shape, end to end: WIQL for ids, then workitemsbatch in pages
        of 200 -- asking for fields AND $expand=relations in the SAME call, which Learn
        never states is legal. If they turn out to be mutually exclusive the join costs
        two passes per page and the contract's O(n/200)+1 estimate is wrong."""
        wiql = ("SELECT [System.Id] FROM workitemLinks "
                f"WHERE ([Source].[System.Id] = {parent['number']}) "
                "AND ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward') "
                # MustContain (the default) returns DIRECT children only. Recursive would
                # pull grandchildren into the join and break 'parentage is authoritative'.
                "MODE (MustContain)")
        q = self.req("wit/wiql", "POST", {"query": wiql})
        rels = q.get("workItemRelations") or []
        root_rows = [r for r in rels if not r.get("source")]
        child_ids = [r["target"]["id"] for r in rels if r.get("source") and r.get("target")]
        batch = self.req("wit/workitemsbatch", "POST", {
            "ids": child_ids[:200],
            "fields": ["System.Id", "System.Title", "System.State",
                       "System.AssignedTo", "System.Description"],
            "$expand": "relations",
            # `omit`, not `fail`: with fail a single deleted/inaccessible child kills the
            # whole page and the join dies on a map someone tidied up.
            "errorPolicy": "omit",
        }, allow_fail=True) if child_ids else {"value": []}
        fields_and_expand_ok = None
        vals = batch.get("value") or []
        if vals:
            fields_and_expand_ok = all(("fields" in v) for v in vals) and any("relations" in v for v in vals)
        # Is multilineFieldsFormat visible anywhere? It is absent from the documented
        # REST 7.1 WorkItem, so the backend may be unable to tell HTML from Markdown.
        wi71 = self._wi(parent)
        wi72 = self.req(f"wit/workitems/{parent['number']}", api="7.2-preview.3", allow_fail=True)
        return {
            "wiqlQuery": wiql,
            "queryResultType": q.get("queryResultType"),
            "workItemRelationsRowCount": len(rels),
            "rootRowShape": root_rows[:1],   # undocumented; pin it in a fixture
            "childIds": child_ids,
            "batchReturned": len(vals),
            "fieldsAndExpandRelationsBothHonoured": fields_and_expand_ok,
            "batchError": batch.get("__error__"),
            "multilineFieldsFormatVisibleAt71": "multilineFieldsFormat" in json.dumps(wi71),
            "multilineFieldsFormatVisibleAt72Preview": "multilineFieldsFormat" in json.dumps(wi72),
        }

    def add_comment(self, item, text):
        # 7.1-preview.4 -- still PREVIEW, unlike every other call here. A preview
        # contract can change shape without an api-version bump, which is worth knowing
        # before rung 2 (a tool-owned comment) is adopted.
        r = self.req(f"wit/workItems/{item['number']}/comments", "POST", {"text": text},
                     api="7.1-preview.4")
        self._throttle()
        return {"id": r.get("id"), "format": r.get("format")}

    def get_comments(self, item):
        r = self.req(f"wit/workItems/{item['number']}/comments", api="7.1-preview.4")
        # Read `text` (the raw source), never `renderedText` (a lossy re-render).
        return [{"id": c.get("id"), "text": c.get("text"), "format": c.get("format")}
                for c in (r.get("comments") or [])]

    def capability_report(self):
        return {"note": "step 6 is GitHub-only; on ADO it is not applicable",
                "applicable": False}

    def dispose(self, item, purge):
        path = f"wit/workitems/{item['number']}"
        if purge:
            path += "?destroy=true"
        r = self.req(path, "DELETE", allow_fail=True)
        self._throttle()
        if "__error__" in r:
            return f"delete-failed: {r['__error__'][:200]}"
        return "destroyed" if purge else "recycle-bin"

    def web_edit_instructions(self, item):
        return [
            f"1. Open {item['url']} in the Azure Boards WEB UI (not Visual Studio).",
            "2. Click into the Description field so the RICH-TEXT editor is active.",
            "3. Add ONE new line of ordinary prose at the END, then click Save.",
            "4. Do NOT touch anything that looks like <!-- decision-map:... -->; the",
            "   markers are invisible in the editor, which is precisely the risk -- the",
            "   editor may rewrite them without you seeing anything.",
            "5. THEN, if your project has the Markdown editor for multiline fields,",
            "   switch Description to Markdown and save again, and re-run verify: one",
            "   human edit can flip the field format under the backend's feet.",
        ]


# ---------------------------------------------------------------------------
# state file
# ---------------------------------------------------------------------------
def state_path(args):
    return os.path.join(args.out_dir, f"probe-state-{args.tracker}.json")


def verdict_path(args):
    return os.path.join(args.out_dir, f"probe-verdict-{args.tracker}.json")


def load_state(args):
    p = state_path(args)
    if os.path.exists(p) and not args.fresh:
        with open(p, "r", encoding="utf-8") as f:
            return json.load(f)
    return None


def save_state(args, st):
    os.makedirs(args.out_dir, exist_ok=True)
    with open(state_path(args), "w", encoding="utf-8", newline="\n") as f:
        json.dump(st, f, indent=2, ensure_ascii=False)


# ---------------------------------------------------------------------------
# steps
# ---------------------------------------------------------------------------
def step(status, detail, **evidence):
    return {"status": status, "detail": detail, "evidence": evidence or None}


def do_setup(tk: Tracker, args, st: dict) -> dict:
    run_id = st["runId"]
    prefix = f"[decision-map-probe] {run_id}"
    steps = st.setdefault("steps", {})

    if not st.get("items"):
        items = {}
        items["map"] = tk.create_item(f"{prefix} map (delete me)",
                                      map_body(run_id, tk.name), "map")
        items["ok"] = tk.create_item(f"{prefix} ticket {KEY_OK} (delete me)",
                                     ticket_body(run_id, KEY_OK, tk.name), "ticket")
        items["bad"] = tk.create_item(f"{prefix} ticket {KEY_BAD} (delete me)",
                                      ticket_body(run_id, KEY_BAD, tk.name), "ticket")
        st["items"] = items
        save_state(args, st)   # persist BEFORE any further call, so a crash still cleans up
    items = st["items"]

    # --- step 1: API round trip -------------------------------------------
    expected = {}
    ev1 = {}
    ok1 = True
    for tag, key in (("map", None), ("ok", KEY_OK), ("bad", KEY_BAD)):
        want = markers_of(ticket_body(run_id, key, tk.name) if key else map_body(run_id, tk.name))
        got_body = tk.get_body(items[tag])
        got = markers_of(got_body)
        expected[tag] = {"markers": want, "escaped": escaped_markers_of(
            ticket_body(run_id, key, tk.name) if key else map_body(run_id, tk.name))}
        good, e = compare_marker_lists(f"{tag}: markers after API write+read", want, got)
        ev1[tag] = e
        ev1[tag]["bodyWasNull"] = got_body is None
        ok1 = ok1 and good
    st["expected"] = expected
    steps["1"] = step("pass" if ok1 else "fail",
                      "marker round trip through the API (write, read back, byte-compare "
                      "every decision-map marker comment after CRLF normalisation)", **ev1)

    # --- step 2: `--` in the key ------------------------------------------
    want_bad = f"<!-- decision-map:key:{KEY_BAD} -->"
    got_bad = markers_of(tk.get_body(items["bad"]))
    bad_survived = want_bad in got_bad
    want_ok = f"<!-- decision-map:key:{KEY_OK} -->"
    ok_survived = want_ok in markers_of(tk.get_body(items["ok"]))
    steps["2"] = step(
        "pass" if ok_survived else "fail",
        "a conforming key must survive; a key containing `--` is observed, not required "
        "to survive (the contract rejects it at chart time either way)",
        conformingKey=KEY_OK, conformingKeySurvivedApi=ok_survived,
        doubleHyphenKey=KEY_BAD, doubleHyphenSurvivedApi=bad_survived,
        markersSeenOnDoubleHyphenItem=[repr(m) for m in got_bad],
        interpretation=("the API stores `--` verbatim, so the no-`--` rule is defensive "
                        "against downstream renderers, not the API -- keep the rule, but "
                        "the contract's justification should say so"
                        if bad_survived else
                        "the tracker mangled `--`: the contract's no-`--` rule is load-bearing "
                        "and empirically necessary"))

    # --- step 7 (added): the ESCAPED form ---------------------------------
    steps["7"] = check_escaped(tk, items["ok"], "after the API round trip")

    # --- step 5/6: join shape + capability --------------------------------
    tk.link_child(items["map"], items["ok"])
    tk.link_child(items["map"], items["bad"])
    edge = tk.link_blocked_by(items["bad"], items["ok"])
    join = tk.join_pass(items["map"])
    joined_keys = sorted(k for c in join.get("children", [])
                         for m in c["markers"] for k in RE_KEY_MARKER.findall(m))
    join_ok = set(joined_keys) >= {KEY_OK} and join.get("childCount", 0) >= 2
    steps["5"] = step("pass" if join_ok else "fail",
                      "the real join call shape end to end -- enumerate the map's children "
                      "ONCE and recover every key from the same pass",
                      keysRecovered=joined_keys, blockingEdge=edge, **join)
    cap = tk.capability_report()
    steps["6"] = step("skipped" if cap.get("applicable") is False else
                      ("pass" if all(v == "ok" for k, v in cap.items() if k.endswith("Endpoint"))
                       else "fail"),
                      "GitHub backend availability: native sub-issues + native issue "
                      "dependencies (the contract's task-list fallback is obsolete)", **cap)

    # --- R2 (added): can a COMMENT carry the marker? ----------------------
    if not st.get("commentProbe"):
        st["commentProbe"] = tk.add_comment(items["map"], COMMENT_MANIFEST)
        save_state(args, st)
    cmts = tk.get_comments(items["map"])
    mine = [c for c in cmts if "decision-map:manifest" in (c.get("text") or "")]
    want_c = markers_of(COMMENT_MANIFEST)
    got_c = markers_of(mine[0]["text"]) if mine else []
    good_c, ev_c = compare_marker_lists("comment markers", want_c, got_c)
    st.setdefault("steps", {})["R2"] = step(
        "pass" if good_c else "fail",
        "fallback rung 2 carrier: does the marker survive inside a tool-owned comment?",
        commentFormat=(mine[0].get("format") if mine else None), **ev_c)

    steps["3"] = step("skipped", "requires a human to edit the body in the web UI; "
                                 "run --phase verify afterwards")
    steps["4"] = step("skipped", "runs in --phase verify (after the web edit, so the two "
                                 "round trips compose the way a real map does)")
    save_state(args, st)
    return st


def check_escaped(tk: Tracker, item, when: str) -> dict:
    """Step 7. The escape the local backend applies -- `<!-- decision-map:` ->
    `&lt;!-- decision-map:` -- produces a LIVE HTML entity. Two distinct ways a tracker
    breaks it, and they are not equally bad."""
    want = f"{MARKER_ESCAPED_PREFIX}key:{FORGED_KEY} -->"
    body = norm_eol(tk.get_body(item) or "")
    intact = want in body
    reencoded = ("&amp;lt;!-- decision-map:" in body)
    decoded = bool(re.search(rf"<!--\s*decision-map:key:{re.escape(FORGED_KEY)}\s*-->", body))
    if intact and not reencoded and not decoded:
        return step("pass", f"escaped user text survived byte-identical {when}",
                    expected=repr(want), reEncoded=False, decoded=False)
    if decoded:
        return step("fail", f"CRITICAL {when}: the tracker DECODED `&lt;` back to `<`, so "
                            "ordinary user text is now a live key marker. The item holds two "
                            "key markers -- the contract's own hard error. The escape cannot "
                            "protect the join on this tracker.",
                    expected=repr(want), decoded=True,
                    markersNowPresent=[repr(m) for m in markers_of(body)])
    if reencoded:
        return step("fail", f"{when}: the tracker RE-ENCODED the escape to `&amp;lt;`. The "
                            "marker is still safe, but the body is no longer byte-identical "
                            "across a read-modify-write, so `chart`'s no-op guarantee drifts "
                            "on every run.",
                    expected=repr(want), reEncoded=True,
                    escapedFormsFound=[repr(m) for m in escaped_markers_of(body)])
    return step("fail", f"{when}: the escaped user text is gone entirely",
                expected=repr(want), bodyExcerpt=repr(body[:400]))


def do_verify(tk: Tracker, args, st: dict) -> dict:
    if not st.get("items") or not st.get("expected"):
        raise ProbeError(f"nothing to verify: no probe items in {state_path(args)}. "
                         "Run --phase setup first (it creates the items and records the "
                         "expected marker bytes that verify compares against).")
    items, steps, expected = st["items"], st["steps"], st["expected"]

    # --- step 3: after the human web-UI edit ------------------------------
    ev3, ok3 = {}, True
    for tag in ("map", "ok"):
        got = markers_of(tk.get_body(items[tag]))
        good, e = compare_marker_lists(f"{tag}: markers after the web-UI edit",
                                       expected[tag]["markers"], got)
        ev3[tag] = e
        ok3 = ok3 and good
    edited = st.get("webEditConfirmed") or args.assume_edited
    steps["3"] = step("pass" if (ok3 and edited) else ("fail" if not ok3 else "skipped"),
                      "the deciding test: the rich-text / web editor, not the API, is what "
                      "rewrites bodies" + ("" if edited else
                      " -- NOT CONFIRMED: pass --assume-edited only once a human really "
                      "did edit it, otherwise this proves nothing"),
                      humanEditConfirmed=bool(edited), **ev3)
    steps["7_afterWebEdit"] = check_escaped(tk, items["ok"], "after the web-UI edit")

    # --- step 4: close then reopen ----------------------------------------
    tk.close_item(items["ok"])
    closed_state = tk.read_state(items["ok"])
    tk.reopen_item(items["ok"])
    reopened_state = tk.read_state(items["ok"])
    got4 = markers_of(tk.get_body(items["ok"]))
    good4, e4 = compare_marker_lists("markers after close + reopen",
                                     expected["ok"]["markers"], got4)
    steps["4"] = step("pass" if good4 else "fail",
                      "close the item and reopen it, then re-read the marker",
                      stateWhenClosed=closed_state, stateWhenReopened=reopened_state, **e4)

    # re-run the join now that state has churned
    steps["5"]["evidence"]["rerunAfterCloseReopen"] = tk.join_pass(items["map"])
    save_state(args, st)
    return st


def do_cleanup(tk: Tracker, args, st: dict) -> dict:
    out = {}
    if not st.get("items"):
        print(f"cleanup: no probe items recorded in {state_path(args)} -- nothing to remove. "
              "If you believe items were created, search the tracker for the title prefix "
              "'[decision-map-probe]' and remove them by hand.", file=sys.stderr)
    for tag, item in (st.get("items") or {}).items():
        try:
            out[tag] = {"id": item.get("number"), "result": tk.dispose(item, args.purge)}
        except Exception as exc:                       # cleanup must never mask itself
            out[tag] = {"id": item.get("number"), "result": f"ERROR {exc}"}
    st["cleanup"] = out
    st["cleanedUp"] = all(str(v["result"]).startswith(("deleted", "closed", "destroyed",
                                                       "recycle-bin")) for v in out.values())
    save_state(args, st)
    return st


# ---------------------------------------------------------------------------
# verdict
# ---------------------------------------------------------------------------
RUNGS = {
    0: "no fallback needed -- the per-ticket key marker in the item body holds",
    1: "key->id manifest in a marker region on the MAP item's body",
    2: "a tool-owned COMMENT on the map item holding the same manifest",
    3: "a key prefix in the title, e.g. '[auth-model] Auth model -- ...'",
}


def derive_verdict(st: dict, args) -> dict:
    s = st.get("steps", {})

    def stat(k):
        return (s.get(k) or {}).get("status", "skipped")

    body_api = stat("1") == "pass"
    body_web = stat("3") == "pass"
    body_reopen = stat("4") == "pass"
    comment_ok = stat("R2") == "pass"
    escaped_ok = stat("7") == "pass" and stat("7_afterWebEdit") in ("pass", "skipped")

    if stat("3") == "skipped":
        survives = None
        rung, why = None, ("undecided: step 3 (the web-UI edit) is the deciding test and it "
                           "has not been run. Do not write join code on steps 1/4 alone.")
    elif body_api and body_web and body_reopen:
        survives, rung = True, 0
        why = "the marker survived the API, the web editor and a close/reopen."
    elif comment_ok and (body_api or body_web):
        survives, rung = False, 2
        why = ("a body marker did not survive every round trip, but a marker inside a "
               "tool-owned comment did. Rung 1 is NOT selectable here: it is the same "
               "body-marker bet moved to one item, so it loses wherever the body loses.")
    elif comment_ok:
        survives, rung = False, 2
        why = "no body marker survived; only the comment carrier did."
    else:
        survives, rung = False, 3
        why = ("neither a body marker nor a comment marker survived every round trip; the "
               "title prefix is the only carrier left. Tags, labels and the search API stay "
               "rejected at every rung.")

    actions = []
    if stat("7") == "fail" or stat("7_afterWebEdit") == "fail":
        actions.append("BLOCKER: the escaped form `&lt;!-- decision-map:` does not round-trip. "
                       "If it DECODED, user text can forge a key marker and the escape-based "
                       "safety argument collapses -- pick a non-entity escape before writing "
                       "join code. If it RE-ENCODED, `chart`'s byte-identical no-op is gone.")
    if stat("2") == "pass" and not ((s.get("2") or {}).get("evidence") or {}).get("doubleHyphenSurvivedApi", True):
        actions.append("Keep the no-`--` key rule: this tracker demonstrably mangles it.")
    elif ((s.get("2") or {}).get("evidence") or {}).get("doubleHyphenSurvivedApi"):
        actions.append("The no-`--` key rule stays (rejection is the safe side), but its stated "
                       "justification is wrong for this tracker: the API stores `--` verbatim. "
                       "Restate it as a defence against downstream renderers.")
    j = ((s.get("5") or {}).get("evidence") or {})
    if j.get("bodiesArrivedInListing"):
        actions.append("The join is ONE paginated pass: bodies arrive in the child listing, so "
                       "delete 'then read each issue's body' from the GitHub join cell.")
    if j.get("fieldsAndExpandRelationsBothHonoured") is False:
        actions.append("ADO workitemsbatch will not honour `fields` and `$expand:relations` "
                       "together -- the join costs two passes per page and the contract's "
                       "O(n/200)+1 estimate is wrong.")
    if j.get("nullBodies"):
        actions.append(f"Null bodies observed on {j['nullBodies']}: the join must classify a "
                       "null-bodied labelled child as the LOUD ERROR, not silently ignore it.")
    if (s.get("6") or {}).get("status") == "pass":
        actions.append("Sub-issues and native dependencies are both live: delete the task-list "
                       "fallback, the `blocked-by: #<n>` body line, and the plan-conditional "
                       "from the Backend-mappings table.")
    if not st.get("cleanedUp", False):
        actions.append("Cleanup did not fully succeed -- re-run --phase cleanup --purge.")

    return {
        "probeVersion": "1",
        "runId": st.get("runId"),
        "tracker": st.get("tracker"),
        "target": st.get("target"),
        "startedAt": st.get("startedAt"),
        "finishedAt": _dt.datetime.now(_dt.timezone.utc).isoformat(),
        "preflight": st.get("preflight"),
        "steps": {k: {"status": stat(k),
                      "detail": (s.get(k) or {}).get("detail"),
                      "evidence": (s.get(k) or {}).get("evidence")}
                  for k in ("1", "2", "3", "4", "5", "6")},
        "additionalSteps": {k: s[k] for k in ("7", "7_afterWebEdit", "R2") if k in s},
        "markerSurvives": survives,
        "fallbackRung": rung,
        "fallbackRungName": RUNGS.get(rung) if rung is not None else None,
        "fallbackRungRationale": why,
        "contractActions": actions,
        "itemsCreated": [{"kind": v.get("kind"), "id": v.get("number"), "url": v.get("url")}
                         for v in (st.get("items") or {}).values()],
        "cleanup": st.get("cleanup"),
        "cleanedUp": st.get("cleanedUp"),
    }


def print_summary(v: dict):
    print("")
    print(f"decision-map marker-survival probe -- {v['tracker']} -- run {v['runId']}")
    for k in ("1", "2", "3", "4", "5", "6"):
        st = v["steps"][k]["status"]
        mark = {"pass": "PASS", "fail": "FAIL", "skipped": "SKIP"}[st]
        print(f"  step {k}: {mark}  {v['steps'][k]['detail']}")
    for k, s in (v.get("additionalSteps") or {}).items():
        print(f"  step {k}: {s['status'].upper()}  {s['detail']}")
    print(f"  markerSurvives = {v['markerSurvives']}   fallbackRung = {v['fallbackRung']}")
    print(f"  {v['fallbackRungRationale']}")
    for a in v.get("contractActions") or []:
        print(f"  action: {a}")


# ---------------------------------------------------------------------------
def plan_lines(args) -> list[str]:
    tgt = args.repo if args.tracker == "github" else f"{args.org}/{args.project}"
    return [
        f"DRY RUN -- nothing was created. Against {args.tracker} target: {tgt}",
        "  would create  1 map item      '[decision-map-probe] <run-id> map (delete me)'",
        f"  would create  1 ticket item   key '{KEY_OK}'   (conforming key)",
        f"  would create  1 ticket item   key '{KEY_BAD}'  (violates the no-`--` rule, on purpose)",
        "  would link    both tickets as children of the map item",
        "  would link    one blocking edge between the two tickets",
        "  would post    1 comment on the map item (fallback rung 2 carrier test)",
        "  would edit    the ticket body once (close/reopen in --phase verify)",
        "  would create  NO labels and NO tags -- the probe never touches that namespace",
        "  cleanup       closes all three items; --purge deletes them outright",
        "",
        "  re-run with --yes to perform it.",
    ]


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="decision-map phase-2 probe: does the key marker survive the tracker?")
    ap.add_argument("--tracker", choices=("github", "ado"), required=True)
    ap.add_argument("--repo", help="github: OWNER/REPO (required, never inferred)")
    ap.add_argument("--org", help="ado: bare org name (or AZDO_ORG)")
    ap.add_argument("--project", help="ado: bare project name (or AZDO_PROJECT)")
    ap.add_argument("--ado-map-type", default="Epic",
                    help="exists in all four default processes (default: Epic)")
    ap.add_argument("--ado-ticket-type", default="Task",
                    help="Task exists in all four default processes; Issue does NOT "
                         "(Scrum has Impediment). Default: Task")
    ap.add_argument("--phase", choices=("setup", "verify", "cleanup", "auto"), default="auto")
    ap.add_argument("--yes", action="store_true",
                    help="required to write anything. Without it this is a dry run.")
    ap.add_argument("--dry-run", action="store_true", help="explicit no-write mode (the default)")
    ap.add_argument("--purge", action="store_true",
                    help="cleanup DELETES instead of closing (recommended on a public repo)")
    ap.add_argument("--assume-edited", action="store_true",
                    help="phase verify: assert a human really did edit the body in the web UI")
    ap.add_argument("--fresh", action="store_true", help="ignore any existing state file")
    ap.add_argument("--out-dir", default=".", help="where state + verdict are written")
    ap.add_argument("--write-delay", type=float, default=0.8,
                    help="seconds between writes (GitHub secondary limit is 80/min)")
    ap.add_argument("--no-cleanup", action="store_true",
                    help="phase auto: leave the items behind (you must clean up yourself)")
    a = ap.parse_args(argv)

    if not a.yes or a.dry_run:
        for ln in plan_lines(a):
            print(ln)
        return EXIT_OK

    tk = GitHubTracker(a) if a.tracker == "github" else AdoTracker(a)
    st = load_state(a)
    if st is None:
        st = {"runId": uuid.uuid4().hex[:8],
              "tracker": a.tracker,
              "target": a.repo if a.tracker == "github" else f"{a.org}/{a.project}",
              "startedAt": _dt.datetime.now(_dt.timezone.utc).isoformat(),
              "steps": {}, "items": {}}
    if a.phase in ("setup", "auto") or not st.get("preflight"):
        st["preflight"] = tk.preflight()
    save_state(a, st)

    try:
        if a.phase in ("setup", "auto"):
            do_setup(tk, a, st)
            if a.phase == "setup":
                print("\nHUMAN STEP -- probe step 3 (the deciding test). Do this now:")
                for ln in tk.web_edit_instructions(st["items"]["ok"]):
                    print("  " + ln)
                print("  ...then re-run this script with:  --phase verify --assume-edited")
                print(f"  state: {state_path(a)}")
        if a.phase in ("verify", "auto"):
            do_verify(tk, a, st)
    finally:
        # cleanup ALWAYS runs, including on failure -- never leave orphans on a public repo
        if a.phase == "cleanup" or (a.phase in ("verify", "auto") and not a.no_cleanup):
            do_cleanup(tk, a, st)

    v = derive_verdict(st, a)
    os.makedirs(a.out_dir, exist_ok=True)
    with open(verdict_path(a), "w", encoding="utf-8", newline="\n") as f:
        json.dump(v, f, indent=2, ensure_ascii=False)
    print_summary(v)
    print(f"\nverdict: {verdict_path(a)}\napi calls: {tk.calls}")
    return EXIT_OK


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ProbeError as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(EXIT_ERROR)
    except KeyboardInterrupt:
        print("error: interrupted -- run --phase cleanup --purge to remove any probe items",
              file=sys.stderr)
        sys.exit(EXIT_ERROR)
