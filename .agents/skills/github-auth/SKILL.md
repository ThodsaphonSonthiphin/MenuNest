---
name: github-auth
description: >-
  Authenticate to GitHub and fix auth problems — the shared primitive every
  other github-* skill relies on. Use this BEFORE any create/query when GitHub
  auth is uncertain, and whenever a GitHub API call misbehaves: a call returns
  401 or 403, "gh not logged in" / token missing, results come back empty or
  from the wrong repo. Covers the two supported methods (gh CLI via `gh auth
  token`, or GH_TOKEN env var), a one-line PowerShell verify snippet against
  the repo API, and a 401/403/wrong-repo troubleshooting table. Invoke when
  someone says "I'm getting a 401 from GitHub", "github-create-issues says no
  token", "gh auth failing", or "check my GitHub login before we run".
---

# github-auth

Authenticate to GitHub and diagnose auth failures. This is a **verify and
troubleshoot** skill — the bundled `create_github_issues.py` already implements
both auth methods, so you are not writing new auth code here. You are confirming
a token works and the owner/repo point where the user expects, then unblocking
the create/query skills ([[github-create-issues]], [[findings-to-github-issues]]).

GitHub accepts two credential styles. Pick A unless the user has a reason to use a PAT.

## Method A — gh CLI (recommended)

Why preferred: no long-lived secret to store, and it rides the user's existing `gh`
session. `gh auth token` returns a ready-to-use token.

```powershell
gh auth login          # one-time setup
$token = gh auth token # get the current token
```

Use it as a bearer header: `Authorization: Bearer <token>`.

## Method B — Personal Access Token (PAT)

Use when `gh` CLI isn't available (e.g. a service context) or the user has an
existing PAT. Scope must include **Issues (Read & Write)** on the target repo.
Classic PAT or fine-grained PAT both work.

```powershell
$env:GH_TOKEN = "<your-pat>"
```

`create_github_issues.py` reads `GH_TOKEN` automatically; if unset it shells out
to `gh auth token`. So setting (or unsetting) `GH_TOKEN` is how you switch methods.

## Set target repo

```powershell
$env:GH_OWNER = "Cartagena365"   # org or user name — NOT a URL
$env:GH_REPO  = "GlassHull"     # repo name — NOT a URL
```

## Verify the credential works

```powershell
gh api "repos/$env:GH_OWNER/$env:GH_REPO" --jq '.full_name'
```

A clean result (e.g. `Cartagena365/GlassHull`) proves the token is valid AND the
repo exists and is reachable. Anything else, see the troubleshooting table below.

For a broader prereq sweep (gh CLI, Python, requests, openpyxl, env vars) run the
bundled checker — it is read-only and changes nothing:

```powershell
powershell -ExecutionPolicy Bypass -File ".agents/scripts/setup_check_github.ps1"
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **HTTP 401** Unauthorized | No token, or it expired | `gh auth login`, then re-run `gh auth token`. For PAT: check it hasn't expired or been revoked. |
| **HTTP 403** Forbidden | Token is valid but lacks rights | PAT scope missing **Issues (Read & Write)** — reissue with that scope. For fine-grained PAT: confirm the repo is included in the token's access list. |
| **repo not found / 404** | Wrong owner/repo name, or private repo the token can't see | Check `GH_OWNER` / `GH_REPO` spelling. For private repos: confirm the token/PAT has repo access. |
| **"gh: command not found"** | gh CLI not installed | Install from https://cli.github.com, then `gh auth login`. Or fall back to Method B (`GH_TOKEN`). |
| **200 but wrong repo** | Env vars point at the wrong place | Re-check `GH_OWNER` / `GH_REPO` — exact casing, exact repo name. |

## Hand-off

Once `gh api repos/...` returns the full_name cleanly, auth is good — continue with
[[github-create-issues]] or [[findings-to-github-issues]]. Leave `GH_OWNER` / `GH_REPO`
set in the shell; they carry straight into all downstream skills.

