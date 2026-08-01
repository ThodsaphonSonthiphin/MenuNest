---
title: Daily trips - what does the edit surface offer when IsDaily is set?
type: grilling
mode: HITL
status: closed
assignee: me
blocked_by: [edit-surface]
gist: On a daily trip EditTripDialog shows all five fields but renders วันเริ่ม and จำนวนวัน DISABLED with their own reasons, never hidden: วันเริ่ม displays today (the treatment TripDateEditor already applies when locked) because the persisted start date is displayed NOWHERE in the SPA - dailyCard has no date row, TripDateEditor is always locked on a daily trip, and GetItinerary projects the date to today - so an editable field there would save successfully and change nothing observable. จำนวนวัน displays 1 with copy saying a daily trip is always single-day, deliberately different from ADR-139 disabled-because-unknown. DailyToggle keeps refusing non-destructively per ADR-133, but its message now names the cost (current day count, and that stops on the removed days go with them) and points at แก้ไข, built from TripDto data already present; it never performs the Shrink itself. The same-tab race is impossible because the dialog is modal, and a cross-tab or MCP flip lands on Reschedule domain guard, which ADR-141 already routes into the dialog error line. Separately and repo-wide (ADR-145): all backend exception messages stay English with no translation layer, so Reschedule keeps its message and ADR-140 refusal is English; the four Thai DomainExceptions in Trips are a known deviation, not precedent.
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

<!-- decision-map:resolution:start -->
## Resolution

On a daily trip EditTripDialog shows all five fields but renders วันเริ่ม and จำนวนวัน DISABLED with their own reasons, never hidden: วันเริ่ม displays today (the treatment TripDateEditor already applies when locked) because the persisted start date is displayed NOWHERE in the SPA - dailyCard has no date row, TripDateEditor is always locked on a daily trip, and GetItinerary projects the date to today - so an editable field there would save successfully and change nothing observable. จำนวนวัน displays 1 with copy saying a daily trip is always single-day, deliberately different from ADR-139 disabled-because-unknown. DailyToggle keeps refusing non-destructively per ADR-133, but its message now names the cost (current day count, and that stops on the removed days go with them) and points at แก้ไข, built from TripDto data already present; it never performs the Shrink itself. The same-tab race is impossible because the dialog is modal, and a cross-tab or MCP flip lands on Reschedule domain guard, which ADR-141 already routes into the dialog error line. Separately and repo-wide (ADR-145): all backend exception messages stay English with no translation layer, so Reschedule keeps its message and ADR-140 refusal is English; the four Thai DomainExceptions in Trips are a known deviation, not precedent.

Detail: docs/adr/144-daily-trip-edit-disables-date-and-day-count-switch-names-the-cost.md

Resolved HITL via `grill-with-docs` on 2026-08-01. Four questions plus one
disambiguation, each answered by the user.

## Where the decisions actually live

- **ADR-144** — [`docs/adr/144-daily-trip-edit-disables-date-and-day-count-switch-names-the-cost.md`](../../adr/144-daily-trip-edit-disables-date-and-day-count-switch-names-the-cost.md)
- **ADR-145** — [`docs/adr/145-backend-error-messages-stay-english-no-translation-layer.md`](../../adr/145-backend-error-messages-stay-english-no-translation-layer.md)
- **CONTEXT.md** — the *Daily trip* entry gained the fact this ticket turned on: a daily
  trip has **no visible start date at all**.

Two ADRs, not one, because the message-language rule is **repo-wide** — burying it inside a
daily-trips ADR would hide it from everyone who needs it.

## The answers, as given

| # | Question | User's answer |
|---|---|---|
| 1 | Does the dialog show วันเริ่ม / จำนวนวัน on a daily trip, and in what state? | **"ปิดทั้งคู่ + บอกเหตุผล"** |
| 2 | The daily switch funnels users into a Shrink — what do we do about it? | **"ข้อความเตือน + ชี้ทาง"** |
| 3 | What does the user see when daily is flipped elsewhere mid-edit? | **"ใช้ guard เดิม + แปลข้อความเป็นไทย"** |
| 4 | Is the Thai/English split a rule, or a one-off fix? | **"เอาข้อความเป็นอังกฤษ"** (free text) |
| 4b | — which reading? | **"backend อังกฤษ ผู้ใช้ก็เห็นอังกฤษ"** |

**Q4 reverses Q3.** Q3 chose to translate `Trip.Reschedule`'s guard into Thai; Q4 then set the
repo-wide rule that backend messages are English and there is no translation layer. The later
decision wins: `Reschedule` keeps its message unchanged. Q4's free-text answer was ambiguous
between "backend English, frontend translates" and "backend English, user sees English", so it
was **not** guessed — the user picked the second in a follow-up.

## Verified during the grill, not assumed

- **A daily trip's persisted start date is displayed nowhere in the SPA — not one pixel.**
  `trip.startDate` renders at exactly one site in the whole frontend (`TripsPage.tsx:46`, inside
  `regularCard`); `dailyCard` has no date row at all (`TripsPage.tsx:22-33`). `TripDateEditor` is
  *always* locked on a daily trip — `currentDay = days.length === 1 && useCurrentTimeAsStart`
  (`TripDetailPage.tsx:99`) and ADR-132 forces that day flag on — so it shows the server-projected
  `overrideDate` and is `disabled` (`TripDateEditor.tsx:95,109`). `GetItineraryHandler` projects
  the date to today and its own comment calls the persisted `Date` "the fallback".
- **`Reschedule` still accepts a new start date on a daily trip** (it throws only on
  `IsDaily && dayCount > 1`), so an editable field there would save successfully and change
  nothing observable. That is what ruled out following `CreateTripDialog`.
- **`CreateTripDialog` already contradicts itself**: with `isDaily` on it disables the stepper but
  leaves the `DatePicker` editable and submits the picked value (`CreateTripDialog.tsx:104`).
  Recorded as a divergence for `edit-mock` not to normalise away.
- **The same-tab race cannot happen** — `CreateTripDialog` is `modal` (`:130`) and ADR-141 makes
  `EditTripDialog` its sibling, so `DailyToggle` sits behind the overlay.
- **Four Thai `DomainException`s exist, all in Trips** (`DeleteTripPlace:27`,
  `RetimeStopToHour:41`, `RetimeStopToWeather:62` and `:80`) against ~80 English ones. They looked
  like an emerging "user-facing guard → Thai" convention; ADR-145 rejects that reading and marks
  them a deviation rather than precedent.
- **`getErrorMessage` has no translation layer** — it passes ProblemDetails `detail` through
  verbatim and its own fallback is English. That is what priced option C out of ADR-145.

## What this changes elsewhere

**ADR-140 was under-specified and is now settled**: its refusal message must name the stop count
and the day range, and it is **English**. Without ADR-145 the SDD implementer would have chosen
a language silently.

## Risks carried forward, unverified in a browser

1. Two different disabled-with-reason states now exist for จำนวนวัน (ADR-139's *unknown count*
   and ADR-144's *daily*). If the mock shows only one, the implementer will reuse its copy for
   both and the daily case will read as a bug.
2. The rewritten `DailyToggle` message is longer than the current one and shares the header's
   error line with `TripDateEditor`. Never rendered in a test — the SPA has no component harness.

<!-- decision-map:resolution:end -->
