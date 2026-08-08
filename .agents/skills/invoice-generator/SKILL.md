---
name: invoice-generator
description: Generate a daily work summary from git commits and reshape it for leadership via management-talk. Use this skill whenever the user mentions Tribletext, invoice, daily summary, timesheet, time report, work log, daily report, status update for management, or wants to summarize what they did yesterday/today from git history. Also trigger when the user says things like "what did I do today", "summarize my commits", "generate my timesheet", or "prepare my daily/leadership update". The raw commit summary is always handed off to the management-talk skill so the final deliverable is shaped for the channel it is going to (Tribletext entry, Slack, standup, email, JIRA, or meeting talking-points).
---

# Invoice Generator — Daily Summary, Reshaped for Leadership

This skill reads git commits across all repositories in the workspace, produces a
concise daily work summary, then **always** hands that summary to the
`management-talk` skill so the final output is shaped for the channel it is going to.

## Why this exists

Writing daily time reports and status updates is tedious. Git commits already
capture what was done — this skill turns them into a clean per-day summary and then
reshapes that summary for leadership/the target channel, ready to paste without
editing.

## How it works

### Step 1: Discover all git repositories

Find all git repos under the current working directory. Repos are identified by
having a `.git` directory. Typically this means checking immediate subdirectories:

```bash
for dir in */; do
  if [ -d "$dir/.git" ]; then
    echo "$dir"
  fi
done
```

### Step 2: Collect commits for yesterday and today

For each discovered repo, fetch commits authored by the current git user for
**yesterday** and **today**:

```bash
git log --author="$(git config user.name)" \
  --since="YYYY-MM-DD" --until="YYYY-MM-DD" \
  --pretty=format:"%ad | %s" --date=short
```

- Use the actual calendar dates (not relative dates like `--since=yesterday`) to
  avoid timezone ambiguity.
- Collect from ALL repos and tag each commit with which repo it came from so you can
  group related work. Include all branches (`--all`) so work on feature branches is
  not missed.

### Step 3: Summarize into 4 lines per day

This is the core value of the skill. Raw commits are too granular — produce a
high-level summary first.

**Rules for summarizing:**

1. **Group by feature/area, not by repo.** If frontend and backend commits both
   relate to "Voyage Schedule search", combine them into one line.
2. **Exactly 4 lines per day.** No more, no less. If there's less work, make lines
   more detailed. If there's more work, combine related items.
3. **Each line should be a complete thought** — describe what was built/fixed/improved,
   not individual commits.
4. **Use action verbs** — "Built", "Developed", "Added", "Fixed", "Created",
   "Implemented", "Enhanced".
5. **Include both frontend and backend context** when a feature spans both — e.g.,
   "Built Profile page — frontend hook, RTK endpoints, route wiring (GET/PUT /api/profile)".
6. **Mention key technical details** that help recall the work — endpoint names,
   component names, patterns used (TDD, RBAC, etc.).
7. **Exclude knowledge-base / documentation work.** Do NOT include wiki ingestion,
   knowledge-base maintenance, note-taking, or documentation-summarizing activity —
   only report actual product work (code commits, CRM/Dataverse changes, deployments,
   bug fixes). This covers billable engineering work, not internal note-keeping.

**What NOT to include (always skip):**

- Wiki / knowledge-base ingestion or scaffolding (e.g. "ingested N docs", "touched N
  wiki pages", "scaffolded wiki structure", building an Obsidian/RAG vault).
- Summarizing, cataloging, or indexing source documents into a knowledge base.
- `log.md` entries whose `{type}` is `ingest`, `scaffold` (wiki), `synthesis`, or
  `lint` — these describe knowledge-base upkeep, not product work.

If a day contains ONLY knowledge-base work and no real commits/CRM changes, say so
plainly rather than dressing the documentation work up as billable engineering.

This per-day summary is the **input** to Step 4 — it is not the final deliverable on
its own.

### Step 4: Always reshape via management-talk

After building the per-day summary, **always** hand it to the `management-talk`
skill to shape it for leadership and the target channel. Do not skip this step and
do not present the raw 4-line summary as the final answer.

1. **Pick the channel.** If the user named a channel (Tribletext entry, Slack,
   async standup, email, JIRA comment, or meeting talking-points), use it. If they
   did not, ask which channel — defaulting to a **Tribletext daily entry** when the
   trigger was invoice/timesheet/Tribletext-related.
2. **Invoke management-talk.** Load the `management-talk` skill (fully qualified:
   `dev-workflows:management-talk`) via your harness's skill-loading mechanism
   (Claude Code: the Skill tool; Antigravity: read its `SKILL.md` when it applies),
   passing the per-day summary from Step 3 as the content to rewrite, along with the
   chosen channel. management-talk owns the audience translation and channel
   formatting.
3. **Output the reshaped result** that management-talk returns as the final
   deliverable. If useful, you may keep the raw per-day summary available as an
   appendix, but the leadership/channel version is the headline output.

### Customization

If the user asks for a different number of lines, different date range, or different
format — adapt accordingly. The 4-line format is the default, not a hard rule.

If the user specifies a date range (e.g., "this week", "last 3 days"), adjust the git
log date filters accordingly. Always convert relative dates to absolute dates for the
git commands.

If the user explicitly asks for the raw commit summary only (no leadership reshaping),
honor that and skip Step 4 — but the default is always to reshape via management-talk.
