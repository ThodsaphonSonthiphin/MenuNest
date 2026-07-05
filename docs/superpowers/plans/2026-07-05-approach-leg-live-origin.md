# Approach Leg (Live-Origin First Stop) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every Day's first Stop an incoming travel time/distance — the **Approach leg** — computed server-side from the viewer's live location at view time, so the Smart Schedule's arrival cascade, the day's total-distance figure, and the itinerary map all account for it.

**Architecture:** Backend: `GetItineraryQuery`/`GetItineraryHandler` resolve one extra leg (index 0) per Day when the request carries viewer coordinates, reusing the existing `IRouteService` and packing the result into the first Stop's existing `LegToReach` field — no new DTO shape, no persistence. Frontend: a Redux-held `viewerLocation` (captured once per trip-detail visit via `navigator.geolocation`) feeds `lat`/`lng` into the existing `getItinerary` RTK Query call; `useSchedule`'s cascade and `useDayRoute`'s total-distance math already treat `legToReach` generically per stop index, so they need no changes — only the map gains a "you are here" pin and a viewer→Stop-1 polyline.

**Tech Stack:** .NET 10 / EF Core (InMemory for unit tests), xUnit + Moq + FluentAssertions (backend); React 19, TypeScript, `@reduxjs/toolkit` (RTK Query), `@vis.gl/react-google-maps`, Vitest (frontend). Backend commands run from `backend/`; frontend commands run from `frontend/`.

## Global Constraints

- **No stored origin.** The viewer's location is read live via `navigator.geolocation`, never a Trip/Day address field (ADR-027 decision 1). Do not add any persisted column/entity for it.
- **No DB migration.** Nothing in this plan touches `MenuNest.Domain` entities, EF configurations, or adds a table/column — `GetItineraryQuery`/`StopDto` changes are in-memory query/DTO shapes only. The CLAUDE.md manual-migration step does not apply.
- **Applies to every Day's first Stop**, not just the Trip's opening day (ADR-027 decision 2).
- **Resolved server-side** through the same `IRouteService` every other Leg uses — never a client-only Haversine estimate (ADR-027 decision 3; design spec §4.1).
- **Unavailable location → show nothing.** No error banner; identical to today's rendering when `legToReach` is null (ADR-027 decision 4).
- **Map gets a pin + polyline** for the viewer's position, styled with the existing Routed/Estimated line convention (ADR-024) (ADR-027 decision 5).
- **Design spec:** `docs/superpowers/specs/2026-07-05-approach-leg-live-origin-design.md`. **Decisions:** ADR-027. **Issue:** #4.
- **Runnable gates:** `dotnet test` (backend/), `npx vitest run`, `npx tsc -b`, `npm run lint` (frontend/).

---

## File Structure

- **Modify** `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs` — add optional `ViewerLat`/`ViewerLng`.
- **Modify** `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` — resolve an index-0 leg per Day when viewer coordinates are present and the Day has stops.
- **Modify** `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` — two new tests (leg resolved with coords; no leg attempted on an empty Day).
- **Modify** `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — `GetItinerary` gains `[FromQuery] double? lat, double? lng`.
- **Modify** `frontend/src/pages/trips/tripsSlice.ts` — new `viewerLocation` state + `setViewerLocation` action.
- **Modify** `frontend/src/pages/trips/TripDetailPage.tsx` — one-shot geolocation read on mount, dispatched into the slice.
- **Modify** `frontend/src/shared/api/api.ts` — `getItinerary` query arg becomes `{tripId, lat?, lng?}`.
- **Modify** `frontend/src/pages/trips/components/ItineraryTab.tsx` — read `viewerLocation`, pass into `useGetItineraryQuery`.
- **Modify** `frontend/src/pages/trips/hooks/useSchedule.test.ts` — one new case locking in that a populated first-stop leg is cascaded.
- **Modify** `frontend/src/pages/trips/hooks/useDayRoute.ts` — read `viewerLocation`, pass into `useGetItineraryQuery`, prepend it to the segment-building points, expose it in the hook's return.
- **Modify** `frontend/src/pages/trips/components/TripMap.tsx` — new `viewerLocation` prop: extra `AdvancedMarker`, included in the `FitBounds` path.
- **Modify** `frontend/src/pages/trips/TripDetailPage.tsx` — pass `dayRoute.viewerLocation` into the desktop `<TripMap>`.
- **Modify** `frontend/src/pages/trips/components/ItineraryTab.tsx` — pass `dayRoute.viewerLocation` into the mobile map-band `<TripMap>`.
- **Modify** `frontend/src/pages/trips/trips-tokens.css` — `.viewer-pin` style.

---

### Task 1: Backend — resolve the Approach leg in `GetItineraryHandler`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs:41-69`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:74-76`

**Interfaces:**
- Consumes: `IRouteService.GetLegTimesAsync` (unchanged signature), `TripPlace`/`Stop`/`ItineraryDay` (unchanged).
- Produces: `GetItineraryQuery(Guid TripId, double? ViewerLat = null, double? ViewerLng = null)`; `StopDto.LegToReach` now non-null on a Day's first Stop whenever both coordinates were supplied and the Day has ≥1 Stop.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` (inside the `GetItineraryHandlerTests` class, after the existing two `[Fact]` methods):

```csharp
    [Fact]
    public async Task Resolves_an_approach_leg_into_the_first_stop_when_viewer_coordinates_are_provided()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p1 = TripPlace.Create(trip.Id, "A", 18.80, 98.92, PlaceCategory.See);
        fx.Db.TripPlaces.Add(p1);
        fx.Db.Stops.Add(Stop.Create(day.Id, p1.Id, 0, 60, TravelMode.Drive));
        await fx.Db.SaveChangesAsync();

        IReadOnlyList<RoutePoint>? capturedPoints = null;
        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RoutePoint>, TravelMode, CancellationToken>((pts, _, _) => capturedPoints = pts)
            .ReturnsAsync(new List<LegTime> { new(500, 3000, "approachPoly", RouteSource.Routed) });

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)
            .Handle(new GetItineraryQuery(trip.Id, ViewerLat: 18.81, ViewerLng: 98.90), CancellationToken.None);

        days[0].Stops[0].LegToReach.Should().NotBeNull();
        days[0].Stops[0].LegToReach!.Seconds.Should().Be(500);
        days[0].Stops[0].LegToReach!.Source.Should().Be(RouteSource.Routed);
        days[0].Stops[0].LegToReach!.EncodedPolyline.Should().Be("approachPoly");
        capturedPoints.Should().NotBeNull();
        capturedPoints![0].Lat.Should().Be(18.81);
        capturedPoints![0].Lng.Should().Be(98.90);
        capturedPoints![1].Lat.Should().Be(18.80);
        capturedPoints![1].Lng.Should().Be(98.92);
    }

    [Fact]
    public async Task Does_not_resolve_an_approach_leg_when_the_day_has_no_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var route = new Mock<IRouteService>();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)
            .Handle(new GetItineraryQuery(trip.Id, ViewerLat: 18.81, ViewerLng: 98.90), CancellationToken.None);

        days.Should().HaveCount(1);
        days[0].Stops.Should().BeEmpty();
        route.Verify(
            r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `backend/`): `dotnet test --filter "FullyQualifiedName~GetItineraryHandlerTests"`
Expected: FAIL — `GetItineraryQuery(trip.Id, ViewerLat: 18.81, ViewerLng: 98.90)` does not compile (`GetItineraryQuery` has no `ViewerLat`/`ViewerLng` parameters yet).

- [ ] **Step 3: Add the optional coordinates to the query**

Replace the full contents of `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.GetItinerary;

public sealed record GetItineraryQuery(Guid TripId, double? ViewerLat = null, double? ViewerLng = null)
    : IQuery<IReadOnlyList<ItineraryDayDto>>;
```

- [ ] **Step 4: Resolve the extra leg and generalize the DTO lookup**

In `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs`, replace the `foreach (var day in days)` block that builds `legTasks` (lines 41-50):

```csharp
        foreach (var day in days)
        {
            var dayStops = dayStopsById[day.Id];
            for (var li = 1; li < dayStops.Count; li++)
            {
                var origin = new RoutePoint(places[dayStops[li - 1].TripPlaceId].Lat, places[dayStops[li - 1].TripPlaceId].Lng);
                var dest = new RoutePoint(places[dayStops[li].TripPlaceId].Lat, places[dayStops[li].TripPlaceId].Lng);
                legTasks.Add(ResolveLegAsync(day.Id, li, origin, dest, dayStops[li].TravelModeToReach, ct));
            }
            // Approach leg (ADR-027): only when the caller supplied the viewer's live
            // location AND the Day has a first Stop to reach. Keyed at index 0, the same
            // slot the DTO-assembly loop below already looks up generically.
            if (q.ViewerLat is { } viewerLat && q.ViewerLng is { } viewerLng && dayStops.Count > 0)
            {
                var origin = new RoutePoint(viewerLat, viewerLng);
                var dest = new RoutePoint(places[dayStops[0].TripPlaceId].Lat, places[dayStops[0].TripPlaceId].Lng);
                legTasks.Add(ResolveLegAsync(day.Id, 0, origin, dest, dayStops[0].TravelModeToReach, ct));
            }
        }
```

Then replace the stop-DTO assembly loop (lines 59-69):

```csharp
            var dayStops = dayStopsById[day.Id];
            var stopDtos = new List<StopDto>(dayStops.Count);
            for (var i = 0; i < dayStops.Count; i++)
            {
                var s = dayStops[i];
                LegDto? leg = legByKey.TryGetValue((day.Id, i), out var l)
                    ? new LegDto(l.Seconds, l.Meters, l.EncodedPolyline, l.Source)
                    : null;
                stopDtos.Add(new StopDto(s.Id, s.TripPlaceId, s.Sequence, s.DwellMinutes, s.TravelModeToReach, leg));
            }
```

(`legByKey.TryGetValue` replaces the old `if (i > 0)` guard: index `i` is only ever in `legByKey` when a leg was actually resolved for it — every `i > 0` as before, plus `i == 0` now when viewer coordinates were supplied.)

- [ ] **Step 5: Run the tests to verify they pass**

Run (from `backend/`): `dotnet test --filter "FullyQualifiedName~GetItineraryHandlerTests"`
Expected: PASS (4 tests: the 2 pre-existing + the 2 new ones).

- [ ] **Step 6: Wire the controller query params**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, replace lines 74-76:

```csharp
    [HttpGet("api/trips/{id:guid}/itinerary")]
    public async Task<ActionResult<IReadOnlyList<ItineraryDayDto>>> GetItinerary(
        Guid id, [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken ct)
        => Ok(await _mediator.Send(new GetItineraryQuery(id, lat, lng), ct));
```

- [ ] **Step 7: Full backend build + test**

Run (from `backend/`): `dotnet build`
Expected: no errors.
Run (from `backend/`): `dotnet test`
Expected: all tests PASS (no regressions in other handlers).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryQuery.cs backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs backend/src/MenuNest.WebApi/Controllers/TripsController.cs
git commit -m "feat(trips): resolve an Approach leg into a Day's first Stop from viewer coords (#4)"
```

---

### Task 2: Frontend — capture the viewer's live location once per trip visit

**Files:**
- Modify: `frontend/src/pages/trips/tripsSlice.ts`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`

**Interfaces:**
- Consumes: nothing new (browser `navigator.geolocation`).
- Produces: `setViewerLocation(loc: {lat: number; lng: number} | null)` action; `state.trips.viewerLocation: {lat: number; lng: number} | null` (default `null`), readable via `useAppSelector((s) => s.trips.viewerLocation)` — consumed by Task 3.

- [ ] **Step 1: Add `viewerLocation` state to the slice**

In `frontend/src/pages/trips/tripsSlice.ts`, add to the `TripsState` interface (after `stopEditorStopId: string | null`):

```ts
  stopEditorStopId: string | null
  viewerLocation: {lat: number; lng: number} | null
```

Add to `initialState` (after `stopEditorStopId: null,`):

```ts
  stopEditorStopId: null,
  viewerLocation: null,
```

Add a reducer (after `setStopEditor`):

```ts
    setStopEditor(s, a: PayloadAction<string | null>) { s.stopEditorStopId = a.payload },
    setViewerLocation(s, a: PayloadAction<{lat: number; lng: number} | null>) { s.viewerLocation = a.payload },
```

Add `setViewerLocation` to the exported actions:

```ts
export const {
  setActiveDay, setActiveTab, setPlacesView, setPlaceCategoryFilter,
  setActiveStop, setCreateTripOpen, setAddMode, setItineraryMapCollapsed, setStopEditor,
  setViewerLocation,
} = tripsSlice.actions
```

- [ ] **Step 2: Typecheck the slice**

Run (from `frontend/`): `npx tsc -b`
Expected: no type errors.

- [ ] **Step 3: Read geolocation once on mount and dispatch it**

In `frontend/src/pages/trips/TripDetailPage.tsx`, change the import on line 2:

```tsx
import { useEffect, useState } from 'react'
```

Change the `tripsSlice` import on line 7:

```tsx
import { setActiveTab, setPlacesView, setAddMode, setViewerLocation } from './tripsSlice'
```

After the existing `const [dateError, setDateError] = useState<string | null>(null)` line (line 26), add:

```tsx
  // Capture the viewer's live location once per trip-detail visit — feeds the
  // Approach leg into each Day's first Stop (ADR-027). Denied, unsupported, or a
  // failed/timed-out read leave viewerLocation null: identical to today's
  // no-Approach-leg rendering (ADR-027 decision 4), no error surfaced here.
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        dispatch(setViewerLocation({
          // Rounded to ~11m so repeated reads at the same spot keep hitting the
          // same RTK Query cache entry instead of refetching on float jitter.
          lat: Math.round(pos.coords.latitude * 10000) / 10000,
          lng: Math.round(pos.coords.longitude * 10000) / 10000,
        }))
      },
      () => {},
    )
  }, [dispatch])
```

- [ ] **Step 4: Typecheck + lint**

Run (from `frontend/`): `npx tsc -b`
Expected: no type errors.
Run (from `frontend/`): `npm run lint`
Expected: no lint errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/tripsSlice.ts frontend/src/pages/trips/TripDetailPage.tsx
git commit -m "feat(trips): capture viewer live location once per trip-detail visit (#4)"
```

---

### Task 3: Frontend — feed the viewer's location into `getItinerary`

**Files:**
- Modify: `frontend/src/shared/api/api.ts:1289-1292`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx:101-107`
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts:69-87`
- Modify: `frontend/src/pages/trips/hooks/useSchedule.test.ts`

**Interfaces:**
- Consumes: `state.trips.viewerLocation` (Task 2).
- Produces: `useGetItineraryQuery({tripId, lat?, lng?})` (breaking change to its former `useGetItineraryQuery(tripId)` signature — both call sites updated in this task); `useDayRoute`'s return gains `viewerLocation: {lat, lng} | null`, consumed by Task 4.

- [ ] **Step 1: Write the failing schedule test**

In `frontend/src/pages/trips/hooks/useSchedule.test.ts`, add inside the existing `describe('computeSchedule', ...)` block, after the current `it(...)`:

```ts
  it('includes a populated leg on the first stop (Approach leg) in the cascade', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, 10 * 60), stop('2', 1, 45, 25 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:10') // dayStart + 10-minute Approach leg
    expect(s[0].depart).toBe('10:10')
    expect(s[1].arrival).toBe('10:35')
  })
```

- [ ] **Step 2: Run it — it should already pass**

Run (from `frontend/`): `npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: PASS. `computeSchedule` (useSchedule.ts:144) already reads `stop.legToReach` generically for every index, including 0 — this test locks in that behavior now that Task 1 can actually populate it. No production code changes in this step.

- [ ] **Step 3: Change `getItinerary`'s query arg shape**

In `frontend/src/shared/api/api.ts`, replace lines 1289-1292:

```ts
        getItinerary: build.query<ItineraryDayDto[], {tripId: string; lat?: number; lng?: number}>({
            query: ({tripId, lat, lng}) => {
                const qs = lat != null && lng != null ? `?lat=${lat}&lng=${lng}` : ''
                return `/api/trips/${tripId}/itinerary${qs}`
            },
            providesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
```

- [ ] **Step 4: Update the two call sites**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, add a selector after the existing ones (line 103, after `const mapCollapsed = useAppSelector((s) => s.trips.itineraryMapCollapsed)`):

```tsx
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)
```

Then replace line 107:

```tsx
  const {data: days} = useGetItineraryQuery({tripId, lat: viewerLocation?.lat, lng: viewerLocation?.lng})
```

In `frontend/src/pages/trips/hooks/useDayRoute.ts`, replace lines 69-73:

```ts
export function useDayRoute(tripId: string) {
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)
  // skip on empty tripId: this hook is called before TripDetailPage's not-found
  // guard, so without skip an empty id would fire GET /api/trips//itinerary.
  const {data: days} = useGetItineraryQuery(
    {tripId, lat: viewerLocation?.lat, lng: viewerLocation?.lng},
    {skip: !tripId},
  )
```

- [ ] **Step 5: Typecheck + lint + unit suite**

Run (from `frontend/`): `npx tsc -b`
Expected: no type errors.
Run (from `frontend/`): `npm run lint`
Expected: no lint errors.
Run (from `frontend/`): `npx vitest run`
Expected: PASS (all existing tests + the new `useSchedule.test.ts` case).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/hooks/useDayRoute.ts frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): pass viewer location into getItinerary; lock in first-stop leg cascade (#4)"
```

---

### Task 4: Frontend — "you are here" pin + polyline on the itinerary map

**Files:**
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts:112-154`
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx:106-115`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx:174-180`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `viewerLocation` (Task 3, already read inside `useDayRoute`); `LegPoint`, `buildSegments` (unchanged — `useDayRoute.ts`, already exported).
- Produces: `TripMap` gains prop `viewerLocation?: {lat: number; lng: number} | null`; `useDayRoute`'s return object gains `viewerLocation`.

- [ ] **Step 1: Prepend the viewer as a `LegPoint` when building segments**

In `frontend/src/pages/trips/hooks/useDayRoute.ts`, replace the `segments` memo (lines 112-128):

```ts
  const segments = useMemo<RouteSegment[]>(() => {
    const points: LegPoint[] = []
    if (viewerLocation) {
      // Nothing "reaches" this point — it's the start of the route, not a Stop —
      // so its own encodedPolyline/source are unused by buildSegments (which reads
      // only the DESTINATION point's fields per segment). Stop 1's own legToReach
      // (the Approach leg, once Task 1's backend change resolves it) supplies the
      // real polyline/source for the viewer→Stop-1 segment.
      points.push({lat: viewerLocation.lat, lng: viewerLocation.lng, alive: true, encodedPolyline: null, source: 'Estimated'})
    }
    points.push(
      ...scheduled.map((s) => {
        const p = placesById[s.stop.tripPlaceId]
        const alive = !!p && Number.isFinite(p.lat) && Number.isFinite(p.lng)
        return {
          lat: alive ? p.lat : 0,
          lng: alive ? p.lng : 0,
          alive,
          encodedPolyline: s.stop.legToReach?.encodedPolyline ?? null,
          source: s.stop.legToReach?.source ?? 'Estimated',
        }
      }),
    )
    return buildSegments(points)
  }, [scheduled, placesById, viewerLocation])
```

- [ ] **Step 2: Return `viewerLocation` from the hook**

In the same file, replace the `return` statement (lines 148-153):

```ts
  return {
    route,
    segments,
    dayLabel: dayIndex >= 0 ? `วัน ${dayIndex + 1}` : '',
    summaryText,
    viewerLocation,
  }
```

- [ ] **Step 3: Typecheck**

Run (from `frontend/`): `npx tsc -b`
Expected: no type errors.

- [ ] **Step 4: Add the `viewerLocation` prop and marker to `TripMap`**

In `frontend/src/pages/trips/components/TripMap.tsx`, add to the props destructuring and type (lines 109-131) — add `viewerLocation` alongside `tripId`:

```tsx
export function TripMap({
  places,
  route,
  segments,
  summaryLabel,
  summaryText,
  addMode = false,
  gestureHandling = 'greedy',
  fitPadding,
  tripId,
  viewerLocation,
  onExitAddMode,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  segments?: RouteSegment[]
  summaryLabel?: string
  summaryText?: string
  addMode?: boolean
  gestureHandling?: string
  fitPadding?: number | google.maps.Padding
  tripId?: string
  viewerLocation?: {lat: number; lng: number} | null
  onExitAddMode?: () => void
}) {
```

Replace the `path` memo (lines 135-138) so the viewer's position is included in what `FitBounds` frames:

```tsx
  const path = useMemo<LatLng[]>(() => {
    const pts = (route ?? []).map((r) => ({lat: r.lat, lng: r.lng}))
    return viewerLocation ? [{lat: viewerLocation.lat, lng: viewerLocation.lng}, ...pts] : pts
  }, [route, viewerLocation])
```

Inside the `routeMode ? (<>...</>) : (...)` block, right after the closing `</RouteSegments>`-and-`FitBounds` pair and before `{routeStops.map(...)}` (i.e. right after line 192's `<FitBounds path={path} fitPadding={fitPadding} />`), add:

```tsx
              {viewerLocation && (
                <AdvancedMarker
                  position={{lat: viewerLocation.lat, lng: viewerLocation.lng}}
                  title="คุณอยู่ที่นี่"
                  zIndex={0}
                >
                  <div className="viewer-pin" aria-label="ตำแหน่งปัจจุบันของคุณ" />
                </AdvancedMarker>
              )}
```

- [ ] **Step 5: Add the `.viewer-pin` style**

Append to `frontend/src/pages/trips/trips-tokens.css` (near the `.route-pin` rules, e.g. after the block ending around line 319):

```css
/* ── Viewer's live-location marker on the itinerary map (ADR-027) ── */
.viewer-pin {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #4285f4;
  border: 3px solid #fff;
  box-shadow: 0 0 0 4px rgba(66, 133, 244, 0.35), 0 2px 6px rgba(0, 0, 0, 0.3);
}
```

- [ ] **Step 6: Pass `viewerLocation` from both `TripMap` call sites**

In `frontend/src/pages/trips/TripDetailPage.tsx`, in the desktop `<TripMap>` (lines 106-115), add the prop:

```tsx
          <TripMap
            places={places ?? []}
            route={tab === 'itinerary' ? dayRoute.route : undefined}
            segments={tab === 'itinerary' ? dayRoute.segments : undefined}
            summaryLabel={dayRoute.dayLabel}
            summaryText={dayRoute.summaryText}
            viewerLocation={tab === 'itinerary' ? dayRoute.viewerLocation : undefined}
            addMode={tab === 'places' && addMode}
            tripId={tripId}
            onExitAddMode={() => dispatch(setAddMode(false))}
          />
```

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, in the mobile map-band `<TripMap>` (lines 174-180), add the prop:

```tsx
          <TripMap
            places={places ?? []}
            route={dayRoute.route}
            segments={dayRoute.segments}
            viewerLocation={dayRoute.viewerLocation}
            gestureHandling="cooperative"
            fitPadding={BAND_FIT_PADDING}
          />
```

- [ ] **Step 7: Typecheck + lint + unit suite**

Run (from `frontend/`): `npx tsc -b`
Expected: no type errors.
Run (from `frontend/`): `npm run lint`
Expected: no lint errors.
Run (from `frontend/`): `npx vitest run`
Expected: PASS (no change to `buildSegments`'s own tests — it's still called with a plain `LegPoint[]`).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/hooks/useDayRoute.ts frontend/src/pages/trips/components/TripMap.tsx frontend/src/pages/trips/TripDetailPage.tsx frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): show the viewer's live location as a map pin + polyline into Stop 1 (#4)"
```

- [ ] **Step 9: Manual verification**

With a running backend + `npm run dev`:
1. Open a trip with ≥1 Stop on its active Day, allowing the browser's location prompt. Confirm: the first Stop now shows an arrival later than the Day's start time, `เดินทางรวม` includes the Approach leg, and a blue "you are here" pin + connecting line appears on both the desktop split map and the mobile map band.
2. Deny the location prompt (or test in a browser/profile with geolocation disabled). Confirm: identical to today — first Stop arrives exactly at the Day start time, no pin, no error banner anywhere.
3. Switch to a later Day (Day 2+). Confirm the Approach leg and pin apply there too, not just Day 1 (ADR-027 decision 2).
4. Confirm the Routes API failure path: if the Approach leg can't be routed, it falls back to a dashed "Estimated" line (ADR-017/018/024), consistent with every other leg's honest-fallback treatment.

---

## Self-Review

**Spec coverage:**
- §4.1 (`GetItineraryQuery`/`GetItineraryHandler` resolve index-0 leg, reuse `LegToReach`) → Task 1. ✓
- §4.2 (`TripsController` query params) → Task 1 Step 6. ✓
- §4.3 (viewer-location sourcing, rounding, cache-key stability, single read per visit, both call sites consistent) → Task 2 + Task 3. ✓
- §4.4 (map pin + polyline, `FitBounds` inclusion, Routed/Estimated styling) → Task 4. ✓
- §5 edge cases: no stops (Task 1 Step 1's second test); denied/unavailable (Task 2 Step 3's no-op error callback + Task 4 Step 9.2 manual check); permission granted mid-session (RTK Query refetches automatically once `viewerLocation` changes — no extra code needed, since the query arg changing triggers a natural refetch); every-Day scope (Task 4 Step 9.3); coordinate jitter (Task 2 Step 3's rounding); Approach-leg-specific Routes API failure (inherits ADR-017/018 fallback already built into `IRouteService` — no new code, verified in Task 4 Step 9.4). ✓
- §6 testing: backend unit (Task 1), frontend unit (Task 3 Step 1-2), manual (Task 4 Step 9). ✓
- Non-goals honored: no stored origin field, no `Stop` persistence change, ADR-011's Navigate hand-off untouched. ✓

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output.

**Type consistency:** `GetItineraryQuery(Guid TripId, double? ViewerLat = null, double? ViewerLng = null)` (Task 1 Step 3) matches its two call sites — `TripsController` (Task 1 Step 6, positional `(id, lat, lng)`) and every test (Task 1 Step 1, named `ViewerLat:`/`ViewerLng:`). `{lat?: number; lng?: number}` on `getItinerary`'s query arg (Task 3 Step 3) matches both call sites' `{tripId, lat: viewerLocation?.lat, lng: viewerLocation?.lng}` (Task 3 Step 4). `viewerLocation: {lat: number; lng: number} | null` is identical across `tripsSlice.ts` (Task 2), `useDayRoute`'s return (Task 4 Step 2), and `TripMap`'s new prop type (Task 4 Step 4).

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-05-approach-leg-live-origin.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
