---
name: triage-findings
description: >-
  Decide WHICH findings become Azure DevOps work items and in what order/batches,
  working from a findings.json produced by extract-findings. Use this AFTER findings
  are extracted and BEFORE creating ADO items — especially when there are more
  findings than you want to file at once. Triggers on "start with the critical ones",
  "which should we file first", "don't dump everything into ADO", "let's do this in
  batches/waves", "only the confirmed ones for now", "hold the needs-review items",
  "scope down the backlog", "prioritize these findings", or any request to pick a
  subset of findings to turn into tickets. Produces a scoped, ordered subset (a
  filtered findings file or a selected/batch flag) and hands it to classify-work-items.
effort: max
---

# Triage findings

You have a `findings.json` (from `extract-findings`). Filing every row blindly creates a
noisy, unprioritized backlog that nobody can action. This skill is the human-in-the-loop
gate: **show the shape of the findings, agree on a scope and order, emit a smaller set**,
then hand off to `classify-work-items`. The win is an auditable *wave* structure — wave 1
is the high-value, confirmed work; later waves pick up the rest after review.

This is a reasoning/filtering step — there is no script to run. You read JSON, summarize,
and write a smaller JSON. See `.agents/references/data-contracts.md` for the
exact `findings.json` shape (the `key`, `severity`, `status`, `kind`, `section` fields).

## 1. Read and profile the findings

Read the `findings.json` the user points at (default name `findings.json` in the working
folder). Then **show counts before asking anything** — people choose well when they can see
the distribution. Break it down across the dimensions that matter for triage:

- **by `severity`**: Critical / High / Medium / Low (and how many have none)
- **by `status`**: e.g. `confirmed` vs `needs-review` (free text — bucket what you see)
- **by `section`**: which areas of the source the findings cluster in
- **by `kind`** (optional): `rename` / `disambiguation` / `missing` / `other`

Present it compactly, e.g.:

```
42 findings total
  severity: Critical 6 | High 14 | Medium 18 | Low 4
  status:   confirmed 30 | needs-review 12
  section:  Cargo Classification 19 | Port Calls 11 | Misc 12
  kind:     rename 25 | disambiguation 9 | missing 5 | other 3
```

## 2. Recommend a default, then follow the user's call

Propose a sensible wave 1 and say why, but **do not impose it** — the user owns the scope:

- **Recommended default: `severity = Critical` AND `status = confirmed` first.** Highest
  value, lowest rework risk. These are the items most worth a ticket today.
- **Hold `needs-review` for a later verification pass.** Filing unconfirmed findings burns
  reviewer trust and creates tickets you may have to close — better to verify, then file as
  wave 2.
- Other common filters to offer: a single `section`, a specific `kind` (e.g. just
  `rename`s), or "everything except Low".

Ask one focused question, e.g. *"Start with the 6 Critical + confirmed as wave 1, and hold
the 12 needs-review for a verification pass? Or a different cut?"* Then honor whatever they
pick — including "file all of them" (in which case just confirm ordering).

## 3. Order within the scope

Within the selected set, order so the most important tickets get created first (and get the
lowest work-item IDs, which reads as priority on the board): **Critical → High → Medium →
Low**, and `confirmed` before anything softer. Stable-sort within a tie by `section` so
related tickets land together. This ordering carries straight into `backlog_input.json`'s
`items` array in the next step.

## 4. Emit the scoped subset

Produce the wave so the rest of the pipeline consumes it unchanged. Two equivalent options —
pick based on how the user wants to track waves:

- **Filtered file (preferred for clean waves):** write `findings.wave1.json` containing the
  same top-level shape (`source`, `keyColumn`) but only the selected, ordered `findings`.
  Wave 2 becomes `findings.wave2.json` later. Each wave is its own auditable artifact.
- **Annotated in place:** add a `"selected": true` and/or `"batch": 1` flag to chosen
  findings in `findings.json`, leaving the rest for later. Useful when you want one file of
  record showing what went into which wave.

Preserve every original field verbatim — above all the **`key`**, which threads through
`backlog_input.json` and `backlog_result.json` so created tickets can be written back to the
right source row (see `data-contracts.md`). Never renumber or invent keys here; triage only
*selects and orders*, it does not rewrite findings.

Minimal example of a filtered wave file:

```json
{
  "source": "Downloads/CRM_Portal_Naming_Audit.xlsx (sheet 'CRM-Portal Audit')",
  "keyColumn": "#",
  "findings": [
    { "key": "1",  "severity": "Critical", "status": "confirmed", "kind": "rename", "current": "Auto", "expected": "Automotive Cargo" },
    { "key": "7",  "severity": "Critical", "status": "confirmed", "kind": "disambiguation", "current": "...", "expected": "..." }
  ]
}
```

## 5. Hand off

Report the wave back to the user (count, the filter applied, the file written) and tell them
the next step: **`classify-work-items`** turns this scoped set into `backlog_input.json`
(mapping each finding to an ADO work-item type and fields). Note any held-back waves so they
are not forgotten — e.g. *"wave 2 = the 12 needs-review findings, after a verification pass."*

## Notes

- If `findings.json` has no `severity`/`status` (e.g. from a prose doc with assigned keys
  `"1","2",...`), triage on `section`/`kind` or simply on count, and say so — don't fabricate
  severities.
- Keep waves small enough to review the created tickets before firing the next wave. A first
  wave of 5–15 items is usually right; 100 tickets in one shot is the anti-pattern this skill
  exists to prevent.
- Triage changes nothing in ADO and writes nothing back — it only shapes the input. Creation
  happens in `ado-create-work-items` (via `create-backlog.cs`), and source write-back in
  `ado-writeback-tracking` (via `tracking.py`).
