"""
daily-state.py — read/write the per-project daily-state.md work-state file.

daily-state.md is a single markdown file with a YAML frontmatter contract that
captures the user's current position in the daily circle plus the explicit next
step. It has two readers: a human (the markdown body) and another agent/session
(the typed frontmatter, parsed deterministically). The canonical schema lives in
plugins/dev-workflows/references/daily-state-contract.md.

This script owns ALL YAML read/write so the machine contract is never corrupted
by freehand edits. Git stays in the /daily SKILL (commit is offered, never run
here) — this script never touches git.

Subcommands:
  show [--path P] [--json]
      Read current state. Default prints a human summary; --json prints a
      machine blob. Prints `no state yet` (exit 0) when the file is absent.
  set [--station S] [--status S] [--ticket T] [--topic TXT]
      [--next-action A] [--next-reason R] [--blocker TXT ...] [--note TXT] [--path P]
      Upsert frontmatter (unset fields preserved), stamp updated=now, optionally
      append --note to the body. Creates the file (with header) if missing.
  log <message> [--path P]
      Append a timestamped line to the ## Log section (e.g. a commit message).
      Stamps updated=now and ensures minimal frontmatter, but does NOT touch
      station/status/focus/next. Creates the file if missing.
  resolve-path [--path P]
      Print the resolved file path (override order: --path > DAILY_STATE_FILE
      env > git-root). Exits non-zero with a message when not in a git repo and
      no override is given.

Importable as a module: resolve_path, parse_frontmatter, render_frontmatter,
upsert_state, human_summary, machine_json are pure functions with no side
effects beyond the ones named (read/write happen only in the CLI layer). The
CLI lives under `if __name__ == "__main__":` so importing never auto-runs.

Dependency: PyYAML (import name `yaml`).
"""

import argparse
import copy
import json
import os
import subprocess
import sys
from datetime import datetime, timezone

import yaml

try:
    sys.stdout.reconfigure(encoding="utf-8")  # Python 3.7+; avoids cp1252 crashes on Windows
except Exception:
    pass

FILE_NAME = "daily-state.md"
SCHEMA_VERSION = 1
DELIM = "---"

# Canonical frontmatter field order — render_frontmatter emits in this order so
# read-modify-write produces a minimal, stable diff. Fields absent from the
# state dict are simply skipped.
FIELD_ORDER = [
    "type",
    "schema_version",
    "updated",
    "station",
    "status",
    "focus",
    "next",
    "chain",
    "blockers",
]

STATIONS = ["start", "work", "file", "report", "wrap"]
STATUSES = ["in-progress", "blocked", "paused", "done"]

# Sentinel so resolve_path can tell "git_root not supplied, go look it up" apart
# from "git_root explicitly None, i.e. there is no repo" (the latter is how tests
# force the not-in-a-repo branch without hitting a real git call).
_UNSET = object()

# Welcome-back / summary emoji per station (matches the SKILL.md menu glyphs).
STATION_EMOJI = {
    "start": "☀️",   # ☀️
    "work": "\U0001f527",       # 🔧
    "file": "\U0001f4cb",       # 📋
    "report": "\U0001f4e3",     # 📣
    "wrap": "\U0001f319",       # 🌙
}

DEFAULT_BODY = (
    "# Daily state\n\n"
    "## What I was doing\n\n"
    "## Next\n"
)


# ---------- path discovery ----------
def _git_root(cwd=None):
    """Return the git repo root for `cwd` (default: process cwd), or None."""
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
        )
    except (OSError, FileNotFoundError):
        return None
    if out.returncode != 0:
        return None
    root = out.stdout.strip()
    return root or None


def resolve_path(path=None, env_value=None, cwd=None, git_root=_UNSET):
    """Resolve where daily-state.md lives.

    Override order: explicit `path` > `env_value` (DAILY_STATE_FILE) > git-root.
    `git_root` may be injected (tests stub it): pass a path to force a root, or
    `None` to force the not-in-a-repo branch. When left at the `_UNSET` sentinel
    (the normal case) the root is looked up via `git rev-parse --show-toplevel`
    from `cwd`.

    Returns the resolved path string, or None when not in a repo and no override
    is given (the CLI turns None into an ask-the-user message).
    """
    if path:
        return path
    if env_value:
        return env_value
    if git_root is _UNSET:
        git_root = _git_root(cwd=cwd)
    if git_root:
        return os.path.join(git_root, FILE_NAME)
    return None


# ---------- frontmatter parse / render ----------
def parse_frontmatter(text):
    """Split a daily-state.md document into (frontmatter_dict, body_str).

    The frontmatter is the YAML block between the first two `---` delimiter
    lines. If the document has no frontmatter block, returns ({}, text).
    """
    if text is None:
        return {}, ""
    lines = text.splitlines()
    # First non-empty line must be the opening delimiter.
    i = 0
    while i < len(lines) and lines[i].strip() == "":
        i += 1
    if i >= len(lines) or lines[i].strip() != DELIM:
        return {}, text
    start = i + 1
    end = None
    for j in range(start, len(lines)):
        if lines[j].strip() == DELIM:
            end = j
            break
    if end is None:
        # Unterminated frontmatter — treat the whole thing as body, don't lose it.
        return {}, text
    fm_text = "\n".join(lines[start:end])
    body = "\n".join(lines[end + 1:])
    # Strip exactly one leading blank line after the closing delimiter.
    if body.startswith("\n"):
        body = body[1:]
    data = yaml.safe_load(fm_text) if fm_text.strip() else {}
    if not isinstance(data, dict):
        data = {}
    return data, body


def render_frontmatter(frontmatter, body):
    """Render (frontmatter_dict, body_str) back into a full document string.

    Emits frontmatter keys in FIELD_ORDER (then any extras, alphabetically) so
    diffs stay minimal. Uses sort_keys=False to preserve our order. Always ends
    the document with a single trailing newline.
    """
    ordered = {}
    for key in FIELD_ORDER:
        if key in frontmatter:
            ordered[key] = frontmatter[key]
    for key in sorted(frontmatter):
        if key not in ordered:
            ordered[key] = frontmatter[key]

    fm_text = yaml.safe_dump(
        ordered,
        sort_keys=False,
        allow_unicode=True,
        default_flow_style=False,
    ).rstrip("\n")

    body = body if body is not None else ""
    body = body.rstrip("\n")
    return f"{DELIM}\n{fm_text}\n{DELIM}\n\n{body}\n"


def _now_iso():
    """Current local time as ISO-8601 with a timezone offset."""
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


# ---------- read-modify-write ----------
def upsert_state(existing, station=None, status=None, ticket=None, topic=None,
                 next_action=None, next_reason=None, blockers=None, now=None):
    """Read-modify-write the frontmatter dict.

    `existing` is the prior frontmatter dict (or None/{} for a fresh file). Only
    arguments that are not None are applied; everything else is preserved. Always
    (re)stamps `type`, `schema_version`, and `updated`. Returns a NEW dict; does
    not mutate `existing`.

    `blockers`, when provided, REPLACES the blockers list (the CLI collects all
    repeated --blocker values into one list). `now` is injectable for tests;
    defaults to the current local time in ISO-8601.
    """
    state = copy.deepcopy(existing) if existing else {}

    state["type"] = "daily-state"
    state["schema_version"] = SCHEMA_VERSION
    state["updated"] = now if now is not None else _now_iso()

    if station is not None:
        state["station"] = station
    if status is not None:
        state["status"] = status

    if ticket is not None or topic is not None:
        focus = dict(state.get("focus") or {})
        if ticket is not None:
            focus["ticket"] = ticket
        if topic is not None:
            focus["topic"] = topic
        state["focus"] = focus

    if next_action is not None or next_reason is not None:
        nxt = dict(state.get("next") or {})
        if next_action is not None:
            nxt["action"] = next_action
        if next_reason is not None:
            nxt["reason"] = next_reason
        state["next"] = nxt

    if blockers is not None:
        state["blockers"] = list(blockers)

    return state


def append_note(body, note, now=None):
    """Append a timestamped note line under a `## Log` section in the body."""
    if not note:
        return body
    stamp = (now if now is not None else _now_iso())
    base = (body or "").rstrip("\n")
    line = f"- {stamp} — {note}"
    if "## Log" in base:
        return base + "\n" + line + "\n"
    sep = "\n\n" if base else ""
    return base + sep + "## Log\n\n" + line + "\n"


# ---------- views ----------
def _relative_age(updated):
    """Human relative-age string from an ISO-8601 timestamp, e.g. '2h ago'.

    Best-effort: returns '' if `updated` is missing or unparseable. The SKILL
    may compute its own relative time from the --json `updated` field; this is a
    convenience for the plain `show` view.
    """
    if not updated:
        return ""
    try:
        then = datetime.fromisoformat(str(updated))
    except (ValueError, TypeError):
        return ""
    now = datetime.now(then.tzinfo) if then.tzinfo else datetime.now()
    delta = now - then
    secs = int(delta.total_seconds())
    if secs < 0:
        return "just now"
    if secs < 90:
        return f"{secs}s ago"
    mins = secs // 60
    if mins < 90:
        return f"{mins}m ago"
    hours = mins // 60
    if hours < 36:
        return f"{hours}h ago"
    days = hours // 24
    return f"{days}d ago"


def human_summary(frontmatter):
    """One-block human summary of the state (used by `show` with no --json)."""
    fm = frontmatter or {}
    station = fm.get("station", "?")
    emoji = STATION_EMOJI.get(station, "")
    focus = fm.get("focus") or {}
    topic = focus.get("topic", "")
    ticket = focus.get("ticket")
    nxt = fm.get("next") or {}
    action = nxt.get("action", "")
    reason = nxt.get("reason")
    status = fm.get("status", "")
    age = _relative_age(fm.get("updated"))

    head = f"{emoji} {str(station).upper()}".strip()
    if ticket:
        head += f" on #{ticket}"
    if topic:
        head += f" — {topic}"
    if status:
        head += f"  [{status}]"

    lines = [head]
    if age:
        lines.append(f"updated: {fm.get('updated')} ({age})")
    if action:
        nxt_line = f"next: {action}"
        if reason:
            nxt_line += f" ({reason})"
        lines.append(nxt_line)
    blockers = fm.get("blockers") or []
    for b in blockers:
        lines.append(f"blocker: {b}")
    return "\n".join(lines)


def machine_json(frontmatter):
    """Machine blob (for an agent). Guarantees required keys are present.

    Required per the contract: type, schema_version, updated, station, status,
    focus.topic, next.action. Missing keys are emitted as null so a consumer can
    detect them rather than KeyError.
    """
    fm = dict(frontmatter or {})
    fm.setdefault("type", "daily-state")
    fm.setdefault("schema_version", SCHEMA_VERSION)
    fm.setdefault("updated", None)
    fm.setdefault("station", None)
    fm.setdefault("status", None)
    focus = dict(fm.get("focus") or {})
    focus.setdefault("topic", None)
    focus.setdefault("ticket", None)
    fm["focus"] = focus
    nxt = dict(fm.get("next") or {})
    nxt.setdefault("action", None)
    fm["next"] = nxt
    return json.dumps(fm, ensure_ascii=False, indent=2, sort_keys=False)


# ---------- file I/O (CLI layer only) ----------
def read_document(path):
    """Read a daily-state.md file. Returns (frontmatter, body) or (None, None)
    when the file does not exist."""
    if not path or not os.path.exists(path):
        return None, None
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    return parse_frontmatter(text)


def write_document(path, frontmatter, body):
    with open(path, "w", encoding="utf-8") as f:
        f.write(render_frontmatter(frontmatter, body))


# ---------- CLI ----------
def _resolved_or_die(path_flag):
    """Resolve the path for the CLI, exiting with guidance if not in a repo."""
    resolved = resolve_path(
        path=path_flag,
        env_value=os.environ.get("DAILY_STATE_FILE"),
    )
    if resolved is None:
        raise SystemExit(
            "not in a git repo and no --path / DAILY_STATE_FILE given. "
            "Pass --path <file> to choose where daily-state.md lives."
        )
    return resolved


def cmd_show(args):
    path = _resolved_or_die(args.path)
    frontmatter, _body = read_document(path)
    if frontmatter is None:
        print("no state yet")
        return
    if args.json:
        print(machine_json(frontmatter))
    else:
        print(human_summary(frontmatter))


def cmd_set(args):
    path = _resolved_or_die(args.path)
    existing, body = read_document(path)
    if body is None:
        body = DEFAULT_BODY
    now = _now_iso()
    state = upsert_state(
        existing,
        station=args.station,
        status=args.status,
        ticket=args.ticket,
        topic=args.topic,
        next_action=args.next_action,
        next_reason=args.next_reason,
        blockers=(args.blocker if args.blocker else None),
        now=now,
    )
    if args.note:
        body = append_note(body, args.note, now=now)
    write_document(path, state, body)
    print(f"wrote {path} (updated={state['updated']})")


def cmd_log(args):
    """Append a single timestamped line to the ## Log body (commit echo).

    Unlike `set`, this never touches station/status/focus/next — it only ensures
    minimal frontmatter (type/schema_version/updated) and appends the note. Used
    by the commit-log hook so every commit lands a line in ## Log.
    """
    path = _resolved_or_die(args.path)
    existing, body = read_document(path)
    if body is None:
        body = DEFAULT_BODY
    now = _now_iso()
    state = upsert_state(existing, now=now)  # stamps type/schema/updated, preserves the rest
    body = append_note(body, args.message, now=now)
    write_document(path, state, body)
    print(f"logged to {path} (updated={state['updated']})")


def cmd_resolve_path(args):
    print(_resolved_or_die(args.path))


def build_parser():
    ap = argparse.ArgumentParser(
        prog="daily-state.py",
        description="Read/write the per-project daily-state.md work-state file.",
    )
    sub = ap.add_subparsers(dest="cmd", required=True)

    s_show = sub.add_parser("show", help="read current state (human or --json)")
    s_show.add_argument("--path")
    s_show.add_argument("--json", action="store_true")
    s_show.set_defaults(func=cmd_show)

    s_set = sub.add_parser("set", help="upsert frontmatter (unset fields preserved)")
    s_set.add_argument("--station", choices=STATIONS)
    s_set.add_argument("--status", choices=STATUSES)
    s_set.add_argument("--ticket")
    s_set.add_argument("--topic")
    s_set.add_argument("--next-action", dest="next_action")
    s_set.add_argument("--next-reason", dest="next_reason")
    s_set.add_argument("--blocker", action="append", help="repeatable; replaces the blockers list")
    s_set.add_argument("--note", help="append a timestamped note to the body")
    s_set.add_argument("--path")
    s_set.set_defaults(func=cmd_set)

    s_log = sub.add_parser(
        "log",
        help="append a timestamped line to ## Log (commit echo); does not touch frontmatter state",
    )
    s_log.add_argument("message", help="the log line text (e.g. a commit message)")
    s_log.add_argument("--path")
    s_log.set_defaults(func=cmd_log)

    s_rp = sub.add_parser("resolve-path", help="print the resolved file path")
    s_rp.add_argument("--path")
    s_rp.set_defaults(func=cmd_resolve_path)

    return ap


def main(argv=None):
    args = build_parser().parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
