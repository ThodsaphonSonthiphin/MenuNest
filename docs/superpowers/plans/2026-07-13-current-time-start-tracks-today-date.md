# Current-time-start Tracks Today's Date Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a single-day Trip's Day is flagged "ใช้เวลาปัจจุบันเสมอ" (`UseCurrentTimeAsStart`), its date follows the viewer's local **today** (alongside the existing start-time-follows-now), so the itinerary and the top-bar date both read as "today."

**Architecture:** Two coordinated changes. (1) Backend `GetItineraryHandler` projects the Day's `Date` to today — read-time only, from the *same* tz-converted `_clock.UtcNow` instant already used for the start time, guarded to single-day trips. (2) Frontend `TripDetailPage` header sources that already-projected date from the (deduped) itinerary query and shows it in the top bar, locking the date picker while the flag is on. No DTO / API / MCP contract change; no DB migration.

**Tech Stack:** .NET (C#, Mediator, EF Core, xUnit + FluentAssertions + Moq) backend; React + TypeScript + RTK Query + Syncfusion `@syncfusion/react-calendars` frontend.

## Global Constraints

- **Every commit references the tracking issue** — `Refs #N` for partial work, `(closes #N)` on the final commit (project CLAUDE.md). Open the issue before the first commit.
- **A pre-commit hook runs the FULL suite** (backend `dotnet build`+`dotnet test` Release, frontend `tsc --noEmit`+`npm run build`, ~40s). Every commit must leave the **entire** suite green. Do **not** `--no-verify`.
- **Stage narrowly** — always `git add <explicit paths>`; never `git add -A`/`.`. Never stage `daily-state.md` or `AGENTS.md`.
- **No frontend component/visual/DOM test harness exists** (vitest runs in `node`). UI wiring and the disabled picker **cannot** be unit-tested and **MUST** be verified interactively (run the app / Chrome DevTools) before the task is considered done (project CLAUDE.md).
- **Read-time projection only** — never persist the projected date/time; the stored `ItineraryDay.Date` stays the fallback (ADR-054).
- **No eslint in pre-commit** — a Rules-of-Hooks mistake is caught by **no** commit gate; hook placement must be verified by hand (see Task 2).

**Accepted non-goals (do NOT implement):** first-stop On-arrival ≈ now stays "No data" (ADR-057); midnight cache staleness (ADR-038 read-time model); multi-day date float (ADR-055, Phase 2).

**Reference docs:** spec `docs/superpowers/specs/2026-07-13-current-time-start-tracks-today-date-design.md`; ADR-054/055/056/057; glossary term **Current-time start** in `CONTEXT.md`.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` | Resolve the viewer's local "now" once; project start time + (single-day) date | Modify |
| `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` | Deterministic unit coverage of the date projection + scope guard | Modify (add tests) |
| `frontend/src/pages/trips/TripDetailPage.tsx` | Derive the effective top-bar date from the itinerary projection; pass it + lock to the date editor | Modify |
| `frontend/src/pages/trips/components/TripDateEditor.tsx` | Show the override date and disable the picker when locked | Modify |

Ordering matters: **Task 1 (backend) first**, then **Task 2 (frontend)**. After Task 1 the itinerary date/weather/flags already track today while the top bar still shows the persisted date via `GetTrip` — a coherent, shippable intermediate state. Task 2 then wires the top bar. The reverse order would briefly show a locked picker still displaying the persisted date.

---

### Task 1: Backend — project the Day date to today on single-day current-time trips

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs:33-41` (compute `nowLocal` + `singleDay` once) and `:95-98` (per-day date projection)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` (append 3 tests)

**Interfaces:**
- Consumes: existing `GetItineraryQuery(Guid TripId, string? TimeZoneId, double? ViewerLat, double? ViewerLng)`; `IClock.UtcNow`; `ItineraryDayDto(Guid Id, DateOnly Date, TimeOnly DayStartTime, bool UseCurrentTimeAsStart, IReadOnlyList<StopDto> Stops)`; test fixture `HandlerTestFixture` (`fx.Clock.UtcNow` settable, `fx.User`, `fx.UserProvisioner`, `fx.Db`).
- Produces: no new types or signatures. Behavioural change only: for a trip with **exactly one** returned Day that is `UseCurrentTimeAsStart`, `ItineraryDayDto.Date` == the viewer's local today (from `TimeZoneId`); otherwise unchanged.

- [ ] **Step 1: Open the tracking issue**

```bash
gh issue create \
  --title "Current-time start (ใช้เวลาปัจจุบันเสมอ) should also track today's date on single-day trips" \
  --body "When a single-day Trip's Day is flagged UseCurrentTimeAsStart, its date should follow the viewer's local today (alongside the existing start-time-follows-now), so the itinerary and top-bar date read as 'today'. Design: docs/superpowers/specs/2026-07-13-current-time-start-tracks-today-date-design.md; ADR-054/055/056/057. Read-time projection only, single-day scope, no contract change."
```

Note the issue number returned (referred to below as `#N`).

- [ ] **Step 2: Write the failing test (date crosses the day boundary via tz)**

Append to `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs`, inside the `GetItineraryHandlerTests` class (before the closing brace):

```csharp
    [Fact]
    public async Task Projects_the_date_to_today_in_the_viewer_time_zone_for_a_single_day_flagged_trip()
    {
        using var fx = new HandlerTestFixture();
        // 20:00 UTC on Jan 15 is 03:00 on Jan 16 in Bangkok (+7) — a real day-boundary cross.
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 20, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Asia/Bangkok"), CancellationToken.None);

        days[0].Date.Should().Be(new DateOnly(2026, 1, 16));       // viewer-local today, not the persisted Jan 15
        days[0].DayStartTime.Should().Be(new TimeOnly(3, 0, 0));   // same instant as the date
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GetItineraryHandlerTests.Projects_the_date_to_today_in_the_viewer_time_zone_for_a_single_day_flagged_trip"`
Expected: **FAIL** — `days[0].Date` is `2026-01-15` (the persisted date), asserted `2026-01-16`.

- [ ] **Step 4: Implement the projection**

In `GetItineraryHandler.cs`, immediately **after** the tz-resolution block (the `if (days.Any(d => d.UseCurrentTimeAsStart)) { ... }` that ends at line 41), add:

```csharp
        // The viewer's local "now", resolved once from the tz validated above (null only when
        // no Day is flagged, in which case it is never read). Both the start time and — on a
        // single-day trip — the date derive from this SAME instant, so they can never land on
        // different calendar days across a midnight tick. Date float is read-time only and
        // scoped to single-day trips (ADR-054/055); the persisted Date stays the fallback.
        DateTime? nowLocal = tz is null ? null : TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz);
        var singleDay = days.Count == 1;
```

Then replace the start-time line and the `result.Add(...)` inside the day loop (currently lines 95-98):

```csharp
            var startTime = day.UseCurrentTimeAsStart
                ? TimeOnly.FromDateTime(nowLocal!.Value)
                : day.DayStartTime;
            var date = singleDay && day.UseCurrentTimeAsStart
                ? DateOnly.FromDateTime(nowLocal!.Value)
                : day.Date;
            result.Add(new ItineraryDayDto(day.Id, date, startTime, day.UseCurrentTimeAsStart, stopDtos));
```

(`nowLocal!.Value` is safe: it is only dereferenced when `day.UseCurrentTimeAsStart` is true, which guarantees at least one flagged Day, which guarantees `tz != null` via the throw at lines 36-40.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GetItineraryHandlerTests.Projects_the_date_to_today_in_the_viewer_time_zone_for_a_single_day_flagged_trip"`
Expected: **PASS**.

- [ ] **Step 6: Add the two scope-guard tests**

Append to the same class:

```csharp
    [Fact]
    public async Task Keeps_the_persisted_date_for_a_single_day_trip_when_the_flag_is_off()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 20, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0)); // NOT flagged
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        days[0].Date.Should().Be(new DateOnly(2026, 1, 15));       // persisted, untouched
        days[0].DayStartTime.Should().Be(new TimeOnly(9, 0));      // persisted, untouched
    }

    [Fact]
    public async Task Does_not_project_the_date_on_a_multi_day_trip_but_still_projects_the_start_time()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 20, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day1 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day1.SetUseCurrentTimeAsStart(true); // flagged day on a MULTI-day trip
        var day2 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 16), new TimeOnly(9, 0));
        fx.Db.ItineraryDays.AddRange(day1, day2);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Asia/Bangkok"), CancellationToken.None);

        days[0].Date.Should().Be(new DateOnly(2026, 1, 15));       // date NOT projected (multi-day, ADR-055)
        days[0].DayStartTime.Should().Be(new TimeOnly(3, 0, 0));   // time STILL projects (per-day, ADR-038)
    }
```

- [ ] **Step 7: Run the full GetItinerary test class to verify all pass (incl. the existing tz/validation tests)**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~GetItineraryHandlerTests"`
Expected: **PASS** — all tests, including the pre-existing `Resolves_a_current_time_day_start_in_the_supplied_time_zone`, `Applies_the_time_zone_not_the_server_clock`, `Ignores_*`, and `Rejects_*` (the change adds no new tz plumbing).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs
git commit -m "feat(trips): current-time-start day tracks today's date on single-day trips (#N)"
```

(The pre-commit hook runs the full suite; expect ~40s. `Refs #N` style — the frontend commit closes the issue.)

---

### Task 2: Frontend — top-bar date reflects today + locked picker

**Files:**
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx` (imports; two hooks above the not-found guard at :57; derived `currentDay`/`overrideDate`; both `TripDateEditor` call sites at :74 and :150)
- Modify: `frontend/src/pages/trips/components/TripDateEditor.tsx` (props `overrideDate?`/`locked?`; show override + disable picker)

**Interfaces:**
- Consumes (from Task 1's runtime effect): `ItineraryDayDto { date: string /* "yyyy-MM-dd" */, useCurrentTimeAsStart: boolean, ... }` from `useGetItineraryQuery`; existing `getViewerTimeZone(): string` (`../utils/time`); `TripDto { dayCount: number; startDate: string; ... }`.
- Produces: `TripDateEditor` now accepts `overrideDate?: string` and `locked?: boolean` (default `false`) in addition to `trip` and `onError`.

> **Rules-of-Hooks (no eslint gate):** the two new hooks (`useAppSelector` + `useGetItineraryQuery`) MUST be added **above** the not-found early `return` at `TripDetailPage.tsx:57-63` — alongside the existing `useDayRoute(tripId)` call (`:51`). Placing them after the guard makes them conditional and crashes the trip-not-found render. Only the non-hook `currentDay`/`overrideDate` consts may follow the guard.

- [ ] **Step 1: Extend `TripDateEditor` with `overrideDate` + `locked`**

In `frontend/src/pages/trips/components/TripDateEditor.tsx`, change the component signature (currently lines 29-37):

```tsx
export function TripDateEditor({
  trip,
  overrideDate,
  locked = false,
  onError,
}: {
  trip: TripDto
  overrideDate?: string // "yyyy-MM-dd" server-projected today; present only when locked
  locked?: boolean      // disable editing while current-time-start is active (single-day)
  onError: (msg: string | null) => void
}) {
```

Then change the display-date derivation + `DatePicker` (currently lines 88-101). Replace:

```tsx
  const startDt = ymdToDate(startYmd)
  const end = endDate(startDt, trip.dayCount)

  return (
    <span className="trip-date-edit">
      <DatePicker
        className="trip-date-picker"
        value={startDt}
        onChange={handleChange}
        format={DATE_FMT}
        editable={false}
        openOnFocus
        clearButton={false}
      />
```

with:

```tsx
  // While locked (current-time-start), show the server-projected today instead of the
  // persisted start; the picker is disabled so onChange can never fire (mirrors the
  // disabled start-time TimePicker in DayStartEditor — ADR-056).
  const displayYmd = locked && overrideDate ? overrideDate : startYmd
  const startDt = ymdToDate(displayYmd)
  const end = endDate(startDt, trip.dayCount)

  return (
    <span className="trip-date-edit">
      <DatePicker
        className="trip-date-picker"
        value={startDt}
        onChange={handleChange}
        format={DATE_FMT}
        editable={false}
        openOnFocus
        clearButton={false}
        disabled={locked}
      />
```

(The end-date span is already gated on `trip.dayCount > 1`; a locked trip is single-day, so it never renders — no change needed there.)

- [ ] **Step 2: Add the itinerary query + derived values in `TripDetailPage`**

In `frontend/src/pages/trips/TripDetailPage.tsx`, extend the api import (currently line 5):

```tsx
import { useGetTripQuery, useListTripPlacesQuery, useGetItineraryQuery } from '../../shared/api/api'
```

Add the time-zone util import (alongside the other `./` imports near line 13-14):

```tsx
import { getViewerTimeZone } from './utils/time'
```

Add the two hooks **immediately after** `const dayRoute = useDayRoute(tripId)` (line 51), i.e. still **above** the not-found guard:

```tsx
  // Effective top-bar date. Reads the SAME itinerary query useDayRoute already fires
  // (identical args → RTK dedups to one request); for a single-day trip flagged
  // current-time-start, the server has projected day[0].date to today (ADR-054/056).
  // MUST stay above the not-found guard below (Rules of Hooks).
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)
  const { data: itineraryDays } = useGetItineraryQuery(
    { tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng },
    { skip: !tripId },
  )
```

Then add the derived (non-hook) values **after** the not-found guard, just before `const isDesktop = bp === 'desktop'` (line 53):

```tsx
  const currentDay = trip?.dayCount === 1 && itineraryDays?.[0]?.useCurrentTimeAsStart === true
  const overrideDate = currentDay ? itineraryDays![0].date.slice(0, 10) : undefined
```

- [ ] **Step 3: Pass the new props at both `TripDateEditor` call sites**

Desktop branch (currently line 74) and mobile branch (currently line 150) — change each `<TripDateEditor trip={trip} onError={setDateError} />` to:

```tsx
              <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
```

- [ ] **Step 4: Verify Rules-of-Hooks placement by eye**

Confirm in `TripDetailPage.tsx` that `useAppSelector((s) => s.trips.viewerLocation)` and `useGetItineraryQuery(...)` both appear **above** the `if (!tripId || tripError || (!tripLoading && !trip)) { return (...) }` block, and that only `currentDay`/`overrideDate` (plain consts) appear after it. (No eslint gate will catch a mistake here.)

- [ ] **Step 5: Typecheck + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: **PASS** (no type errors; `ItineraryDayDto.date`/`useCurrentTimeAsStart` already exist in the generated types).

- [ ] **Step 6: Interactive verification (REQUIRED — no DOM test harness)**

Run the app against a seeded/authed env (or Chrome DevTools). On a **1-day** trip:
1. Tick "ใช้เวลาปัจจุบันเสมอ" ⇒ the top-bar date shows **today** and the date picker is **disabled** (cannot open/edit); the day-start time snaps to now.
2. **Downstream** stops' On-arrival (ไปถึง) weather chips populate; the **first** stop's On-arrival stays "No data" (expected — covered by its "ตอนนี้" chip, ADR-057).
3. Day-of-week-sensitive Timing flags reflect **today's** weekday.
4. Untick ⇒ the planned date returns and the picker is editable again.
5. Open a **multi-day** trip ⇒ its top-bar date is unaffected and the picker stays editable (even if a day is flagged).

Record the result (and a screenshot) in the PR/issue.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/components/TripDateEditor.tsx
git commit -m "feat(trips): top-bar date follows today when current-time-start is on (closes #N)"
```

(Pre-commit runs the full suite; expect ~40s.)

---

## Self-Review

**1. Spec coverage**

| Spec item | Task |
|---|---|
| Backend `GetItineraryHandler`: `nowLocal` once, project date on `days.Count==1 && flag` | Task 1 Step 4 |
| Same-instant date+time (no midnight straddle) | Task 1 Step 4 (single `nowLocal`) |
| Backend tests: tz day-boundary; single-day flag-off; multi-day flag-on | Task 1 Steps 2, 6 |
| Existing tz/validation tests stay green | Task 1 Step 7 |
| Frontend header: deduped itinerary query above the guard | Task 2 Step 2, 4 |
| `currentDay`/`overrideDate` derivation | Task 2 Step 2 |
| `TripDateEditor` `overrideDate`/`locked` + disabled picker | Task 2 Step 1 |
| Both call sites updated | Task 2 Step 3 |
| No contract/DTO/MCP/migration change | (no task — verified: no such file touched) |
| Interactive verification (no DOM harness) | Task 2 Step 6 |
| Tracking issue + commit references | Task 1 Step 1, 8; Task 2 Step 7 |
| Non-goals not implemented (first-stop weather, midnight, multi-day float) | Global Constraints (excluded) |

No gaps.

**2. Placeholder scan** — no TBD/TODO/"handle edge cases"/"similar to"; every code step shows complete code.

**3. Type consistency** — `overrideDate?: string` / `locked?: boolean` declared in Task 2 Step 1 and used identically at both call sites (Step 3); `itineraryDays` is the `useGetItineraryQuery` result used consistently in Step 2; backend `nowLocal` (`DateTime?`) / `singleDay` (`bool`) declared and used only within Task 1 Step 4; `ItineraryDayDto.date` is `string`, `.slice(0,10)`-safe. Consistent.
