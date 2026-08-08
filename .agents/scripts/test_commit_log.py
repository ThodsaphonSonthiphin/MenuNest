"""Regression tests for the commit-log PostToolUse hook.

Each test drives `hooks/commit-log.py` exactly as the harness does: a payload as
UTF-8 bytes on a stdin pipe. The first three classes are the bugs the hook shipped
with (see its module docstring) — non-ASCII mojibake, a shell-substitution message
logged raw, and a content-free "(commit)" placeholder.
"""

import io
import json
import os
import subprocess
import sys

import pytest

HOOK = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "hooks", "commit-log.py",
)

EM = "—"                        # em dash
THAI = "พัฒนา"

EMPTY_STATE = (
    "---\ntype: daily-state\nschema_version: 1\n"
    "updated: '2026-08-01T00:00:00+07:00'\n---\n\n# Daily state\n\n## Log\n\n"
)


def _sh(repo, *args):
    """Run a git command in repo, returning the CompletedProcess."""
    return subprocess.run(
        ["git", "-C", str(repo)] + list(args),
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )


@pytest.fixture
def repo(tmp_path):
    """A git repo with an empty daily-state.md and one commit already made."""
    _sh(tmp_path, "init", "-q", ".")
    _sh(tmp_path, "config", "user.email", "t@t.t")
    _sh(tmp_path, "config", "user.name", "T")
    _sh(tmp_path, "config", "commit.gpgsign", "false")
    (tmp_path / "daily-state.md").write_text(EMPTY_STATE, encoding="utf-8")
    return tmp_path


def commit(repo, subject, filename=None, *extra):
    """Make a real commit so HEAD moves, the way the hook expects to find it."""
    if filename:
        (repo / filename).write_text(filename, encoding="utf-8")
        _sh(repo, "add", "-A")
    r = _sh(repo, "commit", "-q", "-m", subject, *extra)
    assert r.returncode == 0, r.stderr
    return r


def fire(repo, command, tool_name="Bash"):
    """Invoke the hook with a PostToolUse payload; assert the hard contract holds."""
    payload = {"tool_name": tool_name,
               "tool_input": {"command": command},
               "cwd": str(repo)}
    raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    p = subprocess.run([sys.executable, HOOK], input=raw,
                       capture_output=True, timeout=60)
    # HARD CONTRACT: never block, never speak.
    assert p.returncode == 0, "hook exited %d: %r" % (p.returncode, p.stderr)
    assert p.stdout == b"", "hook wrote to stdout: %r" % p.stdout
    return p


def log_lines(repo):
    text = (repo / "daily-state.md").read_text(encoding="utf-8")
    body = text.split("## Log", 1)[1] if "## Log" in text else ""
    return [l for l in body.splitlines() if l.startswith("- ")]


# --------------------------------------------------------------------------
# symptom 1 — non-ASCII was decoded with the Windows ANSI codepage
# --------------------------------------------------------------------------

def test_non_ascii_subject_is_not_mojibake(repo):
    subject = "feat: subject with an %s em dash and %s" % (EM, THAI)
    commit(repo, subject, "a.txt")
    fire(repo, 'git commit -m "%s"' % subject)

    lines = log_lines(repo)
    assert len(lines) == 1
    assert EM in lines[0]
    assert THAI in lines[0]
    assert "â€”" not in lines[0]   # the cp1252 rendering of an em dash


# --------------------------------------------------------------------------
# symptom 2 — `-m "$(cat <<'EOF' ...)"` logged the shell source, not the message
# --------------------------------------------------------------------------

def test_heredoc_substitution_logs_the_real_subject(repo):
    subject = "feat: from a heredoc"
    commit(repo, subject, "b.txt", "-m", "Co-Authored-By: X <x@y.z>")
    fire(repo, 'git commit -m "$(cat <<\'EOF\'\n%s\n\nCo-Authored-By: X\nEOF\n)"' % subject)

    line = log_lines(repo)[-1]
    assert subject in line
    assert "$(" not in line and "EOF" not in line
    assert len(line.splitlines()) == 1        # never breaks the Log list structure


def test_body_trailers_are_not_logged(repo):
    commit(repo, "feat: subject only", "b.txt", "-m", "Co-Authored-By: X <x@y.z>")
    fire(repo, 'git commit -m "feat: subject only" -m "Co-Authored-By: X <x@y.z>"')

    assert "Co-Authored-By" not in log_lines(repo)[-1]


# --------------------------------------------------------------------------
# symptom 3 — a commit with no -m logged a content-free placeholder
# --------------------------------------------------------------------------

def test_commit_without_dash_m_logs_the_real_subject(repo):
    commit(repo, "feat: original", "c.txt")
    _sh(repo, "commit", "-q", "--amend", "-m", "feat: amended subject")
    fire(repo, "git commit --amend --no-edit")

    lines = log_lines(repo)
    assert "feat: amended subject" in lines[-1]
    assert not any("(commit)" in l for l in lines)


# --------------------------------------------------------------------------
# only real commits get logged
# --------------------------------------------------------------------------

@pytest.mark.parametrize("command", [
    "git log -1 --format=%B  # show last commit message",
    "git merge feature --no-edit  # keeps the merge commit",
    "echo 'remember to commit'",
])
def test_merely_mentioning_commit_logs_nothing(repo, command):
    commit(repo, "feat: already logged", "d.txt")
    fire(repo, 'git commit -m "feat: already logged"')
    before = len(log_lines(repo))

    fire(repo, command)

    assert len(log_lines(repo)) == before


def test_failed_commit_logs_nothing(repo):
    commit(repo, "feat: baseline", "e.txt")
    fire(repo, 'git commit -m "feat: baseline"')
    before = len(log_lines(repo))

    # nothing staged -> git refuses, HEAD does not move
    assert _sh(repo, "commit", "-m", "feat: nothing staged").returncode != 0
    fire(repo, 'git commit -m "feat: nothing staged"')

    assert len(log_lines(repo)) == before


def test_firing_twice_for_one_commit_appends_once(repo):
    commit(repo, "chore: idempotence probe", "f.txt")
    fire(repo, 'git commit -m "chore: idempotence probe"')
    after_first = len(log_lines(repo))

    fire(repo, 'git commit -m "chore: idempotence probe"')

    assert len(log_lines(repo)) == after_first


def test_logs_what_git_recorded_not_what_was_typed(repo):
    """A git hook that rewrites the message must not desync the log."""
    commit(repo, "feat: what the agent typed", "g.txt")
    _sh(repo, "commit", "-q", "--amend", "-m", "feat: what a hook rewrote it to")
    fire(repo, 'git commit -m "feat: what the agent typed"')

    assert "what a hook rewrote it to" in log_lines(repo)[-1]


def test_non_bash_tool_is_ignored(repo):
    commit(repo, "feat: baseline", "h.txt")
    before = len(log_lines(repo))

    fire(repo, 'git commit -m "feat: baseline"', tool_name="Read")

    assert len(log_lines(repo)) == before


# --------------------------------------------------------------------------
# the hard contract: never block, whatever the input
# --------------------------------------------------------------------------

@pytest.mark.parametrize("raw", [
    b"",
    b"not json at all",
    b"{}",
    b'{"tool_name": "Bash"}',
    b'{"tool_name": "Bash", "tool_input": {"command": "git commit -m x"}}',
    b'{"tool_name": "Bash", "tool_input": {"command": "git commit"}, "cwd": "/nope/absent"}',
])
def test_malformed_payload_never_blocks(raw):
    p = subprocess.run([sys.executable, HOOK], input=raw,
                       capture_output=True, timeout=60)
    assert p.returncode == 0
    assert p.stdout == b""


def test_outside_a_git_repo_logs_nothing(tmp_path):
    """No git repo at cwd -> silent no-op, no file created."""
    fire(tmp_path, 'git commit -m "feat: nowhere"')
    assert not (tmp_path / "daily-state.md").exists()


def test_long_subject_is_truncated_to_one_line(repo):
    subject = "feat: " + ("x" * 400)
    commit(repo, subject, "i.txt")
    fire(repo, 'git commit -m "%s"' % subject)

    line = log_lines(repo)[-1]
    assert len(line.splitlines()) == 1
    assert len(line) < 300


def test_dash_C_selects_the_target_repo(tmp_path):
    """`git -C <path> commit` must log into that repo, not the payload's cwd."""
    other = tmp_path / "other"
    other.mkdir()
    _sh(other, "init", "-q", ".")
    _sh(other, "config", "user.email", "t@t.t")
    _sh(other, "config", "user.name", "T")
    _sh(other, "config", "commit.gpgsign", "false")
    (other / "daily-state.md").write_text(EMPTY_STATE, encoding="utf-8")
    commit(other, "feat: in the other repo", "j.txt")

    elsewhere = tmp_path / "elsewhere"
    elsewhere.mkdir()
    fire(elsewhere, 'git -C "%s" commit -m "feat: in the other repo"' % other)

    assert "feat: in the other repo" in log_lines(other)[-1]
    assert not (elsewhere / "daily-state.md").exists()
