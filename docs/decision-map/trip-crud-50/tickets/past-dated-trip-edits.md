---
title: A trip whose start date is already past - may its start date or day count be edited at all?
type: grilling
mode: HITL
status: closed
assignee: me
blocked_by: []
gist: A past-dated trip is FULLY editable - name, day count and travel mode change normally, and its date may still move FORWARD - but a Backdate (a Reschedule whose NEW date lands in the past) is refused. What is governed is where the date LANDS, never which direction it moved, so the relational 'shift backward' test 14 Nov->12 Nov stays valid. UpdateTripHandler throws when StartDate CHANGES to a date before UtcNow.AddDays(-1) - the same floor and the same explanatory comment as RetimeStopToHourHandler, giving one past-date semantic across both writers of Trip.StartDate; an unchanged date always passes, which is what keeps renaming a past trip working under the full-replace PUT. Server-side only, NO override flag (the ADR-140 AllowStopLoss shape was rejected) and MCP inherits the refusal. Both pickers - EditTripDialog's and TripDateEditor's - set minDate={today} so the guard is a backstop nobody normally reaches; this AMENDS ADR-142, whose 'needs no change' was verified against ADR-140 only. Day count carries no past-specific restriction and the edit surface shows nothing past-specific. Chosen for cross-handler consistency AGAINST the evidence: create has no past-date rule at all, Retime's guard exists to stop a SILENT move rather than a past one, and the 'silent degradation' premise is false - moving forward past 240h flips weather to No-data identically.
---

## Question

UpdateTripHandler will happily move a trip into the past: it has NO past-date guard, while RetimeStopToHourHandler - the other writer of Trip.StartDate, using the same DayRealigner - does have one (RetimeStopToHourHandler.cs:40-41). ADR-140 has now established that UpdateTrip can carry domain guards, so the asymmetry is a live choice rather than an oversight. Decide: may a past-dated trip's start date and/or day count be edited, freely or not at all; does UpdateTrip inherit the past-date guard, and if so does it apply to the whole command or only to a backward move; and what does the edit surface show for a past trip - the ADR-139 disable-one-control-with-a-reason pattern is available. Note the silent degradation this interacts with: moving a trip into the past flips every stop's on-arrival weather to 'past' and renders No-data (weather.ts:8-12,57-58), and can add or remove season warnings and opening-hours flags on stops nobody touched.

<!-- decision-map:resolution:start -->
## Resolution

A past-dated trip is FULLY editable - name, day count and travel mode change normally, and its date may still move FORWARD - but a Backdate (a Reschedule whose NEW date lands in the past) is refused. What is governed is where the date LANDS, never which direction it moved, so the relational 'shift backward' test 14 Nov->12 Nov stays valid. UpdateTripHandler throws when StartDate CHANGES to a date before UtcNow.AddDays(-1) - the same floor and the same explanatory comment as RetimeStopToHourHandler, giving one past-date semantic across both writers of Trip.StartDate; an unchanged date always passes, which is what keeps renaming a past trip working under the full-replace PUT. Server-side only, NO override flag (the ADR-140 AllowStopLoss shape was rejected) and MCP inherits the refusal. Both pickers - EditTripDialog's and TripDateEditor's - set minDate={today} so the guard is a backstop nobody normally reaches; this AMENDS ADR-142, whose 'needs no change' was verified against ADR-140 only. Day count carries no past-specific restriction and the edit surface shows nothing past-specific. Chosen for cross-handler consistency AGAINST the evidence: create has no past-date rule at all, Retime's guard exists to stop a SILENT move rather than a past one, and the 'silent degradation' premise is false - moving forward past 240h flips weather to No-data identically.

Detail: docs/adr/146-updatetrip-refuses-a-backdate-and-both-pickers-stop-offering-one.md

## How it was decided

Five HITL choices, in order. The user's answers verbatim:

1. Policy — **"Refuse a backward move"**, chosen over *no guard at all* (my recommendation), *no guard + a dialog warning*, and *lock a past trip entirely*.
2. Enforcement — **"Server, no override"**, chosen over the ADR-140 `AllowStopLoss` shape and over SPA-only.
3. SPA surface — **"minDate on both pickers"**, chosen over reacting to the server error and over dialog-only.
4. Day count — **"No extra restriction"**, chosen over disabling it on a past trip and over grow-yes-shrink-no.
5. Floor — **"use as comment"**, clarified to **"ใช้เหมือนของเก่า"** — the same floor as the existing handler, `UtcNow.AddDays(-1)`, with the rationale carried as a code comment the way `RetimeStopToHourHandler.cs:36-39` already does.

I recommended **no guard** on question 1 and said so; the user chose the guard for cross-handler
consistency. Recorded here because the ADR's Context section argues against its own Decision, and a
future reader should know that is deliberate rather than a drafting error.

## Evidence the grilling turned up

- `CreateTripValidator` has **no date rule**, and `CreateTripDialog.tsx:220-224` renders its
  `DatePicker` unbounded — creating a trip that starts in 2020 is three taps. The guard therefore
  makes edit strictly weaker than create, and is knowingly bypassable.
- `RetimeStopToHourHandler.cs:36-39`'s guard is about **silence**, not about past dates: retiming is
  an *implicit* date move (the user picks an hour, not a date). `EditTripDialog` has no such problem.
- `UpdateTrip` is a **full replace**, so a guard reading "refuse if `StartDate` is past" would fire
  when renaming last month's trip. Only a *changed*-date check works. This is why the rule keys on
  where the date **lands** and on whether it **changed** — never on direction.
- `UpdateTripHandlerRelationalTests.cs:95-101` is a **"shift backward"** test (`14 Nov → 12 Nov`).
  It survives only because the rule is "lands in the past", not "moves earlier".
- The ticket's "silent degradation" premise is **false as a past-specific claim**: moving a trip
  forward past 240h flips on-arrival weather to No-data by the same branch (`weather.ts:9-11`,
  `useStopWeather.ts:53-55`). Weather loss is a horizon effect, symmetric both ways. Season and
  opening-hours changes are the correct answer to a new date, not damage.
- Nothing in the app treats a past trip as a record: `ListTripsHandler` sorts
  `OrderByDescending(StartDate)` and past trips sink down the same list forever — no archive, no
  completed state, no dimming.
- Syncfusion's prop is **`minDate`** (from `CalendarBaseProps`), not `min`; it gates selectability
  (`datepicker.js:41`). `strictMode` must stay `false` — it "auto-corrects" invalid values.
  **No Syncfusion picker in this repo sets `minDate` today.**

## Carried forward

- **CONTEXT.md gains `Backdate`** — a `Reschedule` whose new date lands in the past — with an
  `_Avoid_` line separating it from *past trip* (a state, not an edit) and from the direction words.
- **ADR-142 is amended** in place, so a reader of 142 alone does not inherit the stale
  "needs no change" claim about `TripDateEditor`.
- **`UpdateTripHandler` gains `IClock`.** Both test `Build` helpers change. Never wire the system
  clock into them — every date in both files is a hardcoded `2026-11-x` / `2026-12-x`, so a real
  clock detonates the suite in December 2026.
- **Two interactive checks before push** (no rendering harness will catch either): a past trip's
  out-of-range value must still *display* in the field, and must not fire an `onChange` that the
  ADR-141 dirty-diff reads as an edit — that would silently move the trip to today.
- **`edit-mock` inherits nothing new to draw.** The edit surface has no past-specific state: no
  disabled control, no banner. `minDate` does all the work, and `จำนวนวัน` keeps exactly the two
  disabled-with-reason states ADR-139 and ADR-144 already defined.

<!-- decision-map:resolution:end -->
