# Design — Edit a Trip's start date from the detail header

**Date:** 2026-07-04
**Status:** Implemented
**Related:** ADR-012 (inline tap-to-edit), ADR-013 (commit-on-change), ADR-009 (trip
schedule cascade / add-remove trailing days)
**Issue:** [#9](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/9)

## 1. Problem

A trip's dates were **read-only** in the SPA. The desktop top-bar rendered a derived
Thai date range (`formatTripDates`), the mobile header showed no date at all, and
there was no affordance anywhere to change when the trip happens. Yet the whole back
half already existed and was unused:

| Layer | Location | State |
|---|---|---|
| Domain `Trip.Reschedule(startDate, dayCount)` | [Trip.cs:58](../../../backend/src/MenuNest.Domain/Entities/Trip.cs#L58) | ✓ |
| `UpdateTripHandler` — reschedules + realigns itinerary days | [UpdateTripHandler.cs](../../../backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs) | ✓ |
| `PUT /api/trips/{id}` | [TripsController.cs:42](../../../backend/src/MenuNest.WebApi/Controllers/TripsController.cs#L42) | ✓ |
| RTK `useUpdateTripMutation` (invalidates `Trips` / `TripDetail` / `TripItinerary`) | [api.ts:1262](../../../frontend/src/shared/api/api.ts#L1262) | ✓ but **never called** |

**This is a frontend-only feature.** No backend, DB, or API change — it wires the
existing `useUpdateTripMutation` to a new inline editor, mirroring the day-start editor
(ADR-012/013).

## 2. Goal / non-goals

**Goal:** Let the user set the trip's **start date** by tapping the date value in the
detail header (desktop top-bar and mobile header); the trip reschedules immediately and
the itinerary days realign server-side.

**Non-goals:** editing `dayCount` / trip length here (the create dialog owns that, and
shrinking a trip silently drops stops per the `UpdateTripHandler` warning — out of scope
until a confirming edit-trip UI exists); renaming; changing destination or travel mode;
Thai Buddhist-era rendering inside the picker (the create dialog already ships an en
Gregorian Syncfusion `DatePicker`, so this matches).

## 3. Overview

```mermaid
flowchart LR
    U["User taps<br/>start date"] --> DP["Syncfusion DatePicker<br/>(dd MMM yyyy)"]
    DP -->|onChange| TDE["TripDateEditor<br/>optimistic local value"]
    TDE -->|useUpdateTripMutation<br/>(dayCount + others unchanged)| API["PUT /api/trips/{id}"]
    API -->|invalidates TripDetail + TripItinerary| Q["getTrip / getItinerary refetch"]
    Q --> CS["days realign to new start<br/>(UpdateTripHandler)"]
    CS --> HDR["header date + itinerary update"]
    API -.->|error| REV["revert local value<br/>+ .trips-field-error"]
```

## 4. Changes, file by file

### 4.1 New — `frontend/src/pages/trips/utils/date.ts` (+ `date.test.ts`)

TZ-stable converters + inclusive end-date derivation, unit-tested to lock the round-trip
against UTC drift (mirrors `utils/time.ts`):

- `ymdToDate(ymd)` — `"yyyy-MM-dd"` (tolerating a time suffix) → local-midnight `Date`.
- `dateToYmd(date)` — `Date` → `"yyyy-MM-dd"` using **local** fields (no `toISOString`).
- `endDate(start, dayCount)` — inclusive end = `start + (dayCount − 1)` days.

### 4.2 New — `frontend/src/pages/trips/components/TripDateEditor.tsx`

```
Props: { trip: TripDto; onError: (msg: string | null) => void }
```

- Renders a Syncfusion `DatePicker` (`editable={false}`, `openOnFocus`,
  `clearButton={false}`, `format="dd MMM yyyy"`) — the same three-prop treatment as
  `DayStartEditor` so it reads as a header label that opens the calendar on tap. Chrome
  is neutralized in CSS to blend into each header.
- **Optimistic local state** seeded from `trip.startDate`; a `useEffect` re-syncs it to
  the server value after the refetch. On pick: set locally, then call `updateTrip` with
  the new `startDate` and **the trip's current `name` / `destination` / `dayCount` /
  `defaultTravelMode` carried through unchanged** — so only the schedule shifts and no
  itinerary days are dropped.
- **Success**: `TripDetail` + `TripItinerary` invalidate → `getTrip` / `getItinerary`
  refetch → days realign; clear any prior error via `onError(null)`.
- **Error**: revert the local value to `trip.startDate` and `onError(getErrorMessage(err))`.
- A `mounted` ref guards the async resolution from touching parent state after unmount.
- For multi-day trips it also shows the derived inclusive **end date** (static, same
  `dd MMM yyyy` format) so the window stays visible.

### 4.3 Edit — `TripDetailPage.tsx`

- Add a `dateError` state.
- **Desktop top-bar**: replace the joined `formatTripDates(...)` meta string with
  discrete spans — `destination ·` + `<TripDateEditor>` + `· N วัน` — plus a
  `.trip-topbar-error` line. `formatTripDates` / `TH_MONTHS` are removed (now unused).
- **Mobile header**: the meta previously showed only `destination · N วัน`; it now also
  carries `<TripDateEditor>` between them, with errors under it via `.trips-field-error`
  (the same class the sibling `ItineraryTab` uses on this page).

### 4.4 Edit — `trips-tokens.css`

Add rules for `.trip-date-edit` / `.trip-date-picker` targeting the React package's
`sf-*` classes (not the legacy ej2 `.e-*`). Base rules neutralize the input chrome and
size it to content; the **dark top-bar** (`.trip-topbar …`) and the **light mobile
header** (`.trip-detail-meta …`) each tint border/text so the field blends in and turns
`--teal` on hover/focus.

## 5. Edge cases

- **1-day trip**: no end date shown; picking a new start just moves the single day.
- **dayCount unchanged**: `UpdateTripHandler` only realigns kept days' dates — never
  removes days — so there is **no stop loss** on a date change (unlike a length change).
- **Loading trip**: the editor is only rendered inside `{trip && …}`, so `trip` is always
  defined when mounted.
- **Slow request / navigation**: the `mounted` ref prevents a late resolution from
  setting state or surfacing an error after unmount.

## 6. Testing

- **Unit (vitest)** — `utils/date.test.ts`: round-trip, month-boundary, malformed input,
  `endDate` inclusivity/clamping. (11 cases, green.)
- **Build/lint** — `tsc -b`, `vite build`, and `eslint` all clean.
- **Manual** — verify optimistic value + error revert against a forced 500, per the
  project's manual-verification habit (no DB change, so the CLAUDE.md migration note
  does not apply).
