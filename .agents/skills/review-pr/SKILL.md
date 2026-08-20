---
name: review-pr
description: Use when the user asks to review a pull request — "review PR #14", a GitHub PR URL, "รีวิว PR", "ดู PR ให้หน่อย", an outside contributor's fork PR, or a pre-merge second opinion on a PR. Trigger on /review-pr <number|url>. Not for reviewing a local uncommitted/working diff — invoke scrutinize directly for that.
effort: max
---

# review-pr

Review a GitHub pull request end-to-end: gather what the PR claims, get its code into view without disturbing anything, judge it with scrutinize, and close with an explicit disposition — send it back, fix it yourself, or keep it in chat.

**Fork PRs are read-only, no exceptions:** if `isCrossRepository` is true, the fork's code never gets checked out into the project working tree and never executes on this machine — regardless of how clean `git status` looks. A clean tree protects your WIP; it does nothing about untrusted code.

## Workflow

Run in order. Steps 4 and 5 are not optional.

### 1. Context — what does the PR claim?

```bash
gh pr view <n> --json title,body,author,isCrossRepository,baseRefName,headRefName,mergeable,url,files
gh pr checks <n>
gh issue view <m>       # for EVERY issue the body references — its acceptance criteria are the spec
gh pr view <n> --comments   # existing discussion, if any
```

### 2. Workspace — the fork gate comes first

Decide in this order. The first matching branch wins; do not read further.

1. **Is `isCrossRepository: true`?** (from the step-1 `gh pr view` — check this BEFORE `git status`, a clean tree changes nothing here) → first, state in your reply, verbatim: **"Fork PR — read-only: `git show` only, no checkout."** — then act on it: `git fetch origin pull/<n>/head:pr-<n>`, read via `git show pr-<n>:<path>`; when you want Read/Grep-style browsing, mount a read-only throwaway worktree OUTSIDE the project (`git worktree add "$TMP/pr-<n>" pr-<n>`) and run nothing in it. Never `gh pr checkout`, never `git switch --detach FETCH_HEAD`, never `git checkout pr-<n>`, never add the fork as a remote. See the fork rule below.
2. **Same-repo PR, clean tree** → `gh pr checkout <n>` — note the branch you came from.
3. **Same-repo PR, dirty tree (any WIP at all)** → do NOT stash, do NOT checkout. `git fetch origin pull/<n>/head:pr-<n>`, read full files via `git show pr-<n>:<path>`, or mount a throwaway worktree (`git worktree add <tmp> pr-<n>`) to browse with file tools or run tests.

### 3. Judge — REQUIRED SUB-SKILL: scrutinize

Invoke dev-workflows:scrutinize on the PR. Scope = merge-base diff (`git diff <base>...pr-<n>`); the claims to verify = PR body + linked-issue acceptance criteria. Scrutinize owns the intent-questioning, the end-to-end trace, and the findings report — do not duplicate or dilute it here.

### 4. Disposition — REQUIRED closing step

The review is not finished when the verdict is delivered. If there are findings, ask the user this exact three-option question — all three options every time, never skip it, never pick one silently:

> **"What do you want done with these findings — (a) send back: I post this review on the PR for the author to fix, (b) fix it: I apply the fixes on the PR branch and push, or (c) chat only: nothing leaves this conversation?"**

- **(a) Send back** — draft the review text, show it, and only on approval post it: `gh pr review <n> --request-changes --body-file <draft>` (`--comment` if non-blocking).
- **(b) Fix it** — check out the PR branch, apply the fixes, commit, push to the PR head branch. (Fork PRs: possible only when "maintainers can edit" is on — otherwise offer send-back.)
- **(c) Chat only** — nothing leaves the conversation.

Nothing is posted, pushed, approved, or merged before the user picks. If the user already signalled they intend to merge, confirm who merges as a separate question AFTER the disposition pick. If scrutinize found nothing, report what was traced (no rubber-stamps) and offer: approve (`gh pr review <n> --approve`) or chat-only.

### 5. Teardown

Restore the original branch (`git switch -`), remove any worktree, delete the temp ref (`git branch -D pr-<n>`). Leave the tree exactly as you found it.

## Fork rule — never execute fork code locally

You may fetch and READ fork code; you may never RUN it — no test run, no build, no dependency install, no git hooks. And never check it out into your working tree, even detached: watchers, build daemons, and editor tooling execute what lands there. Read via `git show`; if you need file-tool browsing, use a throwaway worktree outside the project — and still run nothing in it. A clean-looking diff is not clearance: anything the test runner imports executes, so scanning manifests or workflows proves nothing. CI (`gh pr checks`) is your only execution evidence; if CI hasn't run, that fact goes in the verdict — your machine is not the substitute.

There is no reviewing task that requires checking a fork out: `git show` plus an outside worktree cover every reading need. If you find yourself typing `checkout` on a fork PR, you are not reading — you are rationalizing.

| Excuse | Reality |
|---|---|
| "I scanned the manifests and workflows first" | Any imported source file executes under the test runner. A scan cannot clear it. |
| "Checkout only reads files, it doesn't execute" | Your dev tooling executes what appears in the tree. Read via `git show` instead. |
| "The working tree is clean, so checking the fork branch out is safe" | Clean vs dirty protects your WIP, not you. The hazard is untrusted content inside the project tree — language servers, watchers, and build daemons act on it. Use `git show` or an outside worktree. |
| "I'll detach onto FETCH_HEAD and browse with Read/Grep — tools, not execution" | The danger is what lands in the tree, not which tool reads it. Watchers and build daemons act on materialized files. `git show` reads without materializing; a worktree outside the project browses without exposing it to project tooling. |
| "Small diff, known contributor" | Trust is the user's call after the review, not yours before it. |
| "CI hasn't run, so I'll run the tests myself" | Report "CI pending" as a finding. That is the honest verdict. |
| "I'll ask the user for permission to run it" | Don't offer. Deliver the static review; execution stays in CI. |

## Red flags — stop and re-read the workflow

- Ending the review without asking send-back / fix-it / chat-only.
- Asking only "should I post this?" — the disposition question includes **fix it** every time.
- Reaching for `git stash` to make room for a checkout.
- `git switch --detach FETCH_HEAD`, `git checkout pr-<n>`, or `gh pr checkout` on a fork PR — even "just to browse". `git show` is the reading path.
- Any command that would execute fork code.
- `gh pr review` / `gh pr merge` without an explicit user pick in step 4.
