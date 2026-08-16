---
name: career-growth
description: "Quarterly career review that turns your real evidence and a live market survey into a defensible moat and a cert-driven growth plan. Builds an evidence-graded skill inventory (resume, repos, cross-repo git history, held certs/LinkedIn, optional Azure DevOps work items, short gap-fill interview), surveys Thailand + SEA + global-remote job markets with live-verified certificates and a triangulated 3-year outlook, proposes moat candidates that must pass four tests (rare, evidenced, paid, durable), lets the user pick, then plans certs and exam-objective-driven mini projects into a personal career git repo. Trigger when the user wants a career review, skill-gap analysis, certification roadmap or plan, job-market survey, \"what should I learn next\", a moat / unique edge / competitive advantage plan, or says \"พัฒนาสกิลตัวเอง\", \"วางแผน cert\", \"ตลาดแรงงานต้องการอะไร\", \"สร้างจุดเด่น\", \"quarterly career review\". Re-run it every quarter. Precedence: reach for this skill for the full periodic review; reach for `verify-then-advise` for a single verified recommendation, or to check whether one named product or credential is still current."
---

# career-growth

Five stations, full run every time, everything written to the user's **career
repo** (a git repo of their choosing — never this plugin, never the current
project). The user — not this skill — picks the career direction.

Print this pipeline diagram verbatim in your first response of a run:

```
CAREER-GROWTH — five stations, full run every time
──────────────────────────────────────────────────

  ① INVENTORY   evidence-graded skill inventory
  │    resume · repos · git history ·
  │    certs/LinkedIn · ADO (if available) ·
  │    gap-fill interview
  ▼
  ② MARKET      live survey — 3 rings
  │    Thailand · SEA · global remote
  │    certs live-verified · 3-yr outlook
  │    triangulated (≥3 signal types)
  ▼
  ③ GAP + MOAT  inventory × market
  │    candidates argued against 4 tests:
  │    rare · evidenced · paid · durable
  ▼
  ④ PRESENT ⛔  the user picks the moat
  │    (approval gate — nothing below
  │     runs without an explicit pick)
  ▼
  ⑤ PLAN        cert-driven guideline
       readiness-checked, ranked by
       moat-fit ÷ hours still to spend
       mini projects from exam objectives
       → career repo, assisted commit
```

## Non-negotiable evidence rules

All outside-world fact verification — certificate lifecycle, market demand,
claim grading — is delegated to **`verify-then-advise`**; Station 2 runs its
six-stage method rather than career-growth re-deriving a thinner copy of it.
Rules 1–2 below are consequences of running that method; rule 3 draws on
career-growth's own trend-signal taxonomy, with the sibling contributing
claim grading and the counter-signal hunt that stress-test it:

1. **Never answer certificate questions from memory** — every cert is
   live-verified against the vendor's registry before it may be named
   (`verify-then-advise` stage 2).
2. **Every market claim carries its source and a confidence grade** — one of
   `verify-then-advise`'s four grades (Verified-primary / Corroborated /
   Directional / Unverified); an ungraded claim may not appear in
   `market-report.md`.
3. **No 3-year claim without triangulation** — at least three signal types,
   drawn from this skill's own trend-signal taxonomy
   (`references/market-sources.md`); `verify-then-advise` contributes the
   claim-grading scale and the counter-signal hunt that stress-test the case.

One rule has no sibling equivalent and stays entirely career-growth's own:

4. **Personal data never enters this plugin or the current project.** All outputs
   go to the career repo. Commits there are assisted — propose, show, let the user
   approve — never automatic.

## Step 0 — Preflight

1. **Career repo path** — if `$ARGUMENTS` is present and resolves to a usable
   directory, use it as the career repo path; only ask the user for it (or
   confirm from a previous run) when `$ARGUMENTS` is absent or doesn't resolve.
   Also ask for (or confirm) the **resume file path** and the **list of repo
   roots** to scan. If the career repo doesn't exist or isn't a git repo, offer
   to create/`git init` it.
2. Read `growth-state.md` and the four artifacts from the career repo if present
   (see `references/growth-state-contract.md`). They pre-fill this run; they never
   skip a station.
3. Detect the optional ADO source: if the `ado-backlog` plugin's skills are
   available in this session, plan to use its assigned-work view in Station 1
   with `$env:AZDO_SHOW_DONE = "true"` — that view's default output is open
   work, and only its Done/Resolved table is delivered-work evidence;
   otherwise tell the user the ADO source is skipped and continue.
4. Confirm the target market rings — default **Thailand + SEA + global remote**;
   the user may narrow or swap for this run.

## Station 1 — INVENTORY

Build the skill inventory from five sources (skip cleanly what the user lacks):

1. **Resume** — read the file; extract claimed skills, roles, domains.
2. **Repos + git history** — for each repo root: scan commit history (what was
   built, how recently, how often; languages, frameworks, infra). This is the
   corrective to resume claims.
3. **Held certificates + LinkedIn** — ask the user to paste/export; never scrape.
4. **ADO work items** (only if available per preflight) — run the assigned-work
   view with `$env:AZDO_SHOW_DONE = "true"` and list only its **Done/Resolved**
   items as delivered, org-internal evidence. Open or in-progress items are not
   evidence of delivery and must not be graded `verified`.
5. **Gap-fill interview** — ask only what the evidence cannot show, one question
   at a time, from `references/interview-bank.md`, pre-filled from the previous
   `profile.md` so the user corrects rather than re-answers.

Write **`profile.md`** to the career repo: every entry lists its attesting
source(s) and an evidence grade — `verified` (artifact: repo/cert/work item),
`interview-attested`, or `unverified` (resume-only). Open the document with one
overview Mermaid diagram (skill map grouped by evidence grade).

## Station 2 — MARKET

Load the `verify-then-advise` skill via your harness's mechanism and run its
six-stage method in full over each confirmed ring, for the skill areas from
Station 1's inventory plus any adjacent areas confirmed with the user. Two
stages run before the source-list work even starts and are easy to miss if
you assume you already know the method:

- **Inventory the moving parts** (stage 1) — before researching anything,
  list every external entity this round's advice will name (certs, vendors,
  products, market claims) as the verification queue.
- **Compute headline numbers in a script** (stage 6) — any number that
  carries the recommendation is computed from source values, once, in a
  script; never sum rounded per-item parts.

`references/market-sources.md` is the **starting** board and ring list — it
bounds where the survey begins, not where it must stop. Two more of the
sibling's stages reach outside that list by design and are required here,
not optional:

- **Counter-signal hunt** (stage 3) — a counter-signal is by definition not on
  a curated list; look for the contradicting view (independent analysts,
  adoption data, post-mortems) before advising a direction.
- **Institutional-incentive read** (stage 5) — read the user's **employer's**
  partner-program, customer, or team requirements for the skill areas in play,
  and surface any dated cliff running against them. This is what turns a
  personal wish into an employer-funded case, and the sibling names it the
  single most useful output of the method.

Gather: demand (posting counts per ring, primary artifacts counted per stage 4
before citing commentary), the certificates employers name (each live-verified
per stage 2 before it may be mentioned), compensation signals where boards
expose them (aggregator numbers grade `Directional`, per the sibling), the
3-year outlook (triangulated — at least three signal types — with the
AI-absorption assessment stated per skill), and — for every certificate that
survives verification — a **published preparation-hour figure** and whether the
vendor offers a **practice assessment**. Station 5's readiness check needs both;
record "not published" explicitly rather than leaving the field absent.

Write **`market-report.md`** to the career repo: overview Mermaid diagram (rings
× demand), a demand table per ring (skill · postings · source · date ·
**confidence grade**), the verified cert list (code, registry status,
`verified_on`, registry URL, published prep hours or "not published", practice
assessment available yes/no), the counter-signals found (or "looked, found
none"), the institutional-incentive findings (with any dated cliff), and the
triangulated outlook with each signal cited and graded. Also record **what was
not checked** — geographies skipped, sources that blocked, questions left open.
The file is overwritten each run; git history keeps the prior rounds.

## Station 3 — GAP + MOAT

Cross INVENTORY × MARKET, weighted by Station 1's evidence grades: `verified`
skills count as strengths; `interview-attested` ones count as real strengths
whose public evidence is still missing, feeding the `evidenced` test as proof
still to be created rather than proof already held; `unverified` ones count as
gaps to close even when claimed.

- **Gap list** — market-demanded skills the user lacks or holds unverified.
- **Moat candidates** — skill *combinations* (never single hot skills), each with
  its **gap** (what the person is missing for this combination, drawn from the
  Gap list) and a four-test argument, one line per test:
  `rare` (evidence of scarcity in the rings) · `evidenced` (what public proof the
  user has or would gain) · `paid` (demand claims with sources and confidence
  grade — a `Directional` claim may support a direction but must never be the
  sole basis for this verdict) · `durable` (the triangulated 3-year case
  against AI/automation absorption — synthesise
  Station 2's per-skill AI-absorption assessments into one argument for *this
  combination*, not per skill).
- Anything failing a test may appear only as a labeled **supporting skill** —
  never as a moat candidate.

## Station 4 — PRESENT ⛔ approval gate

Present the candidates (a compact table: combination · gap · the four test
verdicts · strongest evidence) and ask the user to **pick one moat, or reject
all**. On reject: collect the objections as constraints and loop back to
Station 3. Never pick for the user; never proceed past this gate without an
explicit pick.

On a pick, write **`moat.md`** to the career repo: the chosen combination, its
gap, its full four-test argument, the rejected candidates (one line each, why),
and an overview Mermaid decision diagram (chosen vs rejected).

## Station 5 — PLAN

For the chosen moat:

1. **Candidate certs** — identify the certificates that evidence the moat, each
   already live-verified in Station 2. If the chosen moat needs a certificate
   Station 2 did not verify, verify it now via `verify-then-advise`'s
   registry-verification stage before naming it — if the registry is
   unreachable or the cert is retired, say so and fall back to the non-cert
   milestone route in item 4 instead of naming an unverified cert. Fetch each
   exam's **study guide** and extract its objective domains. This produces a
   candidate list, **not yet a ranking** — item 2 decides the order.

2. **Readiness check — the person, not just the moat.** Moat fit alone picks
   the cert that best *describes* the destination, which is not the same as the
   cert that best *repays the hours*. For each candidate cert, take its objective
   domains from item 1 and grade every domain against `profile.md`:
   **known** (a `verified` entry already attests it) · **partial** ·
   **unknown** (no entry, or only `unverified` ones). Then:

   - Estimate **remaining hours** from the partial + unknown share weighted by
     each domain's published exam weighting — never from the exam's total
     nominal prep time, which assumes a stranger.
   - **A published practice assessment outranks any estimate.** Where the vendor
     offers one, say so and instruct the user to sit it cold as a measurement
     before the cert is scheduled; the estimate stands only until that number
     exists. Where the vendor offers none — common for a newly released exam —
     state that plainly and label the hour figure **unvalidated**, because
     nothing downstream will re-check it.
   - Record the readiness grade per domain in `growth-plan.md`, not just the
     total. The domain table is what makes a wrong estimate visible next round.

3. **Rank, and show the trade.** Order the candidates by
   **moat-fit ÷ remaining hours**, not by moat-fit alone. A cert the user has
   largely already earned through delivered work can outrank a closer-fitting
   one — most sharply when the user currently holds **no live credential**, or
   when a dated employer/partner cliff from Station 2 lands sooner than the
   closer cert could. Where the two orderings disagree, present both with the
   cost of each in hours and let the user choose; never silently resolve it.
   State any cert dropped from the plan and why, so a later round can revisit.

4. **Mini projects** — design each project *backwards from exam objectives*: the
   project exists to build the knowledge the exam tests; passing the exam is the
   milestone. Aim each project at the domains the readiness check graded
   **unknown** — hours spent on already-known domains buy nothing. Size each to
   the user's stated study hours — if study hours were never captured (the
   interview's *Constraints & preferences* section can be skipped), ask for them
   now before sizing. Offer (never require) to publish each project to a public
   repo when its content allows — record `published_url` when taken.

5. **Non-cert milestones** — any moat component with no matching cert gets an
   explicit alternative milestone (a shipped artifact or delivered work), stated
   in the same pass/fail form. A milestone that costs no study hours (publishing
   existing work, a conversation with an employer, confirming a credential's
   expiry) is listed **first**, since it buys moat progress without spending the
   scarcest resource.

6. Write **`growth-plan.md`** to the career repo (overview Mermaid diagram: certs
   + projects on a quarter timeline; the readiness table per candidate cert with
   its per-domain grades and remaining-hour figure, marked validated or
   unvalidated; the ranking with the trade shown; then per-project sections:
   objective domains covered, milestone, size, publish decision).

7. **Wrap up:** propose the career-repo commit (assisted — show the diff
   summary, let the user approve). On approval, write/finalise
   **`growth-state.md`**'s `last_run` (and the round's other fields) per
   `references/growth-state-contract.md`, **then** commit all five artifacts
   together — `profile.md`, `market-report.md`, `moat.md`, `growth-plan.md`,
   and `growth-state.md` — so the file asserting a committed round exists is
   itself inside that commit. A committed round is what `last_run` means, so
   a crashed run and a declined commit both leave it unchanged: on decline,
   do not write `growth-state.md` at all. Then print the `next_review_due`
   date with a reminder that re-runs are user-initiated. If the user declines
   the commit, say plainly that the run is not recorded as complete and the
   next run's posting-trend-delta signal will have no prior round to diff
   against.

## Failure & degradation

| Situation | Behavior |
|---|---|
| A job board 403s | try the alternates in `references/market-sources.md`; only then report the metric unavailable |
| Vendor cert registry unreachable | withhold cert recommendations — never from memory; keep a previously-targeted cert's existing status and leave its stale `verified_on` in place, noting the verification could not be refreshed. `retired-blocked` is only for a confirmed retirement listing |
| No published prep hours and no practice assessment for a cert | rank it anyway, on the estimate, but label the figure **unvalidated** in `growth-plan.md` and name the missing measurement as a risk — never present an unmeasured estimate as a schedule |
| `ado-backlog` absent | skip the ADO source with an explicit notice |
| No web access at all | the run stops after INVENTORY — Stations 2 through 5 do not run, because without market evidence the `paid` and `durable` tests cannot be argued; say why, never fabricate |
| User rejects all candidates | loop to Station 3 with their objections as constraints |

## Relationship to neighbouring skills

- **`verify-then-advise`** — owns retirement-registry lifecycle verification,
  the counter-signal hunt, primary-artifact counting, the institutional-
  incentive read, and the four-grade claim scale. Station 2 runs its
  six-stage method in full; career-growth owns the person-side inventory,
  the trend-signal taxonomy, and the decision structure (the four-test moat
  argument, the approval gate, the readiness-ranked cert plan).
  Precedence: reach for career-growth for the full periodic review; reach for
  `verify-then-advise` for a single verified recommendation, or to check
  whether one named product or credential is still current.
- **`study-design-verify`** — same evidence-grounded stance, aimed at the
  user's own system (code, schemas, live data) rather than the outside world.
  Station 1's repo/git-history/ADO evidence is the closest overlap; if the
  question turns into "how should this system actually work" rather than
  "what does the person and the market look like", hand off there.
