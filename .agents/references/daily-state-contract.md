# daily-state.md — the cross-session work-state contract

One file per project, written by `/daily save` / `/daily wrap` and read back on
`/daily` and `/daily start`. It has **two readers by design**: a **human** (reads and
edits the markdown body) and **another agent/session** (parses the YAML frontmatter
deterministically to resume or hand off). The frontmatter is the stable machine
contract — keep the field names and types exactly as below, because
`plugins/dev-workflows/scripts/daily-state.py` and any other consumer depend on them.

This file is the single source of truth for the schema. The helper script owns all
YAML read/write; do not hand-edit the frontmatter freehand — call `daily-state.py set`
so the contract stays valid and `updated` stays honest.

---

## File location

Resolved at **runtime**, never hardcoded:

1. `--path <file>` flag (highest precedence), then
2. `DAILY_STATE_FILE` env var, then
3. `daily-state.md` at the **git repository root** (`git rev-parse --show-toplevel`).

If the cwd is not inside a git repo, the skill asks the user where to put it rather
than failing. Root-level (not a dotfolder) so the human reader sees it and Obsidian
indexes it. One resume-point per project; the next agent working in that project finds
it at a stable relative path.

---

## The file shape

```markdown
---
type: daily-state
schema_version: 1
updated: 2026-06-19T17:40:00+07:00
station: work
status: in-progress
focus:
  ticket: "6125"
  topic: cargo-group status
next:
  action: grill-then-plan
  reason: fix has a design choice
chain:
  - debug-mantra: done (cause confirmed — POL null)
blockers: []
---

# Daily state — glasshull

## What I was doing
<human prose: files touched, decisions made, links to [[memory]] pages / ADO items>

## Next
<human elaboration of next.action>
```

The **frontmatter** (between the `---` fences) is the machine contract. The **body**
(everything after the closing `---`) is free-form human prose; `--note` appends to it
under a `## Log` section.

---

## Frontmatter field contract

| Field | Type | Required | Meaning |
|---|---|---|---|
| `type` | `"daily-state"` (literal string) | yes | Identifies the contract to any consumer. Always exactly `daily-state`. |
| `schema_version` | int | yes | Currently `1`. Bump only on a breaking schema change; consumers may branch on it. |
| `updated` | ISO-8601 datetime with timezone offset | yes | Set to now by the script on every write (e.g. `2026-06-19T17:40:00+07:00`). A consumer diffs it against now to judge staleness and render relative time ("2h ago") — the relative figure is computed by the reader, never stored. |
| `station` | enum: `start` \| `work` \| `file` \| `report` \| `wrap` | yes | Position in the 5-station daily circle (ADR 0004). |
| `status` | enum: `in-progress` \| `blocked` \| `paused` \| `done` | yes | State of the active focus. |
| `focus` | map | yes | Container for the single active focus (v1 tracks one). |
| `focus.topic` | string | yes | One-line description of the active work. |
| `focus.ticket` | string | no | Ticket/issue id, if any (e.g. `"6125"`). Quote it so YAML keeps it a string. |
| `next` | map | yes | Container for the explicit next step the resuming reader should take. |
| `next.action` | string | yes | The next concrete step — a skill name (e.g. `grill-then-plan`) or free text. This is replayed verbatim on resume; the skill does NOT re-derive it. |
| `next.reason` | string | no | Why that is next. |
| `chain` | list | no | Breadcrumbs of completed work-chain steps (e.g. `- debug-mantra: done (cause confirmed)`). |
| `blockers` | list of strings | no | What is blocking, populated when `status: blocked`. Defaults to `[]`. |

Notes:
- Field **order is preserved** on write (`yaml.safe_dump(..., sort_keys=False)`) to keep diffs small.
- A `set` is read-modify-write: only the fields you pass change; everything else is preserved.
- The body `## What I was doing` may `[[link]]` to relevant auto-memory pages; `daily-state.md` and auto-memory are separate and complementary stores.

---

## Script CLI contract — `scripts/daily-state.py`

The script owns all YAML; **git stays in the skill** (commit is offered, never run by the
script). The script is both importable as a module (pure functions) and runnable as a CLI.

| Command | Does |
|---|---|
| `show [--path P] [--json]` | Read current state. Default = human summary; `--json` = a machine blob containing exactly the frontmatter fields (with `null` filled in for any missing required key). **When no file exists, prints the literal text `no state yet` (NOT JSON) under both plain and `--json` modes** — a caller must string-check for `no state yet` before attempting `json.loads`. |
| `set [--station S] [--status S] [--ticket T] [--topic TXT] [--next-action A] [--next-reason R] [--blocker TXT ...] [--note TXT] [--path P]` | Upsert frontmatter (unset fields preserved), set `updated`=now, optionally append `--note` to the body. `--blocker` is repeatable (replaces the `blockers` list when given). Creates the file with a header if missing. |
| `resolve-path [--path P]` | Print the resolved file path (override order above) so the skill can show the user where it will write — without writing. Exits non-zero with guidance when not in a repo and no override is given. |

Importable seams (used by tests and by other agents — names match the script exactly):
- `resolve_path(path=None, env_value=None, cwd=None, git_root=_UNSET)` — applies the `--path > DAILY_STATE_FILE > git-root` precedence; returns `None` when not in a repo.
- `parse_frontmatter(text) -> (frontmatter_dict, body_str)` — split fences, `yaml.safe_load` the frontmatter.
- `render_frontmatter(frontmatter, body) -> str` — round-trip emit (`sort_keys=False`).
- `upsert_state(existing, station=None, status=None, ticket=None, topic=None, next_action=None, next_reason=None, blockers=None, now=None) -> dict` — read-modify-write, stamps `updated`.
- `human_summary(frontmatter) -> str` and `machine_json(frontmatter) -> str` — the two `show` views.

Importing the module must NOT auto-run; only `if __name__ == '__main__':` invokes the CLI.

---

## See also

- ADR `0014` — `/daily` gains a cross-session work-state file (the decision and rejected alternatives).
- ADR `0004` — `/daily` router hybrid interaction (the 5-station circle this layers onto).
- `plugins/dev-workflows/skills/daily/SKILL.md` — where the read/write touchpoints live.
