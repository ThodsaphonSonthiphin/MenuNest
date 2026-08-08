"""
test_daily_state.py — unit tests for the daily-state.py helper.

daily-state.py owns a machine-read contract (the YAML frontmatter another agent
parses), so a parse/emit bug silently corrupts the file for every consumer.
These tests lock the pure functions: path precedence, frontmatter round-trip,
read-modify-write preservation, the machine blob, and the not-in-repo branch.

Run from the repo root:
    python -m pytest plugins/dev-workflows/scripts/test_daily_state.py -v

(`pytest` is not on PATH in this environment — invoke via `python -m pytest`.)
"""

import importlib.util
import json
import os

import pytest

# The module file name has a hyphen, so it cannot be imported by name; load it
# from its path next to this test file.
_HERE = os.path.dirname(os.path.abspath(__file__))
_SPEC = importlib.util.spec_from_file_location(
    "daily_state", os.path.join(_HERE, "daily-state.py")
)
ds = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(ds)


# ---------- resolve_path precedence (acceptance check 6 + override order) ----------
def test_resolve_path_flag_wins_over_env_and_gitroot():
    got = ds.resolve_path(path="/explicit/x.md", env_value="/env/y.md", git_root="/repo")
    assert got == "/explicit/x.md"


def test_resolve_path_env_wins_over_gitroot():
    got = ds.resolve_path(path=None, env_value="/env/y.md", git_root="/repo")
    assert got == "/env/y.md"


def test_resolve_path_falls_back_to_gitroot():
    got = ds.resolve_path(path=None, env_value=None, git_root="/repo/root")
    assert got == os.path.join("/repo/root", ds.FILE_NAME)


def test_resolve_path_returns_none_when_not_in_repo():
    # No flag, no env, no git root -> None so the CLI can ask the user.
    assert ds.resolve_path(path=None, env_value=None, git_root=None) is None


def test_cli_resolve_path_errors_when_not_in_repo(monkeypatch):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    monkeypatch.setattr(ds, "_git_root", lambda cwd=None: None)
    with pytest.raises(SystemExit) as exc:
        ds.main(["resolve-path"])
    assert "not in a git repo" in str(exc.value)


def test_cli_resolve_path_honors_env(monkeypatch, capsys):
    monkeypatch.setenv("DAILY_STATE_FILE", os.path.join("/env", "z.md"))
    monkeypatch.setattr(ds, "_git_root", lambda cwd=None: None)
    ds.main(["resolve-path"])
    out = capsys.readouterr().out.strip()
    assert out == os.path.join("/env", "z.md")


# ---------- frontmatter round-trip ----------
def test_parse_render_round_trip_preserves_body():
    state = ds.upsert_state(
        None, station="work", topic="cargo-group status",
        next_action="grill-then-plan", now="2026-06-19T17:40:00+07:00",
    )
    body = "# Daily state\n\n## What I was doing\nlooked at #6125\n\n## Next\nplan the fix\n"
    text = ds.render_frontmatter(state, body)
    fm2, body2 = ds.parse_frontmatter(text)
    assert fm2["type"] == "daily-state"
    assert fm2["station"] == "work"
    assert fm2["focus"]["topic"] == "cargo-group status"
    assert fm2["next"]["action"] == "grill-then-plan"
    # Body survives intact (modulo trailing-newline normalization).
    assert "looked at #6125" in body2
    assert "plan the fix" in body2


def test_render_emits_canonical_field_order():
    state = ds.upsert_state(
        None, status="blocked", station="file", topic="x", next_action="y",
        now="2026-06-19T10:00:00+07:00",
    )
    text = ds.render_frontmatter(state, "body")
    # type/schema_version/updated/station/status/focus/next must appear in order.
    order = ["type:", "schema_version:", "updated:", "station:", "status:", "focus:", "next:"]
    positions = [text.index(tok) for tok in order]
    assert positions == sorted(positions), text


def test_parse_handles_missing_frontmatter():
    fm, body = ds.parse_frontmatter("# just a heading\nno frontmatter here\n")
    assert fm == {}
    assert "just a heading" in body


# ---------- upsert read-modify-write (acceptance check 3) ----------
def test_upsert_creates_required_fields():
    state = ds.upsert_state(
        None, station="work", topic="x", next_action="grill-then-plan",
        now="2026-06-19T17:40:00+07:00",
    )
    assert state["type"] == "daily-state"
    assert state["schema_version"] == ds.SCHEMA_VERSION
    assert state["updated"] == "2026-06-19T17:40:00+07:00"
    assert state["station"] == "work"
    assert state["focus"]["topic"] == "x"
    assert state["next"]["action"] == "grill-then-plan"


def test_second_upsert_preserves_unset_fields():
    first = ds.upsert_state(
        None, station="work", ticket="6125", topic="cargo-group status",
        next_action="grill-then-plan", next_reason="design choice",
        now="2026-06-19T17:40:00+07:00",
    )
    # Second set changes ONLY status; everything else must survive.
    second = ds.upsert_state(first, status="blocked", now="2026-06-19T18:00:00+07:00")
    assert second["status"] == "blocked"
    assert second["station"] == "work"               # preserved
    assert second["focus"]["ticket"] == "6125"        # preserved
    assert second["focus"]["topic"] == "cargo-group status"
    assert second["next"]["action"] == "grill-then-plan"
    assert second["next"]["reason"] == "design choice"
    assert second["updated"] == "2026-06-19T18:00:00+07:00"  # restamped


def test_upsert_does_not_mutate_input():
    first = ds.upsert_state(None, station="work", topic="x", next_action="y",
                            now="2026-06-19T17:40:00+07:00")
    snapshot = json.dumps(first, sort_keys=True)
    ds.upsert_state(first, status="done", now="2026-06-19T19:00:00+07:00")
    assert json.dumps(first, sort_keys=True) == snapshot


def test_upsert_does_not_alias_nested_focus_or_next():
    first = ds.upsert_state(None, station="work", ticket="6125", topic="cargo-group status",
                            next_action="grill-then-plan", now="2026-06-19T17:40:00+07:00")
    # change ONLY status -> focus/next branches are not taken
    second = ds.upsert_state(first, status="blocked", now="2026-06-19T18:00:00+07:00")
    # mutating the result's nested dicts must not bleed back into `first`
    second["focus"]["topic"] = "MUTATED"
    second["next"]["action"] = "MUTATED"
    assert first["focus"]["topic"] == "cargo-group status"
    assert first["next"]["action"] == "grill-then-plan"


def test_upsert_replaces_blockers_list():
    first = ds.upsert_state(None, status="blocked", topic="x", next_action="y",
                            blockers=["waiting on QA"], now="2026-06-19T17:40:00+07:00")
    assert first["blockers"] == ["waiting on QA"]
    second = ds.upsert_state(first, blockers=["new blocker", "second"],
                             now="2026-06-19T18:00:00+07:00")
    assert second["blockers"] == ["new blocker", "second"]


# ---------- machine_json (acceptance check 2) ----------
def test_machine_json_has_all_required_fields():
    state = ds.upsert_state(None, station="work", topic="x", next_action="y",
                            now="2026-06-19T17:40:00+07:00")
    blob = json.loads(ds.machine_json(state))
    for key in ("type", "schema_version", "updated", "station", "status", "focus", "next"):
        assert key in blob, f"missing {key}"
    assert "topic" in blob["focus"]
    assert "action" in blob["next"]


def test_machine_json_fills_nulls_for_sparse_state():
    blob = json.loads(ds.machine_json({"type": "daily-state"}))
    assert blob["station"] is None
    assert blob["status"] is None
    assert blob["focus"]["topic"] is None
    assert blob["next"]["action"] is None


# ---------- note append ----------
def test_append_note_adds_log_section():
    body = ds.append_note("", "cause confirmed", now="2026-06-19T17:40:00+07:00")
    assert "## Log" in body
    assert "cause confirmed" in body
    # A second note reuses the same Log section (no duplicate header).
    body2 = ds.append_note(body, "started the plan", now="2026-06-19T18:00:00+07:00")
    assert body2.count("## Log") == 1
    assert "started the plan" in body2


# ---------- end-to-end CLI on a real temp file (acceptance checks 1, 2, 3) ----------
def test_cli_set_then_show_round_trip(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    state_file = tmp_path / "daily-state.md"

    # set creates the file with valid frontmatter (check 1)
    ds.main([
        "set", "--station", "work", "--topic", "cargo-group status",
        "--next-action", "grill-then-plan", "--path", str(state_file),
    ])
    capsys.readouterr()
    assert state_file.exists()
    text = state_file.read_text(encoding="utf-8")
    assert text.startswith("---\n")
    assert "type: daily-state" in text

    # show --json emits all required fields (check 2)
    ds.main(["show", "--json", "--path", str(state_file)])
    blob = json.loads(capsys.readouterr().out)
    assert blob["station"] == "work"
    assert blob["focus"]["topic"] == "cargo-group status"
    assert blob["next"]["action"] == "grill-then-plan"
    assert blob["updated"] is not None  # agent computes relative time from this

    # second set preserves unset fields (check 3)
    ds.main(["set", "--status", "blocked", "--path", str(state_file)])
    capsys.readouterr()
    ds.main(["show", "--json", "--path", str(state_file)])
    blob2 = json.loads(capsys.readouterr().out)
    assert blob2["status"] == "blocked"
    assert blob2["station"] == "work"                      # preserved
    assert blob2["focus"]["topic"] == "cargo-group status"  # preserved


def test_cli_show_no_state_yet(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    missing = tmp_path / "daily-state.md"
    ds.main(["show", "--path", str(missing)])
    assert capsys.readouterr().out.strip() == "no state yet"
    ds.main(["show", "--json", "--path", str(missing)])
    assert capsys.readouterr().out.strip() == "no state yet"


def test_cli_set_with_note_appends_body(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    state_file = tmp_path / "daily-state.md"
    ds.main([
        "set", "--station", "work", "--topic", "x", "--next-action", "y",
        "--note", "cause confirmed — POL null", "--path", str(state_file),
    ])
    capsys.readouterr()
    text = state_file.read_text(encoding="utf-8")
    assert "## Log" in text
    assert "cause confirmed — POL null" in text


# ---------- log subcommand (commit echo) ----------
def test_cli_log_creates_file_with_minimal_frontmatter(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    state_file = tmp_path / "daily-state.md"
    ds.main(["log", "fix(cargo): pass real contactId #6053", "--path", str(state_file)])
    capsys.readouterr()
    assert state_file.exists()
    text = state_file.read_text(encoding="utf-8")
    assert "type: daily-state" in text          # minimal frontmatter stamped
    assert "## Log" in text
    assert "fix(cargo): pass real contactId #6053" in text


def test_cli_log_does_not_touch_state_fields(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    state_file = tmp_path / "daily-state.md"
    # establish real work-state first
    ds.main(["set", "--station", "work", "--ticket", "6125", "--topic", "cargo-group status",
             "--next-action", "grill-then-plan", "--path", str(state_file)])
    capsys.readouterr()
    # a commit echo must append to ## Log but NOT change station/focus/next
    ds.main(["log", "wip commit", "--path", str(state_file)])
    capsys.readouterr()
    ds.main(["show", "--json", "--path", str(state_file)])
    blob = json.loads(capsys.readouterr().out)
    assert blob["station"] == "work"
    assert blob["focus"]["ticket"] == "6125"
    assert blob["focus"]["topic"] == "cargo-group status"
    assert blob["next"]["action"] == "grill-then-plan"
    assert "wip commit" in state_file.read_text(encoding="utf-8")


def test_cli_log_appends_into_single_log_section(tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("DAILY_STATE_FILE", raising=False)
    state_file = tmp_path / "daily-state.md"
    ds.main(["log", "first commit", "--path", str(state_file)])
    capsys.readouterr()
    ds.main(["log", "second commit", "--path", str(state_file)])
    capsys.readouterr()
    text = state_file.read_text(encoding="utf-8")
    assert text.count("## Log") == 1
    assert "first commit" in text
    assert "second commit" in text
