# Trip Route Geometry + Honest Estimated Fallback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Draw road-accurate per-Leg route lines with real routed distance/time on the Trips map, and when the Routes API is unavailable, fall back to a straight-line estimate that is honestly labelled instead of shown as truth.

**Architecture:** Backend swaps Routes API `computeRouteMatrix` → `computeRoutes` (one call per Leg, its own travel mode) to obtain `encodedPolyline` + distance + duration; each Leg carries a new `RouteSource {Routed, Estimated}` and an optional `EncodedPolyline`. The frontend decodes the polyline and draws per-Leg segments (solid-curved for `Routed`, dashed-faded-straight for `Estimated`), and marks estimated legs with a "ประมาณ" chip + `~` prefix and a day-summary flag.

**Tech Stack:** .NET (C#, xUnit + FluentAssertions, System.Text.Json, `IMemoryCache`); React 19 + TypeScript + Vite + RTK Query + `@vis.gl/react-google-maps`; Vitest.

**Spec:** `docs/superpowers/specs/2026-07-03-trip-route-geometry-and-honest-fallback-design.md`
**ADRs:** 017, 018, 023, 024, 025. **Mock:** `docs/mocks/route-estimate-treatment-mock.html`.

## Global Constraints

- **Essentials pricing tier — never leave it.** Field mask must be exactly `routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline`; do **not** send `routingPreference`; do **not** set `polylineQuality: HIGH_QUALITY`; do **not** widen the mask. Any of these moves the call to the paid traffic tier.
- **Keep the attribution header** `X-Goog-Maps-Solution-ID: gmp_git_agentskills_v1` on the Routes call (ADR-007).
- **`RouteSource` must serialize as a string** (`"Routed"`/`"Estimated"`). This is already true via `Program.cs:185` `JsonStringEnumConverter`; do **not** add a custom converter or change enum serialization.
- **Frontend robustness:** treat a missing/`undefined` `source` as `Estimated` (never assume `Routed`).
- **Map cleanup:** dispose **every** per-Leg polyline in the effect teardown.
- **New UI copy is Thai text, not emoji** (the "ประมาณ" / "ระยะโดยประมาณ" markers). Existing pill mode emoji 🚗/🚶/🚃 stay unchanged (out of scope).
- **No** change to `appsettings*.json`, `infra/`, DI wiring, or the DB (no EF migration — `LegDto`/`LegTime` are not entities).
- **Pre-merge gate:** run the `google-maps-platform` skill's compliance review on the computeRoutes code before merging (ADR-007).
- **Commits go straight to `main`** (no feature branch).

---

## File Structure

**Backend (create):**
- `backend/src/MenuNest.Domain/Enums/RouteSource.cs` — the new `{ Routed, Estimated }` enum.
- `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleRouteServiceTests.cs` — computeRoutes parse + fallback tests.

**Backend (modify):**
- `backend/src/MenuNest.Application/Abstractions/IRouteService.cs` — widen `LegTime`.
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — widen `LegDto`.
- `backend/src/MenuNest.Infrastructure/Maps/HaversineRouteService.cs` — set `Estimated`.
- `backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs` — set `Routed`; computeRoutes swap; comment.
- `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` — map new fields.
- `backend/src/MenuNest.Infrastructure/DependencyInjection.cs` — de-stale comment.
- `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` — fix `LegTime` literals.
- `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs` — assert `Estimated`.

**Frontend (modify):**
- `frontend/src/shared/api/api.ts` — `RouteSource` type + widen `LegDto` interface.
- `frontend/src/pages/trips/hooks/useDayRoute.ts` — `buildSegments` + `segments` + `anyEstimated` + summary flag.
- `frontend/src/pages/trips/hooks/useSchedule.test.ts` — fix the leg literal (compile break).
- `frontend/src/pages/trips/components/TripMap.tsx` — geometry decode + per-Leg polylines.
- `frontend/src/pages/trips/TripDetailPage.tsx` — pass the new `segments` prop.
- `frontend/src/pages/trips/components/TravelLeg.tsx` — `~` + "ประมาณ" chip.
- `frontend/src/pages/trips/trips-tokens.css` — `.leg-approx` chip class.

**Frontend (create):**
- `frontend/src/pages/trips/hooks/useDayRoute.test.ts` — `buildSegments` unit tests.

---

## Task 1: Backend contract + honest fallback (enum, records, producers, handler)

Widening a positional record breaks the build until every construction site is updated, so this whole task is **one commit**. `GoogleRouteService` still calls `computeRouteMatrix` here (so it returns `Routed` with a `null` polyline — no geometry yet); the endpoint swap is Task 2.

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/RouteSource.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IRouteService.cs:5`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs:15`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/HaversineRouteService.cs:18`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs` (line 84 return)
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` (~line 62)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs:31,67`

**Interfaces:**
- Produces: `enum RouteSource { Routed, Estimated }` (namespace `MenuNest.Domain.Enums`); `record LegTime(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source)`; `record LegDto(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source)`.

- [ ] **Step 1: Create the enum**

`backend/src/MenuNest.Domain/Enums/RouteSource.cs`:
```csharp
namespace MenuNest.Domain.Enums;

/// <summary>Whether a Leg's distance/time is a real routed value (Routes API) or a
/// straight-line estimate (Haversine fallback). Named by quality, not vendor (ADR-018).</summary>
public enum RouteSource
{
    Routed,
    Estimated,
}
```

- [ ] **Step 2: Widen `LegTime`**

`backend/src/MenuNest.Application/Abstractions/IRouteService.cs` — line 5 (`using MenuNest.Domain.Enums;` already present at line 1):
```csharp
public sealed record LegTime(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source);
```

- [ ] **Step 3: Widen `LegDto`**

`backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — line 15 (`using MenuNest.Domain.Enums;` already present):
```csharp
public sealed record LegDto(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source);
```

- [ ] **Step 4: Haversine sets `Estimated` + null polyline**

`backend/src/MenuNest.Infrastructure/Maps/HaversineRouteService.cs` — replace the `legs.Add(...)` at line 18:
```csharp
legs.Add(new LegTime(
    (int)Math.Round(meters / SpeedMps(mode)),
    (int)Math.Round(meters),
    null,
    RouteSource.Estimated));
```

- [ ] **Step 5: Google returns `Routed` (polyline still null for now)**

`backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs` — the return in `ComputeOneAsync` (line 84). (`using MenuNest.Domain.Enums;` is already imported — `TravelMode` is used in this file.)
```csharp
return new LegTime(seconds, meters, null, RouteSource.Routed);
```

- [ ] **Step 6: Thread the fields through the handler**

`backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` — replace line 62 (`LegDto? leg = i == 0 ? null : new LegDto(...)`) with a hoisted lookup:
```csharp
LegDto? leg = null;
if (i > 0)
{
    var l = legByKey[(day.Id, i)];
    leg = new LegDto(l.Seconds, l.Meters, l.EncodedPolyline, l.Source);
}
```

- [ ] **Step 7: Fix the two `LegTime` literals in the handler tests**

`backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs` — line 31 and line 67 (`using MenuNest.Domain.Enums;` already at line 6):
```csharp
// line 31
.ReturnsAsync(new List<LegTime> { new(900, 4200, null, RouteSource.Routed) });
// line 67
.ReturnsAsync(new List<LegTime> { new(600, 2000, null, RouteSource.Routed) });
```

- [ ] **Step 8: Add the failing Haversine assertions**

`backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs` — add to the end of `Returns_one_leg_fewer_than_points_with_positive_times` (after the existing `legs[0].Seconds` assertion):
```csharp
legs[0].Source.Should().Be(RouteSource.Estimated);
legs[0].EncodedPolyline.Should().BeNull();
```

- [ ] **Step 9: Run the backend tests**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj`
Expected: PASS (all existing tests + the two new Haversine assertions). If it does not compile, a `new LegTime(...)` / `new LegDto(...)` call site was missed.

- [ ] **Step 10: De-stale the DI comment**

`backend/src/MenuNest.Infrastructure/DependencyInjection.cs` — line ~112, change `computeRouteMatrix` to `computeRoutes`:
```csharp
// Route service — per-leg Haversine fallback always available; Google Routes API
// (computeRoutes, not the legacy Distance Matrix API) registered when key is present.
```

- [ ] **Step 11: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/RouteSource.cs \
        backend/src/MenuNest.Application/Abstractions/IRouteService.cs \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.Infrastructure/Maps/HaversineRouteService.cs \
        backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs \
        backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs \
        backend/src/MenuNest.Infrastructure/DependencyInjection.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs
git commit -m "feat(trips): add RouteSource to Leg contract; Haversine legs labelled Estimated

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Backend — switch `computeRouteMatrix` → `computeRoutes` for real geometry

Now `GoogleRouteService` calls `computeRoutes` and returns a real `encodedPolyline` on `Routed` legs.

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs` (header comment 10-15; `ComputeOneAsync` 62-90)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleRouteServiceTests.cs` (create)

**Interfaces:**
- Consumes: `LegTime` (4-arg, from Task 1), `RouteSource`, `GoogleMapsOptions { ApiKey }`.
- Produces: `GoogleRouteService.GetLegTimesAsync` now yields `Routed` legs carrying `EncodedPolyline`, and `Estimated` legs (via the Haversine catch) with `null` polyline.

- [ ] **Step 1: Write the failing tests**

`backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleRouteServiceTests.cs`:
```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GoogleRouteServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public StubHandler(HttpStatusCode status, string json = "") { _status = status; _json = json; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static GoogleRouteService Build(HttpMessageHandler handler) => new(
        new StubFactory(handler),
        Options.Create(new GoogleMapsOptions { ApiKey = "test-key" }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<GoogleRouteService>.Instance);

    private static readonly List<RoutePoint> TwoPoints =
        new() { new(12.61, 102.10), new(12.57, 102.18) };

    [Fact]
    public async Task Routed_leg_parses_distance_duration_and_polyline()
    {
        const string json = "{\"routes\":[{\"duration\":\"1830s\",\"distanceMeters\":45200,\"polyline\":{\"encodedPolyline\":\"_p~iF~ps|U_ulLnnqC\"}}]}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));

        var legs = await svc.GetLegTimesAsync(TwoPoints, TravelMode.Drive, CancellationToken.None);

        legs.Should().HaveCount(1);
        legs[0].Source.Should().Be(RouteSource.Routed);
        legs[0].Seconds.Should().Be(1830);
        legs[0].Meters.Should().Be(45200);
        legs[0].EncodedPolyline.Should().Be("_p~iF~ps|U_ulLnnqC");
    }

    [Fact]
    public async Task Google_failure_falls_back_to_Estimated_without_polyline()
    {
        var svc = Build(new StubHandler(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));

        var legs = await svc.GetLegTimesAsync(TwoPoints, TravelMode.Drive, CancellationToken.None);

        legs.Should().HaveCount(1);
        legs[0].Source.Should().Be(RouteSource.Estimated);
        legs[0].EncodedPolyline.Should().BeNull();
        legs[0].Meters.Should().BeGreaterThan(0); // Haversine still produced an estimate
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleRouteServiceTests"`
Expected: FAIL — `Routed_leg_parses...` throws (the current code POSTs `computeRouteMatrix` and parses a top-level array, so `GetProperty("routes")` is absent and the leg is empty/wrong).

- [ ] **Step 3: Rewrite `ComputeOneAsync` for computeRoutes**

`backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs` — replace the body of `ComputeOneAsync` (lines 62-90) with:
```csharp
private async Task<LegTime> ComputeOneAsync(RoutePoint o, RoutePoint d, TravelMode mode, CancellationToken ct)
{
    var client = _http.CreateClient();
    using var req = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes");
    req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
    // Essentials-tier field mask: geometry + distance + time only. Do NOT widen (ADR-023/017).
    req.Headers.Add("X-Goog-FieldMask", "routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline");
    req.Headers.Add("X-Goog-Maps-Solution-ID", "gmp_git_agentskills_v1");
    req.Content = JsonContent.Create(new
    {
        origin = Wp(o),
        destination = Wp(d),
        travelMode = mode switch { TravelMode.Walk => "WALK", TravelMode.Transit => "TRANSIT", _ => "DRIVE" },
        // No routingPreference: omission = TRAFFIC_UNAWARE (Essentials) and is required for WALK/TRANSIT.
    });
    // Bound each Google call so one slow upstream cannot stall the whole itinerary response.
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
    var resp = await client.SendAsync(req, timeoutCts.Token);
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeoutCts.Token));
    var route = doc.RootElement.GetProperty("routes").EnumerateArray().First();
    var seconds = ParseDuration(route.GetProperty("duration").GetString());
    var meters = route.TryGetProperty("distanceMeters", out var m) ? m.GetInt32() : 0;
    var polyline = route.TryGetProperty("polyline", out var p) && p.TryGetProperty("encodedPolyline", out var e)
        ? e.GetString()
        : null;
    return new LegTime(seconds, meters, polyline, RouteSource.Routed);

    static object Wp(RoutePoint p) => new { location = new { latLng = new { latitude = p.Lat, longitude = p.Lng } } };
    static int ParseDuration(string? s) =>
        s is not null && double.TryParse(s.TrimEnd('s'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? (int)Math.Round(v) : 0;
}
```

- [ ] **Step 4: Update the header comment (lines 10-15)**

Replace the computeRouteMatrix header block with:
```csharp
// Routes API: POST https://routes.googleapis.com/directions/v2:computeRoutes  (one call per leg)
// Headers: X-Goog-Api-Key, X-Goog-FieldMask (mandatory), X-Goog-Maps-Solution-ID
// FieldMask: routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline (Essentials tier — no traffic).
// Response: { "routes":[{ "duration":"120s", "distanceMeters":N, "polyline":{ "encodedPolyline":"..." } }] }.
// Duration is a Duration string ("120s") parsed via TrimEnd('s'). travelMode: DRIVE | WALK | TRANSIT.
// Cache TTL 12 h per leg — well within the ToS 30-day caching limit. Data never used for ML training.
```
Also update the inline comment at ~line 43 (`// computeRouteMatrix over the missing ...`) to `// computeRoutes over each missing (origin,dest) pair.`

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleRouteServiceTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS (no regressions).

- [ ] **Step 7: Compliance gate**

Run the `google-maps-platform` skill's compliance review against `GoogleRouteService.cs`. Confirm: field mask unchanged, no `routingPreference`, attribution header present, 12h cache within ToS. Fix any finding before committing.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleRouteServiceTests.cs
git commit -m "feat(trips): resolve legs via Routes API computeRoutes to get road geometry

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Frontend contract + per-Leg segment logic

Widen the TS `LegDto`, fix the compile-breaking test literal, and add a **pure, unit-tested** `buildSegments` helper plus the `anyEstimated` summary flag in `useDayRoute`.

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (~line 491 & 501)
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts`
- Modify: `frontend/src/pages/trips/hooks/useSchedule.test.ts:8`
- Test: `frontend/src/pages/trips/hooks/useDayRoute.test.ts` (create)

**Interfaces:**
- Consumes: wire `LegDto { seconds, meters, encodedPolyline, source }`.
- Produces: `type RouteSource = 'Routed' | 'Estimated'`; `interface RouteSegment { from: {lat:number;lng:number}; to: {lat:number;lng:number}; encodedPolyline: string | null; source: RouteSource }`; `interface LegPoint { lat:number; lng:number; alive:boolean; encodedPolyline:string|null; source:RouteSource }`; `function buildSegments(points: LegPoint[]): RouteSegment[]`; `useDayRoute(...)` now also returns `segments: RouteSegment[]`.

- [ ] **Step 1: Widen the api.ts types**

`frontend/src/shared/api/api.ts` — next to `TravelMode` (line ~491) add:
```ts
export type RouteSource = 'Routed' | 'Estimated'
```
and replace the `LegDto` interface (line ~501):
```ts
export interface LegDto { seconds: number; meters: number; encodedPolyline: string | null; source: RouteSource }
```

- [ ] **Step 2: Fix the compile-breaking leg literal in the schedule test**

`frontend/src/pages/trips/hooks/useSchedule.test.ts` — line 8, the `stop` helper's `legToReach` literal:
```ts
legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated'},
```

- [ ] **Step 3: Write the failing `buildSegments` test**

`frontend/src/pages/trips/hooks/useDayRoute.test.ts`:
```ts
import {describe, it, expect} from 'vitest'
import {buildSegments, type LegPoint} from './useDayRoute'

const pt = (lat: number, over: Partial<LegPoint> = {}): LegPoint => ({
  lat, lng: 100, alive: true, encodedPolyline: null, source: 'Estimated', ...over,
})

describe('buildSegments', () => {
  it('makes one segment fewer than the alive points', () => {
    const segs = buildSegments([pt(1), pt(2), pt(3)])
    expect(segs).toHaveLength(2)
    expect(segs[0].from.lat).toBe(1)
    expect(segs[0].to.lat).toBe(2)
  })

  it('carries polyline+Routed when the pair is consecutive', () => {
    const segs = buildSegments([
      pt(1),
      pt(2, {encodedPolyline: 'abc', source: 'Routed'}),
    ])
    expect(segs[0].source).toBe('Routed')
    expect(segs[0].encodedPolyline).toBe('abc')
  })

  it('drops a dead point and falls back to a straight Estimated segment across the gap', () => {
    const segs = buildSegments([
      pt(1),
      pt(2, {alive: false, encodedPolyline: 'skip', source: 'Routed'}),
      pt(3, {encodedPolyline: 'xyz', source: 'Routed'}),
    ])
    expect(segs).toHaveLength(1)
    expect(segs[0].from.lat).toBe(1)
    expect(segs[0].to.lat).toBe(3)
    expect(segs[0].source).toBe('Estimated') // gap → cannot trust point 3's polyline
    expect(segs[0].encodedPolyline).toBeNull()
  })
})
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `npm --prefix frontend test -- src/pages/trips/hooks/useDayRoute.test.ts`
Expected: FAIL — `buildSegments` is not exported.

- [ ] **Step 5: Implement `buildSegments` + segments/flag in `useDayRoute`**

`frontend/src/pages/trips/hooks/useDayRoute.ts`:

Add `RouteSource` to the api import:
```ts
import type {ItineraryDayDto, RouteSource} from '../../../shared/api/api'
```

Add the exported types + pure helper (near the top, after `RouteStop`):
```ts
export interface RouteSegment {
  from: {lat: number; lng: number}
  to: {lat: number; lng: number}
  encodedPolyline: string | null
  source: RouteSource
}

export interface LegPoint {
  lat: number
  lng: number
  alive: boolean // coords finite & place resolved
  encodedPolyline: string | null // the leg that REACHES this point
  source: RouteSource
}

// Connect consecutive SURVIVING points. A segment keeps the incoming leg's real
// geometry only when its two endpoints are adjacent in the original order; a dropped
// point in between invalidates that geometry, so we render a straight Estimated line.
export function buildSegments(points: LegPoint[]): RouteSegment[] {
  const alive = points.map((p, i) => ({...p, i})).filter((p) => p.alive)
  const segs: RouteSegment[] = []
  for (let k = 1; k < alive.length; k++) {
    const a = alive[k - 1]
    const b = alive[k]
    const adjacent = b.i === a.i + 1
    segs.push({
      from: {lat: a.lat, lng: a.lng},
      to: {lat: b.lat, lng: b.lng},
      encodedPolyline: adjacent ? b.encodedPolyline : null,
      source: adjacent ? b.source : 'Estimated',
    })
  }
  return segs
}
```

Inside the hook, after the existing `route` memo, add:
```ts
const segments = useMemo<RouteSegment[]>(
  () =>
    buildSegments(
      scheduled.map((s) => {
        const p = placesById[s.stop.tripPlaceId]
        const alive = !!p && Number.isFinite(p.lat) && Number.isFinite(p.lng)
        return {
          lat: alive ? p.lat : 0,
          lng: alive ? p.lng : 0,
          alive,
          encodedPolyline: s.stop.legToReach?.encodedPolyline ?? null,
          source: s.stop.legToReach?.source ?? 'Estimated', // missing source → treat as Estimated
        }
      }),
    ),
  [scheduled, placesById],
)

const anyEstimated = scheduled.some((s) => s.stop.legToReach?.source === 'Estimated')
```

Replace the `summaryText` assignment (lines ~81-83) with:
```ts
const summaryText = route.length
  ? `${route.length} จุด · ${anyEstimated ? '~' : ''}${totalKm.toFixed(1)} กม · ${spanText}${anyEstimated ? ' · ระยะโดยประมาณ' : ''}`
  : ''
```

Add `segments` to the returned object:
```ts
return {
  route,
  segments,
  dayLabel: dayIndex >= 0 ? `วัน ${dayIndex + 1}` : '',
  summaryText,
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `npm --prefix frontend test -- src/pages/trips/hooks/useDayRoute.test.ts`
Expected: PASS (3 cases).

- [ ] **Step 7: Typecheck + full frontend test run**

Run: `npm --prefix frontend run build`
Expected: `tsc -b` passes (the widened `LegDto` and the fixed `useSchedule.test.ts` literal compile).
Run: `npm --prefix frontend test`
Expected: PASS (all suites).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/trips/hooks/useDayRoute.ts \
        frontend/src/pages/trips/hooks/useDayRoute.test.ts \
        frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): per-leg route segments + estimated summary flag in useDayRoute

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Frontend — draw per-Leg polylines on the map

Replace the single straight geodesic line with per-Leg segments: solid-curved decoded polyline for `Routed`, dashed-faded straight for `Estimated`.

**Files:**
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx` (~lines 128-133)

**Interfaces:**
- Consumes: `RouteSegment[]` from `useDayRoute` (Task 3).
- Produces: `TripMap` accepts a new `segments?: RouteSegment[]` prop.

- [ ] **Step 1: Import the segment type + geometry library, replace `RoutePolyline`**

`frontend/src/pages/trips/components/TripMap.tsx` — update the type import:
```ts
import type {RouteStop, RouteSegment} from '../hooks/useDayRoute'
```
Replace the `RoutePolyline` component (lines 29-49, including the leading comment) with:
```tsx
// Per-leg route lines. Routed legs draw the decoded encodedPolyline (road-following,
// solid teal); Estimated legs draw a dashed, faded, straight line between the two stops
// — an honest "we're guessing this segment" signal (ADR-023/024). @vis.gl/react-google-maps
// has no <Polyline>, so create google.maps.Polyline imperatively and dispose ALL of them.
const DASH = {path: 'M 0,-1 0,1', strokeOpacity: 0.55, strokeColor: '#0e8f9e', scale: 3}

function RouteSegments({segments}: {segments: RouteSegment[]}) {
  const map = useMap()
  const maps = useMapsLibrary('maps')
  const geometry = useMapsLibrary('geometry')
  useEffect(() => {
    if (!map || !maps || !geometry || segments.length === 0) return
    const lines = segments.map((seg) => {
      const routed = seg.source === 'Routed' && !!seg.encodedPolyline
      const path = routed
        ? geometry.encoding.decodePath(seg.encodedPolyline as string)
        : [seg.from, seg.to]
      const opts = routed
        ? {path, strokeColor: '#0e8f9e', strokeOpacity: 0.9, strokeWeight: 4}
        : {path, strokeOpacity: 0, icons: [{icon: DASH, offset: '0', repeat: '12px'}]}
      const line = new maps.Polyline(opts)
      line.setMap(map)
      return line
    })
    return () => lines.forEach((l) => l.setMap(null))
  }, [map, maps, geometry, segments])
  return null
}
```

- [ ] **Step 2: Add the `segments` prop and render it**

In the `TripMap` prop type (lines ~69-79) add `segments?: RouteSegment[]`:
```tsx
export function TripMap({
  places,
  route,
  segments,
  summaryLabel,
  summaryText,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  segments?: RouteSegment[]
  summaryLabel?: string
  summaryText?: string
}) {
```
In the `routeMode` block, replace `<RoutePolyline path={path} />` (line ~117) with:
```tsx
<RouteSegments segments={segments ?? []} />
```
Leave `<FitBounds path={path} />` and the marker loop unchanged.

- [ ] **Step 3: Pass `segments` from `TripDetailPage`**

`frontend/src/pages/trips/TripDetailPage.tsx` — in the desktop `<TripMap>` (lines ~128-133), add the prop alongside `route`:
```tsx
segments={tab === 'itinerary' ? dayRoute.segments : undefined}
```
(Match the exact `tab`/`dayRoute` expression already used for the `route` prop on the adjacent line.)

- [ ] **Step 4: Typecheck + lint**

Run: `npm --prefix frontend run build`
Expected: `tsc -b` + `vite build` pass.
Run: `npm --prefix frontend run lint`
Expected: no new errors.

- [ ] **Step 5: Verify in the running app**

Run: `npm --prefix frontend run dev`, open a trip's itinerary day.
Expected (no Google billing / no key → all `Estimated`): every leg is a **dashed, faded** straight teal line; the day-summary shows `~… กม · ระยะโดยประมาณ`. If a real key with billing is available, `Routed` legs render as **solid teal curves that follow roads**. Switching days and zooming leaves no leftover lines (cleanup works). Confirm against `docs/mocks/route-estimate-treatment-mock.html`.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/TripMap.tsx \
        frontend/src/pages/trips/TripDetailPage.tsx
git commit -m "feat(trips): draw per-leg route lines (solid routed / dashed estimated)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Frontend — "ประมาณ" chip + `~` prefix on the itinerary pill

**Files:**
- Modify: `frontend/src/pages/trips/components/TravelLeg.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `LegDto.source` (Task 3).

- [ ] **Step 1: Render the estimated markers**

`frontend/src/pages/trips/components/TravelLeg.tsx` — replace the whole file:
```tsx
// frontend/src/pages/trips/components/TravelLeg.tsx
import type {LegDto, TravelMode} from '../../../shared/api/api'

const ICON: Record<TravelMode, string> = {Drive: '🚗', Walk: '🚶', Transit: '🚃'}

export function TravelLeg({leg, mode}: {leg: LegDto; mode: TravelMode}) {
  // Missing/undefined source is treated as Estimated so the pill never over-promises.
  const estimated = leg.source !== 'Routed'
  const prefix = estimated ? '~' : ''
  return (
    <div className="travel-leg">
      <span className="leg-pill">{ICON[mode]} {prefix}{Math.round(leg.seconds / 60)} นาที</span>
      <span className="leg-line" />
      <span className="leg-dist">{prefix}{(leg.meters / 1000).toFixed(1)} กม.</span>
      {estimated && <span className="leg-approx">ประมาณ</span>}
    </div>
  )
}
```

- [ ] **Step 2: Add the chip style**

`frontend/src/pages/trips/trips-tokens.css` — add near the `.travel-leg` rules (~line 228):
```css
.leg-approx {
  font-size: 11px;
  font-weight: 700;
  color: #9a7b25;
  background: #fbf1d9;
  border: 1px solid #e8d6a3;
  border-radius: 6px;
  padding: 2px 7px;
  letter-spacing: 0.02em;
}
```

- [ ] **Step 3: Typecheck + lint**

Run: `npm --prefix frontend run build`
Expected: pass.
Run: `npm --prefix frontend run lint`
Expected: no new errors.

- [ ] **Step 4: Verify in the running app**

With the dev server running (Task 4), open an itinerary day. Expected: each travel-leg pill shows `~N นาที`, `~K.k กม.`, and an amber **ประมาณ** chip while legs are `Estimated`; when `Routed`, no `~` and no chip. Matches the mock's pill treatment.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/TravelLeg.tsx \
        frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): mark estimated legs with ~ prefix and ประมาณ chip

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- computeRoutes swap (field mask, singular origin/destination, parse `routes[0]`, no `routingPreference`, keep attribution) → Task 2 ✓
- `RouteSource {Routed, Estimated}` + widened `LegTime`/`LegDto`, Google=Routed / Haversine=Estimated → Task 1 ✓
- Handler maps new fields (hoisted lookup); `AddStopHandler` unchanged → Task 1 ✓ (AddStop needs no edit)
- Wire contract string enum (no codegen) → relied on `Program.cs:185`; no task needed ✓ (asserted in Task 2 tests via the string values flowing)
- Frontend `api.ts` types; treat missing `source` as Estimated → Task 3 (types) + Task 4/5 (`!== 'Routed'`) ✓
- `useDayRoute` per-leg segments + `anyEstimated` + summary flag; `totalKm` unchanged → Task 3 ✓
- `TripMap` geometry decode + per-leg solid/dashed + dispose all → Task 4 ✓
- `TravelLeg` `~` + chip; `trips-tokens.css` → Task 5 ✓
- `useSchedule.test.ts` compile-break fix → Task 3 ✓
- De-stale comments (GoogleRouteService, DI, TripMap) → Tasks 1/2/4 ✓
- No change to appsettings/Bicep/DI wiring/`StopEditorDialog`/`useSchedule.ts`/`ItineraryTab` → honored (not touched) ✓
- Compliance gate → Task 2 Step 7 ✓

**Placeholder scan:** No TODO/TBD; every code step shows full code; every run step has an exact command + expected result. ✓

**Type consistency:** `RouteSource` = `{Routed, Estimated}` (C#) / `'Routed' | 'Estimated'` (TS) throughout; `LegTime`/`LegDto` both `(Seconds/seconds, Meters/meters, EncodedPolyline/encodedPolyline, Source/source)`; `buildSegments(points: LegPoint[]): RouteSegment[]` and `RouteSegment {from,to,encodedPolyline,source}` consistent between Task 3 (defines) and Task 4 (consumes); `TripMap` `segments?: RouteSegment[]` prop consistent with `TripDetailPage` producer. ✓

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-03-trip-route-geometry-and-honest-fallback.md`. Two execution options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
