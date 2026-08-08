# Data contracts (github-backlog)

Three JSON shapes connect the pipeline steps. Keep them stable — the bundled
scripts depend on these exact field names.

```
extract-findings          -> findings.json              (backend-agnostic)
classify-github-issues    -> github_backlog_input.json  (consumed by create_github_issues.py)
github-create-issues      -> github_backlog_result.json (consumed by github_tracking.py)
```

The link between a source row and its created issue is the **`key`** field — carried
through all three files so issue links can be written back to the right row.

---

## 1. `findings.json` — identical to ado-backlog

```json
{
  "source": "Downloads/CRM_Audit.xlsx (sheet 'Findings')",
  "keyColumn": "#",
  "findings": [
    {
      "key": "1",
      "section": "Login",
      "current": "Auto",
      "expected": "Automotive Cargo",
      "kind": "rename",
      "severity": "Critical",
      "status": "confirmed",
      "recommendation": "Rename label",
      "notes": "Appears on 3 screens."
    }
  ]
}
```

| field | required | notes |
|---|---|---|
| `key` | yes | Stable id per finding. Used for write-back. |
| `current` / `expected` | yes | Observed vs canonical value. |
| `kind` | no | `rename` \| `disambiguation` \| `missing` \| `other`. |
| `severity` | no | `Critical` \| `High` \| `Medium` \| `Low`. Drives priority label. |
| `status` | no | Free text (`confirmed`, `needs-review`). Triage filter. |
| `section` / `recommendation` / `notes` | no | Carried into issue body. |

---

## 2. `github_backlog_input.json` — issues to create

```json
{
  "owner": "Cartagena365",
  "repo": "GlassHull",
  "milestone": "Audit Wave 1",
  "items": [
    {
      "key": "1",
      "title": "Portal label \"Auto\" should display \"Automotive Cargo\"",
      "body": "## Finding\n\n**Current:** Auto  \n**Expected:** Automotive Cargo  \n**Recommendation:** Rename label on all screens.\n\n**Estimate:** 2h",
      "labels": ["bug", "P1", "size:XS"],
      "assignees": [],
      "milestone": "Audit Wave 1"
    }
  ]
}
```

| field | required | notes |
|---|---|---|
| `owner` / `repo` | yes* | *Overridden by `GH_OWNER` / `GH_REPO` env vars if set. |
| `milestone` | yes | Name of the batch milestone. Created if it doesn't exist. |
| `items[].key` | yes | Threads back to `findings.json` and into the result. |
| `items[].title` | yes | Issue title. Self-contained — readable without opening. |
| `items[].body` | yes | Markdown. Include `**Estimate:** Xh` at the end. |
| `items[].labels` | yes | Flat GitHub labels: type (`bug`/`enhancement`/`task`/`documentation`), priority (`P0`-`P3`), size (`size:XS`-`size:XL`). |
| `items[].assignees` | no | GitHub usernames. Empty array = unassigned. |

**Label convention:**

| Dimension | Labels |
|---|---|
| Type | `bug`, `enhancement`, `task`, `documentation` |
| Priority | `P0`, `P1`, `P2`, `P3` |
| Size/estimate | `size:XS` (≤2h), `size:S` (3-4h), `size:M` (5-8h), `size:L` (9-16h), `size:XL` (>16h) |

Missing labels are auto-created by `create_github_issues.py` before the first issue.

---

## 3. `github_backlog_result.json` — what got created

```json
{
  "owner": "Cartagena365",
  "repo": "GlassHull",
  "tracking_issue": {
    "number": 42,
    "url": "https://github.com/Cartagena365/GlassHull/issues/42"
  },
  "items": [
    {
      "key": "1",
      "number": 43,
      "url": "https://github.com/Cartagena365/GlassHull/issues/43",
      "status": "created",
      "title": "Portal label \"Auto\" should display \"Automotive Cargo\""
    }
  ]
}
```

`github_tracking.py writeback` matches each result `key` to the source's key column
and writes `Issue #` / `Issue URL` / `State` / `Created`.
