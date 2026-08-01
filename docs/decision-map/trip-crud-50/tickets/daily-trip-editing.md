---
title: Daily trips - what does the edit surface offer when IsDaily is set?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [edit-surface]
gist: 
---

## Question

What does the edit surface do for a daily trip (IsDaily, #49)? Daily trips are in scope for editing, but the daily on/off toggle stays where it is on the detail header, so the edit form never sets IsDaily. Decide which fields the form offers for a daily trip, and how it renders the ones that do not apply: dayCount is pinned to 1 and the start date is projected to today, and CreateTripDialog already hides or overrides them when isDaily is set. Decide too what happens if a multi-day regular trip is toggled to daily from the header while the edit surface is open.

## Comment

Finding surfaced while resolving `shrink-data-loss` (2026-08-01). Recorded here, not decided —
it is this ticket's to answer.

**ADR-133 funnels a user straight into the newly-guarded destructive path.** Enabling daily mode
on a multi-day trip is rejected, and the guidance is *"remove the extra days first"* (echoed
verbatim in the MCP `set_trip_daily` tool description: *"a daily trip must be single-day
(dayCount==1) — enabling a multi-day trip is rejected; remove the extra days first"*).

"Remove the extra days" **is** a **Shrink** — the one irreversible destruction in MenuNest. So a
user who merely wants to flip โหมดประจำวัน on a 3-day trip is being told to hard-delete two days
and cascade away every stop on them. Per ADR-138 that shrink now confirms, and per ADR-140 the
API refuses it outright unless the caller opts in — but the *advice itself* still points at it,
and nothing in the daily flow explains the cost.

Worth deciding here:

- Does the daily-enable rejection say what removing those days would actually destroy, rather
  than "remove the extra days first"?
- Is there a supported path at all, or does the user have to leave the daily flow, shrink
  through the edit surface (confirming the loss), then come back and toggle?
- `Trip.Reschedule` also throws on `IsDaily && dayCount > 1`, so a daily trip can never be
  extended either — the edit surface must handle a trip whose day-count control is
  permanently pinned at 1, separately from ADR-139's "disabled because the count is unknown".

Related: ADR-138, ADR-139, ADR-140; ADR-132/133 for the enable semantics.

