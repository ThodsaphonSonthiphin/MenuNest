---
name: reflect
description: >-
  Session retrospective that turns THIS session's problems into durable,
  correctly-routed improvements. Trigger on /reflect, "what did we learn",
  "retro", "retrospective", "improve our workflow", "how do we not repeat
  this", or after a painful debugging round. It captures the DELTA (what
  went wrong, what was slow, what got corrected) and routes each lesson to
  where it will fire again: an owned skill, a project CLAUDE.md, a cross-project
  GOTCHAS.md file, or memory.
  NOT a what-was-done summary (that is invoice-generator) and NOT a single
  bug's root-cause record (that is post-mortem).
effort: max
---

# reflect — turn this session's problems into durable improvements

Every session teaches something. This skill harvests those lessons and files
each one where it will actually fire next time, so the workflow compounds.

It is **problem-focused**, not a summary. If you want "what did I do", that is
`invoice-generator`. If you want one bug's canonical record, that is
`post-mortem`. `reflect` asks: **what should change about how we work?**

## When NOT to run

- Trivial or purely conversational sessions — concluding "no durable lesson"
  and exiting is a valid, first-class outcome. Do not manufacture findings.
- As a stand-in for `invoice-generator` or `post-mortem`.

## Scope

Analyze the **current conversation context**. Do NOT parse transcript files
from disk. If the session was compacted, work from what remains and say so:
"analysis limited to post-compaction context".

## Stage 1 — Harvest

Scan the session for four signal types:

| Signal | Definition |
|---|---|
| Correction | The user corrected the approach, output, or understanding. |
| Friction | Repeated manual steps, wrong tool first, retries, permission churn. |
| Skill failure | A skill was used but guided wrong/insufficiently, OR should have triggered and did not. |
| Skill gap | A recurring workflow done ad-hoc that no skill covers. |

These four are prompts to notice lessons, not an exhaustive partition — capture
any recurring pain even if it does not fit a row cleanly.

For each finding record: **what happened**, **evidence** (a concrete moment),
**cost** (rework / time / tokens), **proposed lesson**.

Cap at ~5 findings, ranked by cost. High-signal, not exhaustive. If nothing
clears the bar, say so and exit.

## Stage 2 — Research & route

Per finding, run up to three passes, gated by type so tokens are not wasted:

- **Pass A — Local overlap (always).** Search owned skills, the auto-memory
  index (MEMORY.md), and the relevant project CLAUDE.md. Update beats create.
- **Pass B — External prior art (only for new-skill candidates).** Search
  GitHub (`gh search repos` / `gh search code`) for existing Claude skills and
  SKILL.md patterns, and the web (WebSearch/WebFetch) for the pattern. Outcome:
  adopt (good skill exists — reference it), inform (borrow patterns), or
  greenfield (build it).
- **Pass C — Technical verification (only when the lesson is a technical
  claim).** Verify against official docs, the actual code, and GitHub
  issues before persisting. Never persist a wrong fix forever.

Research guardrails: external findings are read-only input to proposals
(nothing auto-applied); bounded to a couple of targeted searches per finding
(inconclusive -> "no strong prior art found", proceed, never block); attribute
any borrowed source by URL/repo.

Assign each finding exactly one route:

| Route | Destination | When |
|---|---|---|
| A | Update existing skill | An owned skill should have prevented this. |
| B | New skill (hand to skill-creator) | Recurring workflow, no skill, no good external one to adopt. |
| C | Project CLAUDE.md | This project's own convention, architecture, or project-only gotcha (this repo only). |
| D | Auto-memory | Personal preference or a single-project fact. |
| E | Discard | One-off noise. |
| F | Global gotcha (`~/.claude/GOTCHAS.md`) | A cross-project tooling / environment / harness trap. |

**F vs C vs D — one-line test.** Ask *"if I did this in another project, would
this same thing bite me?"* **Yes → F** — it fires everywhere via the
`@`-imported `~/.claude/GOTCHAS.md`. **No, it's this repo's own rule → C.** **A
preference or one-project fact → D.** Route F explicitly reclaims the
cross-project tooling/environment lessons that used to default to D (D's store
is keyed by project directory, so they never surfaced in other projects).

**Ownership guardrail:** Route A applies ONLY to skills you own — any plugin in
this repo (`dev-workflows`, `ado-backlog`, `github-backlog`, ...) and personal
skills under `~/.claude/skills`. Third-party skills (superpowers,
skill-creator, Microsoft plugins) are READ-ONLY; their lessons become a Route D
memory or a Route C CLAUDE.md override instead.

**Route F writes global config.** Route F targets the user's global Claude
config — `~/.claude/GOTCHAS.md` plus one `@~/.claude/GOTCHAS.md` import line in
`~/.claude/CLAUDE.md`. The user owns these, so writing is allowed, but the edit
to the personal `CLAUDE.md` is **announced before it happens** (see Stage 4) —
the same transparency Route D memory writes get.

## Stage 3 — Present & approve

Present a numbered list. Each item: **finding -> evidence -> route -> concrete
proposed change** (actual replacement text or diff, plus any cited source —
never "improve X"). The user replies which numbers to apply (all / some / none).

## Stage 4 — Apply (approved items only)

- **Route A (owned skill):** edit the skill SOURCE, never the installed cache.
  Then, by where the skill lives:
    - **A plugin in this repo** (e.g. `dev-workflows`, `ado-backlog`,
      `github-backlog`): edit under `C:\Repo2\workflow daily work` and bump the
      OWNING plugin version in BOTH its own
      `plugins/<plugin>/.claude-plugin/plugin.json` and its entry in the
      repo-root `.claude-plugin/marketplace.json` (keep the two identical).
    - **A personal skill under `~/.claude/skills`**: edit in place — there is
      no plugin manifest or version to bump.
  Either way the edit will not load until the cache is re-synced and Claude Code
  restarts — perform the file steps, then TELL the user a restart is
  required (you cannot restart the session). See the
  `claude-skills-resync-mechanism` memory for the copy + installed_plugins.json
  procedure.
- **Route B (new skill):** hand off to `skill-creator` / `writing-skills` — do
  not hand-roll a SKILL.md that duplicates their job.
- **Route C (CLAUDE.md):** edit the relevant repo's CLAUDE.md, matching its
  structure.
- **Route D (memory):** write the memory file and add a one-line MEMORY.md
  pointer, per the memory schema; check for an existing file to update first.
- **Route E:** nothing.
- **Route F (global gotcha -> `~/.claude/GOTCHAS.md`):** the destination is a
  standalone, cross-project file that Claude Code auto-loads in every session
  via an `@` import in the global `~/.claude/CLAUDE.md`. Provision it lazily and
  idempotently:
    1. **Ensure the file.** If `~/.claude/GOTCHAS.md` is missing, create it with
       a short header (title + one line: auto-loaded everywhere via `@` in
       ~/.claude/CLAUDE.md, one gotcha = one line, grouped by area, update in
       place). If it exists, never clobber it.
    2. **Append or update -- a forward-looking WARNING, not an event log.** A
       gotcha exists to steer future behavior, and this file loads on EVERY turn,
       so every word is a per-turn tax. Write the **trap -> fix**, not the story:
       `- **<trap, stated so future-you recognizes it>** -- <fix / rule>. (YYYY-MM-DD)`
       Target ~1 line (2 only when the fix truly needs it). Keep the recognizable
       symptom and the fix; **cut the incident narrative** -- no "hit twice",
       "chased X once", blow-by-blow, or saying the same thing twice. The story,
       evidence, and timeline go in the Stage 5 reflections record (and a
       post-mortem for a full bug), NOT here. A tiny parenthetical for credibility
       is fine; a paragraph is not. Before adding, search for the bold `<trap>`;
       if present, UPDATE that line in place (refine + re-date) instead of
       duplicating. Never auto-delete; the date supports manual review. No Mermaid
       diagram (convention-exempt like MEMORY.md, ADR 0030). Any literal `@path`
       written INTO this file must be backticked, or it would itself be
       re-imported.
    3. **Ensure the import (first time only, announced).** If `~/.claude/CLAUDE.md`
       has no bare `@~/.claude/GOTCHAS.md` line, first TELL the user: "adding one
       `@import` line to your global CLAUDE.md so gotchas auto-load in every
       project — Claude Code will ask you to approve the import on next start,
       please approve it." Then append the line at end of file, written **plain**
       (never inside backticks / a code fence, or it will not import). A newly
       filed gotcha is **inert until the next session** — it loads only after a
       restart and after the user approves that import; do NOT report the gotcha
       as already live. If global gotchas ever stop loading, the import was
       likely declined (Claude Code will not re-prompt) — re-enable it in Claude
       Code settings.

Note: writes into `C:\Repo2\workflow daily work` and other non-glasshull paths
are blocked for the Write/Edit tools by the mobile-app write-guard hook — use
PowerShell here-strings or Bash for those.

## Stage 5 — Record & commit offer

- Append a terse block to `docs/reflections/YYYY-MM.md` in the plugin repo
  (create `docs/reflections/` on first use). One block per session: date,
  project, findings (one line each — tag the route, e.g.
  `[Route F · GOTCHAS.md] <title>` for a global gotcha), applied vs skipped, cited
  sources.
  Short and greppable — not a narrative.
- Run the commit offer (assisted, never automatic): write files first, then ask
  before staging/committing. Respect workspace git rules — confirm the target
  repo before committing when the path is a non-repo root or one of several
  sub-repos.
