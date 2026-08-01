---
title: Visual mock for the trip edit and delete surface, approved before any code
type: prototype
mode: HITL
status: closed
assignee: me
blocked_by: [edit-surface, delete-ux, past-dated-trip-edits]
gist: APPROVED as drawn. The mock lives in Claude Design -> 'MenuNest design system' -> Screens -> 'Issue #50 - Trip edit & delete' (project 8d8d4c81, path screens/issue-50-trip-edit-delete.html, registered in _ds_manifest.json); DS-only, matching the issue-46/47/49 precedent, not docs/mocks. Six panels built from the real .create-trip-dialog CSS rather than approximated: pencil entry in BOTH headers with TripDateEditor still inline beside it; the dialog's normal state; the three disabled-with-a-reason cases drawn SIDE BY SIDE (daily / count-unknown / past-dated-has-nothing-disabled) so the implementer cannot build one and reuse its wording; the shrink confirm naming days+count+place names+the มาแล้ว tag; the delete confirm naming the trip, 'N วัน · M จุดแวะ' and the Discover consequence without claiming stops are deleted; and the save-failure state with the English backend message. Two implementation constraints the mock pins: .ctd-actions must change from flex-end to space-between because ลบทริป sits hard LEFT, and the red-outline delete treatment is deliberate per ADR-143 (a reviewer who 'fixes' it to match the muted .se-delete is undoing a decision). The mock surfaced one live question and it was decided: useConfirm has ZERO custom CSS anywhere in frontend/src, so the two confirms render raw Syncfusion (8px, square buttons) against the dialog's 22px teal - a THIRD visual family alongside teal and orange. Left default, because useConfirm is mounted app-wide via AppLayout and used by Budget/Health, so restyling it either hits every caller with no visual test harness or needs a Trips-only variant; now an out-of-scope line. No ADR - the confirm call is cheaply reversible and everything else is already carried by ADR-138..146.
---

## Question

Produce the visual mock for the trip edit and delete surface, once the surface, the shrink confirmation and the delete confirmation are decided, and get it approved before any code is written. Every trip UI in this repo is mockup-backed - there are more than fifteen trip mocks in docs/mocks and none of them covers edit or delete - and the review gates are blind to visual fidelity, so a task can pass every automated and agent gate and still ship visibly wrong. The mock is the artifact the implementation is diffed against before merge.

<!-- decision-map:resolution:start -->
## Resolution

APPROVED as drawn. The mock lives in Claude Design -> 'MenuNest design system' -> Screens -> 'Issue #50 - Trip edit & delete' (project 8d8d4c81, path screens/issue-50-trip-edit-delete.html, registered in _ds_manifest.json); DS-only, matching the issue-46/47/49 precedent, not docs/mocks. Six panels built from the real .create-trip-dialog CSS rather than approximated: pencil entry in BOTH headers with TripDateEditor still inline beside it; the dialog's normal state; the three disabled-with-a-reason cases drawn SIDE BY SIDE (daily / count-unknown / past-dated-has-nothing-disabled) so the implementer cannot build one and reuse its wording; the shrink confirm naming days+count+place names+the มาแล้ว tag; the delete confirm naming the trip, 'N วัน · M จุดแวะ' and the Discover consequence without claiming stops are deleted; and the save-failure state with the English backend message. Two implementation constraints the mock pins: .ctd-actions must change from flex-end to space-between because ลบทริป sits hard LEFT, and the red-outline delete treatment is deliberate per ADR-143 (a reviewer who 'fixes' it to match the muted .se-delete is undoing a decision). The mock surfaced one live question and it was decided: useConfirm has ZERO custom CSS anywhere in frontend/src, so the two confirms render raw Syncfusion (8px, square buttons) against the dialog's 22px teal - a THIRD visual family alongside teal and orange. Left default, because useConfirm is mounted app-wide via AppLayout and used by Budget/Health, so restyling it either hits every caller with no visual test harness or needs a Trips-only variant; now an out-of-scope line. No ADR - the confirm call is cheaply reversible and everything else is already carried by ADR-138..146.

## The approved artifact

**Claude Design → `MenuNest design system` → Screens → "Issue #50 — Trip edit & delete"**
Project `8d8d4c81-41c1-4e0a-a0b7-370b39dfbe70`, path `screens/issue-50-trip-edit-delete.html`.
Retrieve with `DesignSync get_file`. Registered in `_ds_manifest.json` — without that entry the
`@dsCard` marker alone leaves a new card invisible.

Per ADR-032 and the ui-mockup mechanism the Claude Design project is the home; this follows the
precedent of issue-46 / -47 / -49, which also live only there and not in `docs/mocks/`.

## What it pins

Six panels, styled from the real `.create-trip-dialog` CSS (teal `#0e8f9e`, 22px radius, 46px badge,
`44px 1fr 44px` stepper, teal-soft summary pill) rather than approximated:

- **A — entry points.** Pencil icon button in both headers: light-on-dark in the desktop
  `.trip-topbar`, teal-on-white in the mobile `.trip-detail-header`. `TripDateEditor` still inline
  beside it, so start date visibly has two editors (ADR-142).
- **B — the dialog, normal state.** Five fields, no daily switch, live end-date pill. **The footer
  splits**: `ลบทริป` hard left in red-outline, `ยกเลิก` / `บันทึก` right. This is a change to
  `.ctd-actions`, which is `justify-content: flex-end` today and must become `space-between`.
- **C — the three disabled-with-a-reason cases together.** C1 daily (both fields, different copy
  each); C2 count-unknown (day count only — the other three stay live); C3 past-dated trip, which
  has **nothing** disabled and shows the `minDate` calendar instead. Drawn side by side
  deliberately: ADR-144 warned the implementer would otherwise build one and reuse its wording.
- **D — shrink confirm.** Day range, stop count, place names, and the `มาแล้ว` tag on the visited one.
- **E — delete confirm.** Trip name + `3 วัน · 8 จุดแวะ` + the Discover consequence, with copy that
  never claims the stops are deleted.
- **F — save failure.** Dialog stays open, error inside it, backend message in English (ADR-145).

## The one question the mock surfaced

`useConfirm` has **zero custom CSS** — no dialog class, no stylesheet rule anywhere in `frontend/src`.
It renders raw Syncfusion (8px corners, square buttons) while `EditTripDialog` is 22px and teal, so
panels D and E genuinely look like a different product from panel B. With teal and orange already in
play that is a **third visual family**.

Decided: **leave them default.** `useConfirm` is mounted app-wide via `AppLayout` and used by Budget,
Health and others, so restyling either changes every caller's confirms with no visual test harness
anywhere, or needs a Trips-only variant. The mismatch is pre-existing and is not #50's to fix. Now
recorded as an out-of-scope line on the map.

## Approval

Asked directly whether the mock stands as the artifact the implementation gets diffed against.
Answer: **"Approved as drawn."** The confirm-styling question was answered separately and first —
**"Leave them default"**, the option whose text reads *"exactly as drawn"*.

No ADR written. The confirm-family call is cheaply reversible, which fails the first of
grill-with-docs' three tests; everything else the mock pins is already carried by ADR-138 through
ADR-146.

## Carried into implementation

- **`.ctd-actions` must become `space-between`** with a right-hand group — the delete button cannot
  sit in the existing `flex-end` row.
- **The red-outline delete treatment is deliberate** (ADR-143), breaking the muted `.se-delete`
  precedent. A reviewer who "fixes" it to match `.se-delete` is undoing a decision.
- **Diff the built CSS against this card before merge.** The gates are blind to visual fidelity —
  #46 shipped flat straight through SDD review and `/scrutinize`.
- **Two interactive checks from ADR-146 are drawn in red on panel C3**: a past trip's out-of-range
  value must still display, and must not fire `onChange` into the dirty-diff.

<!-- decision-map:resolution:end -->
