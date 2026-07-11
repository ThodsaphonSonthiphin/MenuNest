# Current-time Start Timezone Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a Day flagged "Current-time start" (`UseCurrentTimeAsStart`) show the **viewer's local** wall-clock time instead of the server's UTC clock.

**Architecture:** The caller supplies its IANA time zone with the `GetItinerary` request (the same "frontend supplies per-request input, backend resolves" shape as ADR-027's viewer lat/lng). `GetItineraryHandler` injects `IClock` and computes the start as `TimeZoneInfo.ConvertTimeFromUtc(IClock.UtcNow, tz)`. `dayStartTime` in the DTO therefore stays the effective start for both consumers (SPA + MCP). The time zone is **required** of every caller and an unresolvable id is rejected loudly — no silent UTC fallback (that fallback is the bug).

**Tech Stack:** .NET (C#, Mediator, EF Core, xUnit + Moq + FluentAssertions), React + TypeScript + RTK Query, Vitest.

## Global Constraints

- **Verified root cause, not hypothesis** — see [ADR-038](../../adr/038-current-time-start-viewer-timezone.md) and [the design spec](../specs/2026-07-11-current-time-start-timezone-fix-design.md). Do not re-diagnose.
- **Ticket on every commit** (CLAUDE.md): docs commit `(#N)`, frontend commit `(#N)`, backend commit `(closes #N)`, where `N` is the issue opened in Task 1.
- **Stage narrowly** — `git add <explicit paths>` only. Never `git add -A` / `git add .`. Never sweep `daily-state.md` or `AGENTS.md` into a commit.
- **Pre-commit hook runs the FULL suite** (`frontend/.husky/pre-commit`: backend `dotnet build`+`dotnet test` Release, frontend `tsc --noEmit`+`npm run build`, ~40s+). Expect the wait; never `--no-verify`.
- **Task order is deployability-safe:** frontend (sends `tz`, backend still ignores it) lands **before** backend (starts requiring it). At no commit is `main` broken.
- **No DB migration, no Azure config change.** The fix makes the server timezone irrelevant to correctness.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `frontend/src/pages/trips/utils/time.ts` | add `getViewerTimeZone()` | 2 |
| `frontend/src/pages/trips/utils/time.test.ts` | test for `getViewerTimeZone()` | 2 |
| `frontend/src/shared/api/api.ts` | `getItinerary` arg gains required `tz`, encoded into the URL | 2 |
| `frontend/src/pages/trips/components/ItineraryTab.tsx` | pass `tz` to the query | 2 |
| `frontend/src/pages/trips/hooks/useDayRoute.ts` | pass `tz` to the query | 2 |
| `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs` | `+ string TimeZoneId` (required) | 3 |
| `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` | inject `IClock`, resolve tz, convert; reject bad/empty tz | 3 |
| `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` | `[FromQuery] string tz` → query | 3 |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs` | `get_itinerary` required `timeZoneId` param | 3 |
| `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs` | expose a `FixedClock Clock` | 3 |
| `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` | update ctor/query sites; replace mirror test | 3 |

---

## Task 1: Open the tracking issue & commit the design docs

**Files:**
- Commit (already written, uncommitted): `docs/adr/038-current-time-start-viewer-timezone.md`, `CONTEXT.md`, `docs/superpowers/specs/2026-07-11-current-time-start-timezone-fix-design.md`, `docs/superpowers/plans/2026-07-11-current-time-start-timezone-fix.md`

**Interfaces:**
- Produces: the GitHub issue number `N`, referenced by every later commit.

- [ ] **Step 1: Open the issue**

```bash
gh issue create \
  --title 'fix(trips): "always use current time" day start shows server UTC, not the viewer local time' \
  --body $'A day flagged "ใช้เวลาปัจจุบันเสมอ" (UseCurrentTimeAsStart) shows a start time equal to the viewer\'s UTC offset behind reality (Thailand 21:44 -> shown 14:43).\n\nRoot cause (verified, debug-mantra): GetItineraryHandler re-seeds the start with TimeOnly.FromDateTime(DateTime.Now); DateTime.Now on Azure App Service is the server UTC clock. Fix per ADR-038: thread a required IANA time zone through GetItinerary and resolve via IClock.UtcNow + TimeZoneInfo.ConvertTimeFromUtc.'
```

Expected: prints the new issue URL ending in `/issues/N`. Record `N`.
(If `gh` returns 401, refresh the User-scope `GH_TOKEN` PAT and restart VS Code — see memory `gh CLI auth via GH_TOKEN`.)

- [ ] **Step 2: Stage the docs (explicit paths only)**

```bash
git add docs/adr/038-current-time-start-viewer-timezone.md CONTEXT.md docs/superpowers/specs/2026-07-11-current-time-start-timezone-fix-design.md docs/superpowers/plans/2026-07-11-current-time-start-timezone-fix.md
```

- [ ] **Step 3: Commit** (replace `N`)

```bash
git commit -m "docs(trips): ADR-038 + spec/plan for current-time-start timezone fix (#N)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

Expected: pre-commit hook runs the full suite and passes (docs-only change), commit succeeds.

---

## Task 2: Frontend — send the viewer's IANA time zone on every itinerary fetch

Landed **before** the backend change: the current backend ignores the extra `tz` query param, so this commit is safe on `main`. TypeScript's required `tz` arg forces both callers to supply it.

**Files:**
- Modify: `frontend/src/pages/trips/utils/time.ts`
- Test: `frontend/src/pages/trips/utils/time.test.ts`
- Modify: `frontend/src/shared/api/api.ts:1294-1300`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx:110` (+ import)
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts:74-77` (+ import)

**Interfaces:**
- Produces: `getViewerTimeZone(): string` (exported from `pages/trips/utils/time.ts`); `useGetItineraryQuery` arg type `{tripId: string; tz: string; lat?: number; lng?: number}`.

- [ ] **Step 1: Write the failing test for `getViewerTimeZone`**

Append to `frontend/src/pages/trips/utils/time.test.ts`:

```typescript
import {getViewerTimeZone} from './time'

describe('getViewerTimeZone', () => {
  it("returns the browser's resolved IANA time zone", () => {
    const expected = Intl.DateTimeFormat().resolvedOptions().timeZone
    expect(getViewerTimeZone()).toBe(expected)
  })

  it('is a non-empty string (falls back to UTC)', () => {
    expect(getViewerTimeZone().length).toBeGreaterThan(0)
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/utils/time.test.ts`
Expected: FAIL — `getViewerTimeZone` is not exported from `./time`.

- [ ] **Step 3: Implement `getViewerTimeZone`**

Append to `frontend/src/pages/trips/utils/time.ts`:

```typescript
/**
 * The viewer's IANA time zone (e.g. "Asia/Bangkok"), sent with the itinerary
 * fetch so the backend can resolve a Current-time-start day into the viewer's
 * local wall-clock (ADR-038). Falls back to "UTC" on the rare browser without
 * Intl time-zone support.
 */
export function getViewerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/utils/time.test.ts`
Expected: PASS.

- [ ] **Step 5: Add required `tz` to the `getItinerary` RTK query**

Replace `frontend/src/shared/api/api.ts:1294-1300` with:

```typescript
        getItinerary: build.query<ItineraryDayDto[], {tripId: string; tz: string; lat?: number; lng?: number}>({
            query: ({tripId, tz, lat, lng}) => {
                const coords = lat != null && lng != null ? `&lat=${lat}&lng=${lng}` : ''
                return `/api/trips/${tripId}/itinerary?tz=${encodeURIComponent(tz)}${coords}`
            },
            providesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
```

(`encodeURIComponent` is required — an IANA id contains `/`.)

- [ ] **Step 6: Pass `tz` from `ItineraryTab`**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, add the import (with the other `../utils/*` imports) and update the query call at line 110:

```typescript
import {getViewerTimeZone} from '../utils/time'
```

```typescript
  const {data: days, isLoading: itineraryLoading, error: itineraryError} = useGetItineraryQuery({tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng})
```

- [ ] **Step 7: Pass `tz` from `useDayRoute`**

In `frontend/src/pages/trips/hooks/useDayRoute.ts`, add the import and update the query call at lines 74-77:

```typescript
import {getViewerTimeZone} from '../utils/time'
```

```typescript
  const {data: days} = useGetItineraryQuery(
    {tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng},
    {skip: !tripId},
  )
```

- [ ] **Step 8: Typecheck + build (the compiler enforces both callers)**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: no type errors, build succeeds. (If a caller of `useGetItineraryQuery` were missed, tsc would fail on the missing required `tz`.)

- [ ] **Step 9: Commit** (replace `N`)

```bash
git add frontend/src/pages/trips/utils/time.ts frontend/src/pages/trips/utils/time.test.ts frontend/src/shared/api/api.ts frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/hooks/useDayRoute.ts
git commit -m "fix(trips): send the viewer's IANA time zone with the itinerary fetch (#N)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

Expected: full suite passes, commit succeeds.

---

## Task 3: Backend — require the tz, resolve the current-time start in it, replace the mirror test

Makes `TimeZoneId` a required field, resolves it via `IClock`, and rejects an unresolvable/empty id. Because the record signature change breaks every construction site at once, the query/handler/controller/MCP/test edits are one atomic task that must compile together.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs:12-19,79`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:76-79`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:114-120`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs`

**Interfaces:**
- Consumes: `IClock` (from `MenuNest.Application.Abstractions`, already DI-registered at `Infrastructure/DependencyInjection.cs:44`); `FixedClock` (test support, ctor `FixedClock(DateTime utcNow)`, settable `UtcNow`).
- Produces: `GetItineraryQuery(Guid TripId, string TimeZoneId, double? ViewerLat = null, double? ViewerLng = null)`; `GetItineraryHandler(IApplicationDbContext, IUserProvisioner, IRouteService, IClock)`; MCP `get_itinerary(Guid tripId, string timeZoneId, double? viewerLat, double? viewerLng, CancellationToken)`.

- [ ] **Step 1: Write the failing behavior tests (new + replacing the mirror test)**

In `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs`, add `using MenuNest.Domain.Exceptions;` to the usings, then **replace** the existing `Uses_the_current_time_as_start_when_the_day_is_flagged_to_track_it` test (lines ~132-152) with these four tests:

```csharp
    [Fact]
    public async Task Resolves_a_current_time_day_start_in_the_supplied_time_zone()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Asia/Bangkok"), CancellationToken.None);

        days[0].UseCurrentTimeAsStart.Should().BeTrue();
        days[0].DayStartTime.Should().Be(new TimeOnly(19, 0, 0)); // 12:00 UTC + 7h ICT
        days[0].DayStartTime.Should().NotBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Applies_the_time_zone_not_the_server_clock()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "America/New_York"), CancellationToken.None);

        days[0].DayStartTime.Should().Be(new TimeOnly(7, 0, 0)); // 12:00 UTC - 5h EST (Jan, no DST)
    }

    [Fact]
    public async Task Keeps_the_persisted_start_time_when_the_day_is_not_flagged()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0)); // NOT flagged
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Asia/Bangkok"), CancellationToken.None);

        days[0].UseCurrentTimeAsStart.Should().BeFalse();
        days[0].DayStartTime.Should().Be(new TimeOnly(9, 0)); // untouched, no conversion
    }

    [Fact]
    public async Task Rejects_an_unresolvable_time_zone_even_with_no_flagged_day()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();
        var handler = new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock);

        var act = () => handler.Handle(new GetItineraryQuery(trip.Id, "Not/AZone"), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }
```

- [ ] **Step 2: Update the fixture to expose a clock**

In `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`, add a property (the value is a harmless default; time tests set `fx.Clock.UtcNow` explicitly):

```csharp
    public FixedClock Clock { get; } = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
```

- [ ] **Step 3: Update the 4 pre-existing tests' ctor + query sites**

In `GetItineraryHandlerTests.cs`, every `new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)` becomes `new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object, fx.Clock)`, and every `new GetItineraryQuery(trip.Id)` / `new GetItineraryQuery(trip.Id, ViewerLat: …, ViewerLng: …)` gains `"UTC"` as the required tz:

- line ~33/34: `…, route.Object, fx.Clock).Handle(new GetItineraryQuery(trip.Id, "UTC"), …)`
- line ~71/72: `…, route.Object, fx.Clock).Handle(new GetItineraryQuery(trip.Id, "UTC"), …)`
- line ~96/97: `…, route.Object, fx.Clock).Handle(new GetItineraryQuery(trip.Id, "UTC", ViewerLat: 18.81, ViewerLng: 98.90), …)`
- line ~122/123: `…, route.Object, fx.Clock).Handle(new GetItineraryQuery(trip.Id, "UTC", ViewerLat: 18.81, ViewerLng: 98.90), …)`

- [ ] **Step 4: Run the tests to verify they fail (compile-red then assertion-red)**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetItineraryHandlerTests"`
Expected: FAIL — the production `GetItineraryQuery` has no `TimeZoneId` and `GetItineraryHandler` has no `IClock` ctor yet (compile errors), which is the red state.

- [ ] **Step 5: Add the required `TimeZoneId` to the query**

Replace `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs` body with:

```csharp
public sealed record GetItineraryQuery(Guid TripId, string TimeZoneId, double? ViewerLat = null, double? ViewerLng = null)
    : IQuery<IReadOnlyList<ItineraryDayDto>>;
```

- [ ] **Step 6: Inject `IClock`, resolve the tz, and convert**

In `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs`:

(a) Replace the fields + constructor (lines 12-17) with:

```csharp
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IRouteService _routes;
    private readonly IClock _clock;

    public GetItineraryHandler(IApplicationDbContext db, IUserProvisioner users, IRouteService routes, IClock clock)
    { _db = db; _users = users; _routes = routes; _clock = clock; }
```

(b) Immediately after the trip-not-found line (line 23, `?? throw new DomainException("Trip not found.");`), insert the eager tz resolution so a bad/empty id is rejected regardless of whether any day is flagged:

```csharp
        if (string.IsNullOrWhiteSpace(q.TimeZoneId))
            throw new DomainException("Time zone is required.");
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(q.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new DomainException($"Unknown time zone: {q.TimeZoneId}"); }
```

(c) Replace line 79:

```csharp
            var startTime = day.UseCurrentTimeAsStart
                ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz))
                : day.DayStartTime;
```

(`IClock` is already imported via `using MenuNest.Application.Abstractions;`; `DomainException` via `using MenuNest.Domain.Exceptions;`; `TimeZoneInfo`/exceptions are in `System`, available through implicit usings.)

- [ ] **Step 7: Pass `tz` from the HTTP controller**

Replace `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:76-79` with:

```csharp
    [HttpGet("api/trips/{id:guid}/itinerary")]
    public async Task<ActionResult<IReadOnlyList<ItineraryDayDto>>> GetItinerary(
        Guid id, [FromQuery] string tz, [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken ct)
        => Ok(await _mediator.Send(new GetItineraryQuery(id, tz, lat, lng), ct));
```

(A missing `tz` is caught by the handler's `IsNullOrWhiteSpace` guard → `DomainException`, surfaced as a clean error rather than a 500.)

- [ ] **Step 8: Add the required `timeZoneId` param to the MCP tool**

Replace `backend/src/MenuNest.McpServer/Tools/TripTools.cs:114-120` with:

```csharp
    [McpServerTool, Description("Get the trip's itinerary: each day's start time and ordered stops, with each stop's dwell, travel mode, and resolved leg-to-reach (seconds/meters/source). For a day set to 'always start from the current time', dayStart is resolved into the supplied timeZoneId. Arrival/leave times and timing flags are NOT included — compute arrivals as dayStart + running sum of (previous leg seconds + previous dwell). viewerLat/viewerLng are for the app's live location and are normally omitted.")]
    public async Task<IReadOnlyList<ItineraryDayDto>> get_itinerary(
        [Description("Trip ID")] Guid tripId,
        [Description("The user's IANA time zone, e.g. Asia/Bangkok. Required — used to resolve any day set to 'always start from the current time' into the user's local wall-clock.")] string timeZoneId,
        [Description("Viewer latitude for the approach leg (optional; usually omit)")] double? viewerLat,
        [Description("Viewer longitude for the approach leg (optional; usually omit)")] double? viewerLng,
        CancellationToken ct)
        => await mediator.Send(new GetItineraryQuery(tripId, timeZoneId, viewerLat, viewerLng), ct);
```

- [ ] **Step 9: Run the GetItinerary tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetItineraryHandlerTests"`
Expected: PASS — all `GetItineraryHandlerTests` (the 4 pre-existing + 4 new) green.

- [ ] **Step 10: Build the whole backend to confirm no other construction site broke**

Run: `cd backend && dotnet build`
Expected: build succeeds (controller + MCP tool are the only other `GetItineraryQuery` sites; both updated).

- [ ] **Step 11: Commit** (replace `N`)

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/src/MenuNest.McpServer/Tools/TripTools.cs backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs
git commit -m "fix(trips): resolve a current-time day start in the viewer's time zone (closes #N)

DateTime.Now returned the server's UTC clock on Azure; thread a required IANA
time zone through GetItinerary and convert IClock.UtcNow into it (ADR-038).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

Expected: full suite passes, commit succeeds.

---

## Manual verification (after Task 3)

Per the `verify` skill / spec §4 — drive the real flow, not just tests:
- Open a trip, tick "ใช้เวลาปัจจุบันเสมอ" on a day. The `เริ่ม HH:mm` value should equal your **device** clock (e.g. 21:44 in Thailand), not 7h behind.
- Confirm the cascaded stop times and any "ร้านปิดแล้ว…" flags shift with the corrected start.
- (Note: no deploy step is in this plan; deployment follows the repo's normal CD on merge to `main`. No EF migration to apply — this change touches no schema.)

## Self-Review

- **Spec coverage:** §3.1 → Task 3 Steps 5-6; §3.2 → Step 7; §3.3 → Step 8; §3.4 → Task 2; §4 edge cases → Task 3 Steps 1 (Bangkok/New_York/non-flagged/unknown) + the `IsNullOrWhiteSpace` guard (missing tz); §5 tests → Task 3 Steps 1-3; §6 rollout → Global Constraints + Task 1 (issue) + Manual verification.
- **Placeholder scan:** none — every step carries real code/commands. `N` is the deliberately-parameterised issue number from Task 1.
- **Type consistency:** `GetItineraryQuery(Guid, string TimeZoneId, double?, double?)`, `GetItineraryHandler(…, IClock)`, `get_itinerary(…, string timeZoneId, …)`, and RTK arg `{tripId, tz, lat?, lng?}` are used identically across every task and both consumers. `fx.Clock` (type `FixedClock`) matches the fixture property added in Step 2.
