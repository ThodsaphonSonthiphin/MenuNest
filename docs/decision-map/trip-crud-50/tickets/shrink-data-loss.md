---
title: Shrinking the day count destroys scheduled stops - confirm, block, or allow?
type: grilling
mode: HITL
status: closed
assignee: me
blocked_by: [field-change-effects]
gist: Confirm-and-proceed, but only when stops are really at risk: the day-count field is STAGED (never commit-on-change - ADR-013's cheap-re-pick rationale fails on an unrecoverable field) and one confirm fires on save naming the day range, the stop count, the place names and any already-visited stops. The control is live ONLY where the itinerary is already cached (never fetch getItinerary just to price a confirm - that re-bills Routes+Weather), and is disabled while the count is unknown rather than defaulting to zero. Guard also lands server-side: UpdateTripCommand gains a trailing AllowStopLoss=false and refuses a stop-destroying shrink with a message naming count+days; MCP exposes the flag so the agent path stops being silent.
---

## Question

Reducing a trip's day count deletes the trailing ItineraryDays and Stop rows cascade with them. UpdateTripHandler names this as silent data loss and says outright that an edit UI must confirm before shrinking a trip that has stops on the days being removed. Decide the policy: confirm and proceed, naming exactly what will be lost; hard-block the shrink until the user clears those days themselves; or allow it with an undo path. Decide whether the at-risk stop count is shown to the user, and what happens when that count cannot be known because the surface has not loaded the itinerary. This policy is a hard constraint on the edit surface's commit semantics, so settle it before the surface.

<!-- decision-map:resolution:start -->
## Resolution

Confirm-and-proceed, but only when stops are really at risk: the day-count field is STAGED (never commit-on-change - ADR-013's cheap-re-pick rationale fails on an unrecoverable field) and one confirm fires on save naming the day range, the stop count, the place names and any already-visited stops. The control is live ONLY where the itinerary is already cached (never fetch getItinerary just to price a confirm - that re-bills Routes+Weather), and is disabled while the count is unknown rather than defaulting to zero. Guard also lands server-side: UpdateTripCommand gains a trailing AllowStopLoss=false and refuses a stop-destroying shrink with a message naming count+days; MCP exposes the flag so the agent path stops being silent.

Detail: docs/adr/138-day-count-shrink-confirms-when-stops-at-risk-staged-not-autosaved.md

Resolved HITL via `grill-with-docs` on 2026-08-01. Eight questions, each answered by the
user; the canonical record is the three ADRs below plus the new `CONTEXT.md` term.

## Where the decision actually lives

- **ADR-138** — [`docs/adr/138-day-count-shrink-confirms-when-stops-at-risk-staged-not-autosaved.md`](../../adr/138-day-count-shrink-confirms-when-stops-at-risk-staged-not-autosaved.md) — the policy
- **ADR-139** — [`docs/adr/139-day-count-editable-only-where-the-itinerary-is-already-cached.md`](../../adr/139-day-count-editable-only-where-the-itinerary-is-already-cached.md) — where the control may live, and the unknown-count rule
- **ADR-140** — [`docs/adr/140-updatetrip-refuses-a-stop-destroying-shrink-unless-allowstoploss.md`](../../adr/140-updatetrip-refuses-a-stop-destroying-shrink-unless-allowstoploss.md) — the server-side guard and MCP
- **`CONTEXT.md`** — new glossary term **Shrink**, with `Reschedule` and `delete` on its _Avoid_ list

## The eight answers, as given

| # | Question | User's answer |
|---|---|---|
| 1 | Confirm, block, or allow with undo? | **"Confirm, then proceed"** |
| 2 | Does the confirm fire when the dropped days are empty? | **"Only when stops at risk"** |
| 3 | What guarantees the at-risk count is knowable? | **"Restrict to where it's loaded"** |
| 4 | What happens while the count is unknown? | **"Disable that one control"** |
| 5 | What does the confirm name? | **"Days + count + place names"** |
| 6 | How does the day-count field commit? | **"Staged, one confirm on save"** |
| 7 | SPA-only guard, or API too? | **"Guard at the API too"** |
| 8 | What does MCP do with the flag? | **"Expose the flag to the agent"** |

**Q7 went against the recommendation, deliberately.** The recommendation was SPA-only plus a
regression test; the user chose defence in depth at the API. That is the stronger position —
it closes the MCP path, which destroys stops silently today with only a tool description as a
warning — and it costs less than it was priced at, because `AllowStopLoss` is a *trailing
defaulted* parameter, so no existing construction site has to change.

## Scope note

The ticket's question was answered as asked; it was not widened. Q6 (commit semantics) was
deliberately scoped to the **day-count field only** — where the edit form lives and how its
other fields commit stays `edit-surface`'s decision. ADR-138 hands that ticket exactly one
hard constraint: day count cannot autosave.

## What this hands to other tickets

- **`edit-surface`** (now unblocked) inherits two constraints: day count must be **staged
  behind an explicit save**, not an in-place commit-on-change editor à la `TripDateEditor`;
  and the day-count control may only be **live where the itinerary is already in the RTK
  cache**, so a trips-list card action cannot change it.
- **`daily-trip-editing`** inherits a finding this ticket surfaced but did not decide:
  **ADR-133 rejects enabling daily mode on a multi-day trip with "remove the extra days
  first"** — which funnels the user straight into the destructive shrink. That flow needs an
  answer there.
- **`edit-mock`** must render the confirm itself, not just the form: day range, stop count,
  capped place-name list (`…และอีก N แห่ง`) inside a 420px dialog, and the distinct
  already-**มาแล้ว** line.

## Two risks carried forward, both unverified in a browser

1. `ConfirmProvider`'s Dialog is portaled to `document.body` and inline-styled, so page-scoped
   `.trips-page` / `.trip-detail` tokens will not resolve inside it.
2. It was never confirmed that the provider's Dialog renders **above** `.itin-reorder-overlay`
   (`z-index: 1200`). Verify interactively before trusting the confirm to be visible.

<!-- decision-map:resolution:end -->
