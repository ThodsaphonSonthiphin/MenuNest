---
title: Edit surface - where does editing an existing trip live, and how does it commit?
type: grilling
mode: HITL
status: closed
assignee: me
blocked_by: [existing-edit-patterns, shrink-data-loss]
gist: A dedicated EditTripDialog - sibling to CreateTripDialog, sharing its .create-trip-dialog CSS class, NOT a mode on it (create and edit diverge seven ways, incl. dropping the isDaily switch per ADR-137). Opened only from the trip-detail header via an inline-SVG pencil icon on both desktop topbar and mobile header; TripsPage is untouched and the card's fate is left entirely to delete-ux. Commits staged behind an explicit save per ADR-138, dirty-diffed so an unchanged save issues no PUT (updateTrip invalidates TripItinerary on every call, which re-bills Routes+Weather), errors local with the dialog staying open on failure, cancel closes with no warning. TripDateEditor SURVIVES alongside - start date is the one field with two editors - and needs no change, because it passes dayCount through unchanged so it is never a Shrink and ADR-140's guard cannot fire on it.
---

## Question

Where does editing an existing trip live, and how does it commit? The candidates: reuse or extend CreateTripDialog as a save/cancel edit dialog; extend the existing in-place commit-on-change header pattern field by field; or a dedicated edit route. Decide the entry point or points - a trip card on TripsPage, the trip detail header, or both - given that a card is currently one large tap target that navigates. Decide the fate of TripDateEditor: replaced, kept alongside, or subsumed. The shrink-data-loss policy constrains this choice directly, because an immediate-commit day stepper would fire a confirmation on every tap of the minus button.

<!-- decision-map:resolution:start -->
## Resolution

A dedicated EditTripDialog - sibling to CreateTripDialog, sharing its .create-trip-dialog CSS class, NOT a mode on it (create and edit diverge seven ways, incl. dropping the isDaily switch per ADR-137). Opened only from the trip-detail header via an inline-SVG pencil icon on both desktop topbar and mobile header; TripsPage is untouched and the card's fate is left entirely to delete-ux. Commits staged behind an explicit save per ADR-138, dirty-diffed so an unchanged save issues no PUT (updateTrip invalidates TripItinerary on every call, which re-bills Routes+Weather), errors local with the dialog staying open on failure, cancel closes with no warning. TripDateEditor SURVIVES alongside - start date is the one field with two editors - and needs no change, because it passes dayCount through unchanged so it is never a Shrink and ADR-140's guard cannot fire on it.

Detail: docs/adr/141-edit-surface-is-a-dedicated-edittripdialog-from-the-detail-header.md

Resolved HITL via `grill-with-docs` on 2026-08-01. Six questions, each answered by the user;
the canonical record is the two ADRs below.

## Where the decision actually lives

- **ADR-141** — [`docs/adr/141-edit-surface-is-a-dedicated-edittripdialog-from-the-detail-header.md`](../../adr/141-edit-surface-is-a-dedicated-edittripdialog-from-the-detail-header.md) — the surface, the entry point, the affordance, commit behaviour
- **ADR-142** — [`docs/adr/142-start-date-keeps-two-editors-tripdateeditor-survives.md`](../../adr/142-start-date-keeps-two-editors-tripdateeditor-survives.md) — `TripDateEditor`'s fate

No `CONTEXT.md` change: nothing fuzzy got sharpened into a domain term this round.

## The six answers, as given

| # | Question | User's answer |
|---|---|---|
| 1 | What is the edit surface? | **"Dedicated EditTripDialog"** |
| 2 | Where is it opened from? | **"Trip detail header only"** |
| 3 | What happens to `TripDateEditor`? | **"เก็บไว้คู่กัน"** (keep both) |
| 4 | What affordance opens it? | **"ปุ่มไอคอนแก้ไข"** (pencil icon button) |
| 5 | How is a gratuitous itinerary refetch avoided? | **"Dirty-diff อย่างเดียว"** |
| 6 | Cancel with unsaved changes? | **"ปิดเลย ไม่เตือน"** (close, no warning) |

All six matched the recommendation. Q4 was the one where the recommendation deliberately went
*against* an existing ADR's letter — ADR-012's "no separate edit icon" — on the grounds that
ADR-012 governs field-level editing, not a record-level action, and that a "visible editable
treatment" on a plain value is exactly what ships flat through gates blind to visual fidelity
(#46). Recorded explicitly in ADR-141 so it does not read as an oversight.

## The three answers the ticket asked for

- **Where it lives:** a dedicated `EditTripDialog`, sibling to `CreateTripDialog`, sharing its
  `.create-trip-dialog` CSS class. Not a mode, not a shared form body, not a route.
- **How it commits:** staged behind an explicit save (as ADR-138 requires), dirty-diffed so an
  unchanged save issues no PUT, errors kept local to the dialog which stays open on failure,
  cancel closes with no warning.
- **Entry point:** the trip-detail header only, via an inline-SVG pencil icon button, on both the
  desktop `.trip-topbar` and the mobile `.trip-detail-header`. `TripsPage` is untouched.
- **`TripDateEditor`:** kept alongside. Start date is the one field with two editors.

## Verified during the grill, not assumed

- `CreateTripDialog`'s day stepper is **already** staged (`Controller` + `field.onChange`,
  `:238-263`), so ADR-138's staging constraint required no new pattern.
- `updateTrip` invalidates `{type: 'TripItinerary', id}` on **every** call (`api.ts:1365`), so a
  no-op save would force a `getItinerary` refetch that the repo's own comment describes as
  re-billing Google Routes and re-fetching Weather. That is what motivated the dirty-diff, and
  it is a cost this feature would otherwise newly introduce.
- `TripDateEditor` passes `dayCount` through unchanged (`:75-82`), so it is **never** a Shrink and
  ADR-140's guard cannot fire on it. It needs no change and must not be given `allowStopLoss`.

## What this hands to other tickets

- **`daily-trip-editing`** (now unblocked) inherits: the dialog exists and has no `isDaily`
  switch, so it must decide what a daily trip's date field and its permanently-pinned
  `dayCount = 1` do inside it — on top of the ADR-133 funnel already noted on that ticket.
- **`edit-mock`** (still blocked on `delete-ux`) now has a concrete thing to draw: a dialog
  reusing `.create-trip-dialog`, minus the daily switch, plus a pencil icon in two header
  variants, plus ADR-138's confirm.
- **`delete-ux`** keeps full freedom over the trips card. ADR-141 deliberately does no
  structural card work, because a card-level *delete* is the more likely want.

## Risks carried forward, unverified in a browser

1. The header date must re-sync after a dialog save — `TripDateEditor` holds an optimistic local
   value with a re-sync effect (`:64-66`) and relies on the `TripDetail` invalidation. Nothing
   automated will catch it if that misses.
2. Two near-identical dialogs now exist; only the shared CSS class holds them together.

<!-- decision-map:resolution:end -->
