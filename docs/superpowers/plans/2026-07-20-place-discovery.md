# "ไปไหนดี" Place Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a map-forward "ไปไหนดี / วันนี้ไปเที่ยวไหนดี" screen that surfaces the user's own saved Places across all Trips, ranked by proximity, with four toggleable free signals — reusing already-stored data, no new Google cost.

**Architecture:** One new **read-only** backend endpoint `GET /api/places` (a Mediator `IQuery` handler that aggregates the user's `TripPlace` rows across Trips, dedupes by `GooglePlaceId`, rolls up `Visited`, no new entity/table/migration). One new frontend feature `pages/discover/` — a `@vis.gl/react-google-maps` map (markers + `@googlemaps/markerclusterer`), a bottom-sheet list, filter toggles, and place actions — with all ranking/filtering computed **client-side** from a single fetch, reusing the existing pure helpers `isOpenAt`, `monthStatus`, `buildStopNavUrl`, and `toResolvedPlace`.

**Tech Stack:** .NET / `martinothamar/Mediator` (source-generated — `IQuery`/`IQueryHandler`, `ValueTask`, `Handle`), EF Core; React 19 + Redux Toolkit + RTK Query + React Router 7, `@vis.gl/react-google-maps` ^1.8.3, `@googlemaps/markerclusterer` (new), vitest (node env).

## Global Constraints

- **Design source of truth:** spec `docs/superpowers/specs/2026-07-20-place-discovery-design.md`; decisions ADR-094–100; glossary Discover / Discovery scope / Discovery signal in `CONTEXT.md`. UI mock: Claude Design "MenuNest design system" → Screens → `place-discovery`.
- **`#42` = the GitHub issue number** for this feature — **open it before Task 1** (`gh issue create --repo ThodsaphonSonthiphin/MenuNest ...`; the repo git remote is named `main`, not `origin`). Every commit subject ends with `(#42)`; the final frontend commit uses `(closes #42)`. Replace `#42` with the real number.
- **No new entity / table / migration / `DbSet`** — the read model is a query over existing tables (`TripPlaces`, `Stops`, `PlaceProfiles`, `Trips`). Do **not** touch `IApplicationDbContext` or the three context implementers.
- **Mediator, not MediatR:** handlers are auto-registered by the source generator — no DI wiring. Auth is global (a `FallbackPolicy` requires an authenticated user) — **no `[Authorize]` attribute needed**. Enums serialize as strings (`JsonStringEnumConverter` is configured).
- **User-scoped, not family-gated:** resolve the caller with `IUserProvisioner.GetOrProvisionCurrentAsync(ct)` (returns the `User` entity; use `user.Id`). Never `RequireFamilyAsync`. Always filter Trips with `t.UserId == user.Id && t.DeletedAt == null` (Trip is soft-deleted).
- **Icons: inline SVG or plain text — never emoji** as UI iconography (the nav label is plain text `ไปไหนดี`). Thai UI strings are inline (no i18n framework).
- **Pre-commit hook runs the FULL suite** (backend `dotnet build` + `dotnet test` Release, frontend `tsc --noEmit` + `npm run build` + vitest). Every commit must leave the whole suite green. **Stage narrowly** with explicit paths — never `git add -A`/`.`; never sweep `daily-state.md` / `AGENTS.md`.
- **Frontend has NO component/visual test harness** (vitest `environment: 'node'`, glob `src/**/*.test.ts`). Only pure `*.test.ts` logic is unit-tested; map/layout/DOM correctness MUST be verified **interactively before push** (CLAUDE.md #36 black-map lesson). Prod deploys on push to `main`.
- **Commands:** backend from `backend/`; frontend from `frontend/`. Backend tests: `dotnet test`. Frontend units: `npm test` (= `vitest run`). Type/build gate: `npm run build`.

---

### Task 1: Backend read model — `DiscoverPlaceDto` + `ListMyPlacesQuery` + `ListMyPlacesHandler`

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext` (`TripPlaces`, `Trips`, `Stops`, `PlaceProfiles`), `IUserProvisioner.GetOrProvisionCurrentAsync`, `SeasonPeriodDto` (from `MenuNest.Application.UseCases.Trips`), domain factories `Trip.Create`, `TripPlace.Create`, `ItineraryDay.Create`, `Stop.Create` + `Stop.SetVisited`, `PlaceProfile.Create`, `User.CreateFromExternalLogin`.
- Produces: `ListMyPlacesQuery` (parameterless `IQuery<IReadOnlyList<DiscoverPlaceDto>>`) and `DiscoverPlaceDto` (consumed by Task 2's controller and Task 3's frontend interface). Field order of `DiscoverPlaceDto` is the JSON contract — Task 3's TS interface must match it.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

public sealed class ListMyPlacesHandlerTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListMyPlacesHandlerTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    private ListMyPlacesHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new ListMyPlacesHandler(_db, users.Object);
    }

    [Fact]
    public async Task Dedupes_same_google_place_across_two_trips_into_one_item_listing_both_trips()
    {
        var t1 = Trip.Create(_user.Id, "Chiang Mai", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        var t2 = Trip.Create(_user.Id, "Temples", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(t1, t2);
        var p1 = TripPlace.Create(t1.Id, "Old name", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-1");
        var p2 = TripPlace.Create(t2.Id, "Old name 2", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-1");
        _db.TripPlaces.AddRange(p1, p2);
        await _db.SaveChangesAsync();
        p2.UpdateDetails("Newer snapshot", PlaceCategory.See, null, null, null); // sets UpdatedAt → representative
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].GooglePlaceId.Should().Be("gp-1");
        result[0].Name.Should().Be("Newer snapshot");
        result[0].Trips.Select(x => x.TripName).Should().BeEquivalentTo(new[] { "Chiang Mai", "Temples" });
    }

    [Fact]
    public async Task Place_without_google_id_is_its_own_item_keyed_tp()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        var p = TripPlace.Create(t.Id, "Unresolved", 13.7, 100.5, PlaceCategory.Eat);
        _db.TripPlaces.Add(p);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].GooglePlaceId.Should().BeNull();
        result[0].Key.Should().Be($"tp:{p.Id}");
    }

    [Fact]
    public async Task Rolls_up_visited_when_any_stop_for_the_place_is_visited()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        var p = TripPlace.Create(t.Id, "Wat", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-9");
        _db.TripPlaces.Add(p);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var stop = Stop.Create(day.Id, p.Id, 0, 60, TravelMode.Drive);
        stop.SetVisited(true);
        _db.Stops.Add(stop);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Visited.Should().BeTrue();
    }

    [Fact]
    public async Task HasProfile_true_when_a_place_profile_exists_for_the_google_id()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        _db.TripPlaces.Add(TripPlace.Create(t.Id, "Cafe", 18.7, 98.9, PlaceCategory.Cafe, googlePlaceId: "gp-7"));
        _db.PlaceProfiles.Add(PlaceProfile.Create(_user.Id, "gp-7"));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result[0].HasProfile.Should().BeTrue();
    }

    [Fact]
    public async Task Excludes_other_users_and_soft_deleted_trips()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        var mine = Trip.Create(_user.Id, "Mine", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        var theirs = Trip.Create(other.Id, "Theirs", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(mine, theirs);
        _db.TripPlaces.Add(TripPlace.Create(mine.Id, "Mine place", 1, 1, PlaceCategory.See, googlePlaceId: "gp-a"));
        _db.TripPlaces.Add(TripPlace.Create(theirs.Id, "Their place", 2, 2, PlaceCategory.See, googlePlaceId: "gp-b"));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Mine place");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ListMyPlacesHandlerTests"`
Expected: **compile failure** — `ListMyPlacesQuery`, `ListMyPlacesHandler`, `DiscoverPlaceDto` do not exist yet.

- [ ] **Step 3: Create the DTOs**

Create `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs`:

```csharp
using MenuNest.Application.UseCases.Trips; // SeasonPeriodDto
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Places;

/// <summary>A Trip that contains a discovered Place (for the "อยู่ในทริป: …" line).</summary>
public sealed record PlaceTripRefDto(Guid TripId, string TripName);

/// <summary>
/// One distinct saved Place surfaced in Discover (ไปไหนดี), deduped by GooglePlaceId
/// across all the user's Trips (ADR-100). Carries raw signal data so the client computes
/// open-now / season / best-time itself. Avoids the banned "Location" term.
/// </summary>
public sealed record DiscoverPlaceDto(
    string Key,
    string? GooglePlaceId,
    Guid RepresentativeTripPlaceId,
    string Name,
    double Lat,
    double Lng,
    string? Address,
    PlaceCategory Category,
    int? PriceLevel,
    string? PhotoUrl,
    string? OpeningHoursJson,
    TimeOnly? BestTimeStart,
    TimeOnly? BestTimeEnd,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods,
    bool Visited,
    bool HasProfile,
    IReadOnlyList<PlaceTripRefDto> Trips);
```

- [ ] **Step 4: Create the query**

Create `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Places.ListMyPlaces;

public sealed record ListMyPlacesQuery() : IQuery<IReadOnlyList<DiscoverPlaceDto>>;
```

- [ ] **Step 5: Create the handler**

Create `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips; // SeasonPeriodDto
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Places.ListMyPlaces;

public sealed class ListMyPlacesHandler : IQueryHandler<ListMyPlacesQuery, IReadOnlyList<DiscoverPlaceDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListMyPlacesHandler(IApplicationDbContext db, IUserProvisioner users)
    {
        _db = db;
        _users = users;
    }

    public async ValueTask<IReadOnlyList<DiscoverPlaceDto>> Handle(ListMyPlacesQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        // The user's Places across all live Trips (+ owning trip name). Materialize:
        // SeasonPeriods is a backing-list value object, mapped in memory (never in SQL).
        var rows = await (from p in _db.TripPlaces
                          join t in _db.Trips on p.TripId equals t.Id
                          where t.UserId == user.Id && t.DeletedAt == null
                          select new { Place = p, TripId = t.Id, TripName = t.Name })
                         .ToListAsync(ct);

        if (rows.Count == 0) return Array.Empty<DiscoverPlaceDto>();

        var placeIds = rows.Select(r => r.Place.Id).ToList();

        var visitedPlaceIds = (await _db.Stops
            .Where(s => placeIds.Contains(s.TripPlaceId) && s.IsVisited)
            .Select(s => s.TripPlaceId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var profiledIds = (await _db.PlaceProfiles
            .Where(pp => pp.UserId == user.Id)
            .Select(pp => pp.GooglePlaceId)
            .ToListAsync(ct)).ToHashSet();

        var groups = rows.GroupBy(r => r.Place.GooglePlaceId ?? $"tp:{r.Place.Id}");

        var result = new List<DiscoverPlaceDto>();
        foreach (var g in groups)
        {
            var rep = g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place;
            var trips = g.Select(r => new PlaceTripRefDto(r.TripId, r.TripName))
                         .GroupBy(x => x.TripId)
                         .Select(x => x.First())
                         .ToList();
            var visited = g.Any(r => visitedPlaceIds.Contains(r.Place.Id));
            var hasProfile = rep.GooglePlaceId != null && profiledIds.Contains(rep.GooglePlaceId);

            result.Add(new DiscoverPlaceDto(
                g.Key,
                rep.GooglePlaceId,
                rep.Id,
                rep.Name,
                rep.Lat,
                rep.Lng,
                rep.Address,
                rep.Category,
                rep.PriceLevel,
                rep.PhotoUrl,
                rep.OpeningHoursJson,
                rep.BestTimeStart,
                rep.BestTimeEnd,
                rep.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
                visited,
                hasProfile,
                trips));
        }

        return result.OrderBy(r => r.Name).ToList();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ListMyPlacesHandlerTests"`
Expected: **PASS** (5 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Places backend/tests/MenuNest.Application.UnitTests/Places
git commit -m "feat(discover): add ListMyPlaces read model (dedup by place_id, visited rollup) (#42)"
```

---

### Task 2: Backend — `PlacesController` (`GET /api/places`)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Controllers/PlacesController.cs`

**Interfaces:**
- Consumes: `ListMyPlacesQuery` (Task 1), `IMediator`.
- Produces: the HTTP route `GET /api/places` → `DiscoverPlaceDto[]` (consumed by Task 3's RTK Query).

- [ ] **Step 1: Create the controller**

Create `backend/src/MenuNest.WebApi/Controllers/PlacesController.cs` (models `MeController` — class-level `[Route("api/[controller]")]` → `api/places`; global auth means no `[Authorize]` needed):

```csharp
using Mediator;
using MenuNest.Application.UseCases.Places;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlacesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlacesController(IMediator mediator) => _mediator = mediator;

    /// <summary>All the caller's saved Places across every Trip (deduped) for Discover.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DiscoverPlaceDto>>> ListMyPlaces(CancellationToken ct)
        => Ok(await _mediator.Send(new ListMyPlacesQuery(), ct));
}
```

- [ ] **Step 2: Verify it builds and the endpoint is wired**

Run: `cd backend && dotnet build`
Expected: **Build succeeded** (the Mediator source generator picks up the handler; the controller is auto-discovered — no DI edits).

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/PlacesController.cs
git commit -m "feat(discover): expose GET /api/places (#42)"
```

---

### Task 3: Frontend RTK Query — `DiscoverPlaceDto`, `MyPlaces` tag, `listMyPlaces`, invalidations

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (tagTypes ~574-604; add DTO near the Trips DTO block ~493-537; add endpoint in the `endpoints` builder; add hook to the `export const { … } = api` block ~1488-1610; extend `addTripPlace`/`updateTripPlace`/`deleteTripPlace` invalidatesTags ~1322-1333)

**Interfaces:**
- Consumes: `GET /api/places` (Task 2).
- Produces: `DiscoverPlaceDto` (TS), `useListMyPlacesQuery` hook (consumed by Tasks 5, 7, 8).

- [ ] **Step 1: Add the `DiscoverPlaceDto` interface**

In `frontend/src/shared/api/api.ts`, in the Trips DTO block (after `ResolvedPlaceDto`, ~line 531), add — field names/order match the C# record's JSON:

```ts
export interface PlaceTripRefDto { tripId: string; tripName: string }
export interface DiscoverPlaceDto {
    key: string
    googlePlaceId: string | null
    representativeTripPlaceId: string
    name: string
    lat: number
    lng: number
    address: string | null
    category: PlaceCategory
    priceLevel: number | null
    photoUrl: string | null
    openingHoursJson: string | null
    bestTimeStart: string | null
    bestTimeEnd: string | null
    seasonPeriods: SeasonPeriod[]
    visited: boolean
    hasProfile: boolean
    trips: PlaceTripRefDto[]
}
```

- [ ] **Step 2: Add the `'MyPlaces'` tag**

In the `tagTypes` array (after `'ChecklistItems'`, ~line 604), add:

```ts
        'ChecklistItems',
        'MyPlaces',
```

- [ ] **Step 3: Add the query endpoint**

In the `endpoints: (build) => ({ … })` block, near the trip-place endpoints, add:

```ts
        listMyPlaces: build.query<DiscoverPlaceDto[], void>({
            query: () => '/api/places',
            providesTags: ['MyPlaces'],
        }),
```

- [ ] **Step 4: Extend the trip-place mutations to invalidate `'MyPlaces'`**

In `addTripPlace`, `updateTripPlace`, `deleteTripPlace` (~1322-1333), append `'MyPlaces'` to each returned `invalidatesTags` array so Discover refreshes after a capture/edit/delete. Example for `addTripPlace`:

```ts
    invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}, 'MyPlaces'],
```

Apply the identical `'MyPlaces'` append to `updateTripPlace` and `deleteTripPlace`.

- [ ] **Step 5: Export the generated hook**

In the `export const { … } = api` block (in the Trips section, after `useDeleteTripPlaceMutation`), add:

```ts
    useListMyPlacesQuery,
```

- [ ] **Step 6: Verify it type-checks**

Run: `cd frontend && npm run build`
Expected: **build succeeds** (no `tsc` errors).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(discover): add listMyPlaces query + MyPlaces cache tag (#42)"
```

---

### Task 4: Frontend pure lib — `distance.ts` (Haversine)

**Files:**
- Create: `frontend/src/pages/discover/lib/distance.ts`
- Test: `frontend/src/pages/discover/lib/distance.test.ts`

**Interfaces:**
- Produces: `haversineKm(a, b): number` (consumed by Task 5's `discoverFilter`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/discover/lib/distance.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {haversineKm} from './distance'

describe('haversineKm', () => {
  it('is zero for identical points', () => {
    expect(haversineKm({lat: 13.75, lng: 100.5}, {lat: 13.75, lng: 100.5})).toBeCloseTo(0, 5)
  })
  it('matches a known distance (Bangkok ↔ Chiang Mai ≈ 580 km)', () => {
    const d = haversineKm({lat: 13.7563, lng: 100.5018}, {lat: 18.7883, lng: 98.9853})
    expect(d).toBeGreaterThan(560)
    expect(d).toBeLessThan(600)
  })
  it('is symmetric', () => {
    const a = {lat: 13.75, lng: 100.5}, b = {lat: 18.79, lng: 98.99}
    expect(haversineKm(a, b)).toBeCloseTo(haversineKm(b, a), 6)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- distance`
Expected: FAIL — `distance.ts` / `haversineKm` not found.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/pages/discover/lib/distance.ts`:

```ts
export interface LatLng {
  lat: number
  lng: number
}

const R = 6371 // km

/** Great-circle distance in km (mirrors the backend HaversineRouteService formula). */
export function haversineKm(a: LatLng, b: LatLng): number {
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLat = toRad(b.lat - a.lat)
  const dLng = toRad(b.lng - a.lng)
  const lat1 = toRad(a.lat)
  const lat2 = toRad(b.lat)
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) ** 2
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)))
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npm test -- distance`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/discover/lib/distance.ts frontend/src/pages/discover/lib/distance.test.ts
git commit -m "feat(discover): add haversineKm distance helper (#42)"
```

---

### Task 5: Frontend pure lib — `discoverFilter.ts` (compose signals + toggles + scope)

**Files:**
- Create: `frontend/src/pages/discover/lib/discoverFilter.ts`
- Test: `frontend/src/pages/discover/lib/discoverFilter.test.ts`

**Interfaces:**
- Consumes: `haversineKm` (Task 4); reuses `isOpenAt` from `../../trips/hooks/useSchedule` and `monthStatus` from `../../trips/lib/season`; `DiscoverPlaceDto` (Task 3).
- Produces: `DiscoverToggles`, `DiscoverInput`, `DiscoverPlaceView`, `applyDiscover(...)` (consumed by Task 8's page).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/discover/lib/discoverFilter.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {applyDiscover, type DiscoverInput} from './discoverFilter'
import type {DiscoverPlaceDto} from '../../../shared/api/api'

// Mon 2026-06-01 10:00 local (getDay()=1, 600 min, getMonth()=5)
const NOW = new Date(2026, 5, 1, 10, 0, 0)
const openMon = JSON.stringify({periods: [{open: {day: 1, hour: 9, minute: 0}, close: {day: 1, hour: 17, minute: 0}}]})

const place = (over: Partial<DiscoverPlaceDto>): DiscoverPlaceDto => ({
  key: 'k', googlePlaceId: 'g', representativeTripPlaceId: 't', name: 'P',
  lat: 13.75, lng: 100.5, address: null, category: 'See', priceLevel: null, photoUrl: null,
  openingHoursJson: null, bestTimeStart: null, bestTimeEnd: null, seasonPeriods: [],
  visited: false, hasProfile: false, trips: [], ...over,
})

const base: DiscoverInput = {
  anchor: {lat: 13.75, lng: 100.5}, viewport: null, category: 'all',
  toggles: {openNow: false, season: false, bestTime: false, hideVisited: false}, now: NOW,
}

describe('applyDiscover', () => {
  it('sorts by distance ascending from the anchor', () => {
    const near = place({key: 'near', lat: 13.75, lng: 100.5})
    const far = place({key: 'far', lat: 18.79, lng: 98.99})
    const out = applyDiscover([far, near], base)
    expect(out.map((p) => p.key)).toEqual(['near', 'far'])
    expect(out[0].distanceKm).toBeCloseTo(0, 3)
  })

  it('open-now toggle drops places closed now but keeps unknown-hours places', () => {
    const open = place({key: 'open', openingHoursJson: openMon})
    const closed = place({key: 'closed', openingHoursJson: JSON.stringify({periods: [{open: {day: 1, hour: 12, minute: 0}, close: {day: 1, hour: 14, minute: 0}}]})})
    const unknown = place({key: 'unknown', openingHoursJson: null})
    const out = applyDiscover([open, closed, unknown], {...base, toggles: {...base.toggles, openNow: true}})
    expect(out.map((p) => p.key).sort()).toEqual(['open', 'unknown'])
  })

  it('season toggle drops "bad" this month and ranks "good" above neutral', () => {
    const bad = place({key: 'bad', seasonPeriods: [{kind: 'Bad', months: [5], note: null}]})
    const good = place({key: 'good', lat: 18, lng: 99, seasonPeriods: [{kind: 'Good', months: [5], note: null}]})
    const none = place({key: 'none', lat: 14, lng: 100})
    const out = applyDiscover([bad, none, good], {...base, toggles: {...base.toggles, season: true}})
    expect(out.map((p) => p.key)).not.toContain('bad')
    expect(out[0].key).toBe('good') // good ranked first despite being farther
  })

  it('hideVisited toggle removes visited places', () => {
    const seen = place({key: 'seen', visited: true})
    const fresh = place({key: 'fresh'})
    const out = applyDiscover([seen, fresh], {...base, toggles: {...base.toggles, hideVisited: true}})
    expect(out.map((p) => p.key)).toEqual(['fresh'])
  })

  it('category filter keeps only the chosen category', () => {
    const eat = place({key: 'eat', category: 'Eat'})
    const see = place({key: 'see', category: 'See'})
    const out = applyDiscover([eat, see], {...base, category: 'Eat'})
    expect(out.map((p) => p.key)).toEqual(['eat'])
  })

  it('viewport filter keeps only places inside the bounds', () => {
    const inside = place({key: 'in', lat: 13.75, lng: 100.5})
    const outside = place({key: 'out', lat: 18.79, lng: 98.99})
    const out = applyDiscover([inside, outside], {...base, viewport: {north: 14, south: 13, east: 101, west: 100}})
    expect(out.map((p) => p.key)).toEqual(['in'])
  })

  it('bestTime toggle ranks a place whose window covers now above others', () => {
    const match = place({key: 'match', lat: 18, lng: 99, bestTimeStart: '09:00:00', bestTimeEnd: '11:00:00'})
    const other = place({key: 'other', lat: 13.75, lng: 100.5, bestTimeStart: '14:00:00', bestTimeEnd: '16:00:00'})
    const out = applyDiscover([other, match], {...base, toggles: {...base.toggles, bestTime: true}})
    expect(out[0].key).toBe('match')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- discoverFilter`
Expected: FAIL — `discoverFilter.ts` not found.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/pages/discover/lib/discoverFilter.ts`:

```ts
import type {DiscoverPlaceDto, PlaceCategory} from '../../../shared/api/api'
import {haversineKm} from './distance'
import {isOpenAt} from '../../trips/hooks/useSchedule'
import {monthStatus} from '../../trips/lib/season'

export interface DiscoverToggles {
  openNow: boolean
  season: boolean
  bestTime: boolean
  hideVisited: boolean
}

export interface ViewportBounds {
  north: number
  south: number
  east: number
  west: number
}

export interface DiscoverInput {
  anchor: {lat: number; lng: number} | null
  viewport: ViewportBounds | null
  category: PlaceCategory | 'all'
  toggles: DiscoverToggles
  now: Date
}

export interface DiscoverPlaceView extends DiscoverPlaceDto {
  distanceKm: number | null
  openNow: boolean | null
  seasonStatus: 'good' | 'bad' | 'none'
  bestTimeMatch: boolean | null
}

function hmsToMinutes(hms: string | null): number | null {
  if (!hms) return null
  const [h, m] = hms.split(':')
  return Number(h) * 60 + Number(m)
}

/** now ∈ [start, end)? null when the window is not fully defined. */
function bestTimeMatch(start: string | null, end: string | null, now: Date): boolean | null {
  const s = hmsToMinutes(start)
  const e = hmsToMinutes(end)
  if (s == null || e == null) return null
  const cur = now.getHours() * 60 + now.getMinutes()
  return cur >= s && cur < e
}

function inViewport(p: DiscoverPlaceDto, v: ViewportBounds): boolean {
  return p.lat <= v.north && p.lat >= v.south && p.lng >= v.west && p.lng <= v.east
}

function toView(p: DiscoverPlaceDto, input: DiscoverInput): DiscoverPlaceView {
  return {
    ...p,
    distanceKm: input.anchor ? haversineKm(input.anchor, {lat: p.lat, lng: p.lng}) : null,
    openNow: isOpenAt(p.openingHoursJson, input.now.getDay(), input.now.getHours() * 60 + input.now.getMinutes()),
    seasonStatus: monthStatus(p.seasonPeriods, input.now.getMonth()).kind,
    bestTimeMatch: bestTimeMatch(p.bestTimeStart, p.bestTimeEnd, input.now),
  }
}

function seasonRank(v: DiscoverPlaceView): number {
  return v.seasonStatus === 'good' ? 1 : 0
}

/** Compute per-place signals, apply category/viewport/toggle filters, and rank. */
export function applyDiscover(places: DiscoverPlaceDto[], input: DiscoverInput): DiscoverPlaceView[] {
  const views = places.map((p) => toView(p, input))
  const filtered = views.filter((v) => {
    if (input.category !== 'all' && v.category !== input.category) return false
    if (input.viewport && !inViewport(v, input.viewport)) return false
    if (input.toggles.openNow && v.openNow === false) return false
    if (input.toggles.season && v.seasonStatus === 'bad') return false
    if (input.toggles.hideVisited && v.visited) return false
    return true
  })
  filtered.sort((a, b) => {
    if (input.toggles.season) {
      const s = seasonRank(b) - seasonRank(a)
      if (s) return s
    }
    if (input.toggles.bestTime) {
      const t = (b.bestTimeMatch === true ? 1 : 0) - (a.bestTimeMatch === true ? 1 : 0)
      if (t) return t
    }
    const da = a.distanceKm ?? Infinity
    const db = b.distanceKm ?? Infinity
    if (da !== db) return da - db
    return a.name.localeCompare(b.name)
  })
  return filtered
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npm test -- discoverFilter`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/discover/lib/discoverFilter.ts frontend/src/pages/discover/lib/discoverFilter.test.ts
git commit -m "feat(discover): add discoverFilter (distance + open-now + season + best-time + toggles) (#42)"
```

---

### Task 6: Frontend UI state — `discoverSlice.ts` + store registration

**Files:**
- Create: `frontend/src/pages/discover/discoverSlice.ts`
- Test: `frontend/src/pages/discover/discoverSlice.test.ts`
- Modify: `frontend/src/store/index.ts` (import ~line 17; reducer key ~line 30)

**Interfaces:**
- Consumes: `PlaceCategory` (`shared/api/api`), `DiscoverToggles`/`ViewportBounds` (Task 5).
- Produces: the `discover` reducer + actions `setAnchor`, `setScope`, `setCategoryFilter`, `toggleSignal`, `setSelectedKey` (consumed by Task 8).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/discover/discoverSlice.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import reducer, {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey, initialState} from './discoverSlice'

describe('discoverSlice', () => {
  it('defaults: openNow/season/hideVisited on, bestTime off, category all', () => {
    expect(initialState.toggles).toEqual({openNow: true, season: true, bestTime: false, hideVisited: true})
    expect(initialState.categoryFilter).toBe('all')
    expect(initialState.anchor).toBeNull()
  })
  it('setAnchor stores coordinates', () => {
    const s = reducer(initialState, setAnchor({lat: 13.75, lng: 100.5}))
    expect(s.anchor).toEqual({lat: 13.75, lng: 100.5})
  })
  it('toggleSignal flips one toggle without touching the others', () => {
    const s = reducer(initialState, toggleSignal('bestTime'))
    expect(s.toggles.bestTime).toBe(true)
    expect(s.toggles.openNow).toBe(true)
  })
  it('setScope + setCategoryFilter + setSelectedKey update their fields', () => {
    let s = reducer(initialState, setScope({north: 1, south: 0, east: 1, west: 0}))
    s = reducer(s, setCategoryFilter('Eat'))
    s = reducer(s, setSelectedKey('gp-1'))
    expect(s.scope).toEqual({north: 1, south: 0, east: 1, west: 0})
    expect(s.categoryFilter).toBe('Eat')
    expect(s.selectedKey).toBe('gp-1')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- discoverSlice`
Expected: FAIL — `discoverSlice.ts` not found.

- [ ] **Step 3: Write the slice**

Create `frontend/src/pages/discover/discoverSlice.ts` (mirrors `tripsSlice.ts` conventions — `s`/`a` params, default-export the reducer):

```ts
import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'
import type {PlaceCategory} from '../../shared/api/api'
import type {DiscoverToggles, ViewportBounds} from './lib/discoverFilter'

interface DiscoverState {
  anchor: {lat: number; lng: number} | null
  scope: ViewportBounds | null
  categoryFilter: PlaceCategory | 'all'
  toggles: DiscoverToggles
  selectedKey: string | null
}

export const initialState: DiscoverState = {
  anchor: null,
  scope: null,
  categoryFilter: 'all',
  toggles: {openNow: true, season: true, bestTime: false, hideVisited: true},
  selectedKey: null,
}

const discoverSlice = createSlice({
  name: 'discover',
  initialState,
  reducers: {
    setAnchor(s, a: PayloadAction<{lat: number; lng: number} | null>) { s.anchor = a.payload },
    setScope(s, a: PayloadAction<ViewportBounds | null>) { s.scope = a.payload },
    setCategoryFilter(s, a: PayloadAction<PlaceCategory | 'all'>) { s.categoryFilter = a.payload },
    toggleSignal(s, a: PayloadAction<keyof DiscoverToggles>) { s.toggles[a.payload] = !s.toggles[a.payload] },
    setSelectedKey(s, a: PayloadAction<string | null>) { s.selectedKey = a.payload },
  },
})

export const {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} = discoverSlice.actions
export default discoverSlice.reducer
```

- [ ] **Step 4: Register the slice in the store**

In `frontend/src/store/index.ts`, add the import beside the other slice imports (~line 17):

```ts
import discoverSlice from '../pages/discover/discoverSlice'
```

and the reducer key inside `configureStore({ reducer: { … } })` (beside `trips`):

```ts
    trips: tripsSlice,
    discover: discoverSlice,
```

- [ ] **Step 5: Run the test + type-check**

Run: `cd frontend && npm test -- discoverSlice && npm run build`
Expected: tests PASS (4); build succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/discover/discoverSlice.ts frontend/src/pages/discover/discoverSlice.test.ts frontend/src/store/index.ts
git commit -m "feat(discover): add discover UI slice (anchor/scope/filters/toggles) (#42)"
```

---

### Task 7: Frontend — `DiscoverMap` component (markers + clustering + viewer pin + scope)

**Files:**
- Create: `frontend/src/pages/discover/components/DiscoverMap.tsx`
- Modify: `frontend/package.json` (add `@googlemaps/markerclusterer`)

**Interfaces:**
- Consumes: `DiscoverPlaceView` (Task 5), `@vis.gl/react-google-maps`, `@googlemaps/markerclusterer`, `trackGoogleMapsError` (`shared/telemetry/googleMapsTelemetry`).
- Produces: `<DiscoverMap places anchor selectedKey onSelect onScopeChange />` (consumed by Task 8).

> **Note:** No unit test — the SPA has no component harness (Global Constraints). The gate is `npm run build` + interactive verification in Task 10. Keep all pure logic in Tasks 4–5 (already tested); this component is wiring only.

- [ ] **Step 1: Add the marker-clusterer dependency**

Run: `cd frontend && npm install @googlemaps/markerclusterer`
Expected: `@googlemaps/markerclusterer` added to `package.json` dependencies; lockfile updated.

- [ ] **Step 2: Write the component**

Create `frontend/src/pages/discover/components/DiscoverMap.tsx` (reuses the `TripMap` env/key pattern — `||` not `??` for the map id — and `useMap`/`useMapsLibrary`; markers are `AdvancedMarkerElement`s managed by a `MarkerClusterer`, tap → `onSelect`):

```tsx
import {useEffect, useRef} from 'react'
import {APIProvider, Map, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import {MarkerClusterer} from '@googlemaps/markerclusterer'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

const KEY = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) || 'DEMO_MAP_ID'
const BKK_CENTER = {lat: 13.7563, lng: 100.5018}

// Category → pin colour (mirrors TripMap CAT_COLOR).
const CAT_COLOR: Record<string, string> = {
  Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76', Cafe: '#b4791f', Shop: '#c2418f', Other: '#0e8f9e',
}

interface Props {
  places: DiscoverPlaceView[]
  anchor: {lat: number; lng: number} | null
  selectedKey: string | null
  onSelect: (key: string) => void
  onScopeChange: (b: {north: number; south: number; east: number; west: number}) => void
}

function pinElement(color: string, dimmed: boolean): HTMLElement {
  const el = document.createElement('div')
  el.className = 'disc-pin'
  el.style.cssText = `width:26px;height:26px;border-radius:50% 50% 50% 2px;transform:rotate(45deg);border:2.5px solid #fff;box-shadow:0 3px 8px rgba(15,23,42,.3);background:${color};opacity:${dimmed ? 0.45 : 1}`
  return el
}

function Markers({places, onSelect}: {places: DiscoverPlaceView[]; onSelect: (k: string) => void}) {
  const map = useMap()
  const markerLib = useMapsLibrary('marker')
  const clustererRef = useRef<MarkerClusterer | null>(null)

  useEffect(() => {
    if (!map || !markerLib) return
    const markers = places.map((p) => {
      const marker = new markerLib.AdvancedMarkerElement({
        position: {lat: p.lat, lng: p.lng},
        title: p.name,
        content: pinElement(CAT_COLOR[p.category] ?? CAT_COLOR.Other, p.visited),
      })
      marker.addListener('gmp-click', () => onSelect(p.key))
      return marker
    })
    clustererRef.current = new MarkerClusterer({map, markers})
    return () => {
      clustererRef.current?.clearMarkers()
      clustererRef.current = null
      markers.forEach((m) => (m.map = null))
    }
  }, [map, markerLib, places, onSelect])

  return null
}

function ViewerPin({anchor}: {anchor: {lat: number; lng: number} | null}) {
  const map = useMap()
  const markerLib = useMapsLibrary('marker')
  useEffect(() => {
    if (!map || !markerLib || !anchor) return
    const dot = document.createElement('div')
    dot.className = 'viewer-pin'
    const marker = new markerLib.AdvancedMarkerElement({position: anchor, content: dot, zIndex: 0, title: 'คุณอยู่ที่นี่'})
    marker.map = map
    return () => { marker.map = null }
  }, [map, markerLib, anchor])
  return null
}

export function DiscoverMap({places, anchor, selectedKey: _sel, onSelect, onScopeChange}: Props) {
  if (!KEY) {
    return <div className="trip-map-fallback">ตั้งค่า VITE_GOOGLE_MAPS_BROWSER_KEY เพื่อแสดงแผนที่</div>
  }
  return (
    <APIProvider apiKey={KEY} onError={trackGoogleMapsError}>
      <div className="discover-map">
        <Map
          mapId={MAP_ID}
          defaultCenter={anchor ?? BKK_CENTER}
          defaultZoom={anchor ? 13 : 6}
          gestureHandling="greedy"
          disableDefaultUI
          internalUsageAttributionIds={['gmp_git_agentskills_v1']}
          onCameraChanged={(ev) => {
            const b = ev.map.getBounds()
            if (!b) return
            const ne = b.getNorthEast()
            const sw = b.getSouthWest()
            onScopeChange({north: ne.lat(), south: sw.lat(), east: ne.lng(), west: sw.lng()})
          }}
        >
          <Markers places={places} onSelect={onSelect} />
          <ViewerPin anchor={anchor} />
        </Map>
      </div>
    </APIProvider>
  )
}
```

- [ ] **Step 3: Verify it type-checks and builds**

Run: `cd frontend && npm run build`
Expected: build succeeds (`@googlemaps/markerclusterer` resolves; no `tsc` errors).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/discover/components/DiscoverMap.tsx frontend/package.json frontend/package-lock.json
git commit -m "feat(discover): add DiscoverMap (clustered category markers + viewer pin + scope) (#42)"
```

---

### Task 8: Frontend — `DiscoverPage` + FilterBar + bottom sheet + geolocation + go-now actions + routing/nav/home

**Files:**
- Create: `frontend/src/pages/discover/DiscoverPage.tsx`
- Create: `frontend/src/pages/discover/DiscoverPage.css`
- Create: `frontend/src/pages/discover/components/FilterBar.tsx`
- Create: `frontend/src/pages/discover/components/PlaceBottomSheet.tsx`
- Create: `frontend/src/pages/discover/components/PlaceSheet.tsx`
- Create: `frontend/src/pages/discover/index.ts`
- Modify: `frontend/src/router.tsx` (import ~line 18; route in the auth-only `AppLayout` group ~line 70)
- Modify: `frontend/src/shared/components/NavBar.tsx` (`navItems` ~line 7-17)
- Modify: `frontend/src/pages/settings/homeOptions.ts` (`HOME_OPTIONS` ~line 8-18)
- Modify: `frontend/src/pages/settings/homeOptions.test.ts` (the hard-coded no-family array ~line 12)

**Interfaces:**
- Consumes: `useListMyPlacesQuery` (Task 3), `applyDiscover` (Task 5), `discoverSlice` actions (Task 6), `DiscoverMap` (Task 7), `buildStopNavUrl` (`../trips/lib/navUrl`), `useAppDispatch`/`useAppSelector`.
- Produces: the routed `/discover` page + nav entry + Home option.

> **Note:** Components are wiring over tested pure logic; gate is `npm run build` + the `homeOptions.test.ts` update + interactive verify (Task 10). The mock `place-discovery` (Screens card) is the visual source of truth — port its structure/classes into `DiscoverPage.css`.

- [ ] **Step 1: Update `homeOptions.ts` + its test first (TDD for the one testable change)**

In `frontend/src/pages/settings/homeOptions.ts`, add to `HOME_OPTIONS` (after `/trips`):

```ts
  { path: '/trips', label: 'Trips', requiresFamily: false },
  { path: '/discover', label: 'ไปไหนดี', requiresFamily: false },
```

In `frontend/src/pages/settings/homeOptions.test.ts`, update the hard-coded no-family assertion (line ~12) to include `/discover`:

```ts
    expect(opts.map((o) => o.path)).toEqual(['/health', '/pomodoro', '/trips', '/discover'])
```

Run: `cd frontend && npm test -- homeOptions`
Expected: PASS (the array now matches; `resolveHomePath('/discover')` is implicitly accepted since it is in `HOME_OPTIONS`).

- [ ] **Step 2: Create the FilterBar**

Create `frontend/src/pages/discover/components/FilterBar.tsx` (category dropdown + four toggle chips; inline-SVG/text only, no emoji):

```tsx
import type {PlaceCategory} from '../../../shared/api/api'
import type {DiscoverToggles} from '../lib/discoverFilter'

const CATEGORIES: {value: PlaceCategory | 'all'; label: string}[] = [
  {value: 'all', label: 'ทั้งหมด'}, {value: 'See', label: 'เที่ยว'}, {value: 'Eat', label: 'กิน'},
  {value: 'Cafe', label: 'คาเฟ่'}, {value: 'Stay', label: 'ที่พัก'}, {value: 'Shop', label: 'ช้อป'}, {value: 'Other', label: 'อื่น ๆ'},
]
const TOGGLES: {key: keyof DiscoverToggles; label: string}[] = [
  {key: 'openNow', label: 'เปิดตอนนี้'}, {key: 'season', label: 'เดือนนี้'},
  {key: 'bestTime', label: 'ช่วงเวลา'}, {key: 'hideVisited', label: 'ซ่อนที่ไปแล้ว'},
]

interface Props {
  category: PlaceCategory | 'all'
  toggles: DiscoverToggles
  onCategory: (c: PlaceCategory | 'all') => void
  onToggle: (k: keyof DiscoverToggles) => void
}

export function FilterBar({category, toggles, onCategory, onToggle}: Props) {
  return (
    <div className="disc-filters">
      <select className="disc-cat" value={category} onChange={(e) => onCategory(e.target.value as PlaceCategory | 'all')}>
        {CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
      </select>
      {TOGGLES.map((t) => (
        <button
          key={t.key}
          type="button"
          className={toggles[t.key] ? 'disc-chip on' : 'disc-chip'}
          aria-pressed={toggles[t.key]}
          onClick={() => onToggle(t.key)}
        >
          {t.label}
        </button>
      ))}
    </div>
  )
}
```

- [ ] **Step 3: Create the PlaceBottomSheet (ranked list)**

Create `frontend/src/pages/discover/components/PlaceBottomSheet.tsx`:

```tsx
import type {DiscoverPlaceView} from '../lib/discoverFilter'

const CAT_LABEL: Record<string, string> = {See: 'เที่ยว', Eat: 'กิน', Cafe: 'คาเฟ่', Stay: 'ที่พัก', Shop: 'ช้อป', Other: 'อื่น ๆ'}

function distanceLabel(km: number | null): string {
  if (km == null) return ''
  return km < 1 ? `${Math.round(km * 1000)} ม.` : `${km.toFixed(1)} กม.`
}

interface Props {
  places: DiscoverPlaceView[]
  onSelect: (key: string) => void
}

export function PlaceBottomSheet({places, onSelect}: Props) {
  return (
    <div className="disc-sheet">
      <div className="disc-grip" />
      <div className="disc-sheet-head">
        <span className="h">ใกล้คุณ</span>
        <span className="n">{places.length} ที่ · เรียงตามระยะ</span>
      </div>
      <ul className="disc-list">
        {places.length === 0 && <li className="disc-empty">ยังไม่มีที่บันทึกไว้ในบริเวณนี้ — ลองเลื่อนแผนที่ หรือปิดตัวกรอง</li>}
        {places.map((p) => (
          <li key={p.key}>
            <button type="button" className={p.visited ? 'disc-row visited' : 'disc-row'} onClick={() => onSelect(p.key)}>
              <span className="disc-name">{p.name}</span>
              <span className="disc-meta">
                <span className="disc-dist">{distanceLabel(p.distanceKm)}</span>
                <span className="disc-cat-lab">{CAT_LABEL[p.category] ?? p.category}</span>
                {p.openNow === true && <span className="disc-badge open">เปิดอยู่</span>}
                {p.openNow === false && <span className="disc-badge closed">ปิดอยู่</span>}
                {p.seasonStatus === 'good' && <span className="disc-badge season">เดือนนี้ดี</span>}
                {p.trips[0] && <span className="disc-badge trip">{p.trips[0].tripName}</span>}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
```

- [ ] **Step 4: Create the PlaceSheet (selected place + go-now actions)**

Create `frontend/src/pages/discover/components/PlaceSheet.tsx` (Navigate reuses `buildStopNavUrl`; open-trip disambiguates when a place is in >1 trip; add/create-trip buttons are wired in Task 9 via the `onAddToTrip`/`onCreateTrip` props declared here):

```tsx
import {useNavigate} from 'react-router-dom'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {buildStopNavUrl} from '../../trips/lib/navUrl'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onAddToTrip: (place: DiscoverPlaceView) => void
  onCreateTrip: (place: DiscoverPlaceView) => void
}

export function PlaceSheet({place, onClose, onAddToTrip, onCreateTrip}: Props) {
  const navigate = useNavigate()
  const navUrl = buildStopNavUrl({lat: place.lat, lng: place.lng, googlePlaceId: place.googlePlaceId}, 'Drive')

  const openTrip = () => {
    if (place.trips.length === 1) navigate(`/trips/${place.trips[0].tripId}`)
    // >1 trip: the caller renders a small chooser; here we no-op unless single.
  }

  return (
    <div className="disc-detail">
      <div className="disc-grip" />
      <div className="disc-detail-head">
        <div className="disc-detail-title">
          <div className="disc-detail-name">{place.name}</div>
          {place.address && <div className="disc-detail-addr">{place.address}</div>}
        </div>
        <button type="button" className="disc-detail-close" onClick={onClose} aria-label="ปิด">✕</button>
      </div>
      <div className="disc-detail-badges">
        {place.openNow === true && <span className="disc-badge open">เปิดอยู่</span>}
        {place.seasonStatus === 'good' && <span className="disc-badge season">เดือนนี้ควรไป</span>}
        {place.seasonStatus === 'bad' && <span className="disc-badge closed">เดือนนี้ควรเลี่ยง</span>}
        {place.trips.map((t) => <span key={t.tripId} className="disc-badge trip">{t.tripName}</span>)}
      </div>
      <div className="disc-actions">
        {navUrl && <a className="disc-abtn primary" href={navUrl} target="_blank" rel="noopener noreferrer">นำทางด้วย Google Maps</a>}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" onClick={openTrip} disabled={place.trips.length !== 1}>เปิดทริป</button>
          <button type="button" className="disc-abtn ghost" onClick={() => onAddToTrip(place)}>เพิ่มเข้าทริป</button>
        </div>
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" onClick={() => onCreateTrip(place)}>สร้างทริปใหม่</button>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 5: Create the DiscoverPage (assembly + geolocation)**

Create `frontend/src/pages/discover/DiscoverPage.tsx`:

```tsx
import {useEffect, useMemo} from 'react'
import './DiscoverPage.css'
import '../trips/trips-tokens.css'
import {useListMyPlacesQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store'
import {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} from './discoverSlice'
import {applyDiscover} from './lib/discoverFilter'
import {DiscoverMap} from './components/DiscoverMap'
import {FilterBar} from './components/FilterBar'
import {PlaceBottomSheet} from './components/PlaceBottomSheet'
import {PlaceSheet} from './components/PlaceSheet'

export function DiscoverPage() {
  const dispatch = useAppDispatch()
  const {data: places = [], isLoading} = useListMyPlacesQuery()
  const {anchor, scope, categoryFilter, toggles, selectedKey} = useAppSelector((s) => s.discover)

  // Live location → anchor (ADR-027 pattern). Denied/unsupported → stays null (fit-all).
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => dispatch(setAnchor({lat: Math.round(pos.coords.latitude * 1e4) / 1e4, lng: Math.round(pos.coords.longitude * 1e4) / 1e4})),
      () => dispatch(setAnchor(null)),
      {timeout: 8000},
    )
  }, [dispatch])

  const views = useMemo(
    () => applyDiscover(places, {anchor, viewport: scope, category: categoryFilter, toggles, now: new Date()}),
    [places, anchor, scope, categoryFilter, toggles],
  )
  const selected = views.find((v) => v.key === selectedKey) ?? null

  return (
    <div className="discover-page">
      <div className="disc-topbar">
        <div className="disc-title-row"><span className="disc-title">ไปไหนดี</span></div>
        <FilterBar
          category={categoryFilter}
          toggles={toggles}
          onCategory={(c) => dispatch(setCategoryFilter(c))}
          onToggle={(k) => dispatch(toggleSignal(k))}
        />
      </div>

      <DiscoverMap
        places={views}
        anchor={anchor}
        selectedKey={selectedKey}
        onSelect={(k) => dispatch(setSelectedKey(k))}
        onScopeChange={(b) => dispatch(setScope(b))}
      />

      {selected ? (
        <PlaceSheet
          place={selected}
          onClose={() => dispatch(setSelectedKey(null))}
          onAddToTrip={() => { /* Task 9 */ }}
          onCreateTrip={() => { /* Task 9 */ }}
        />
      ) : (
        <PlaceBottomSheet places={views} onSelect={(k) => dispatch(setSelectedKey(k))} />
      )}

      {isLoading && <div className="disc-loading">กำลังโหลด…</div>}
    </div>
  )
}
```

- [ ] **Step 6: Create the barrel + CSS**

Create `frontend/src/pages/discover/index.ts`:

```ts
export {DiscoverPage} from './DiscoverPage'
```

Create `frontend/src/pages/discover/DiscoverPage.css` — port the layout/classes from the confirmed mock (`place-discovery` Screens card): `.discover-page` (full-viewport flex column), `.discover-map` (fills, `min-height`), `.disc-topbar` (absolute, floating), `.disc-filters`/`.disc-chip`/`.disc-chip.on` (teal pill toggles), `.disc-sheet`/`.disc-detail` (bottom sheets), `.disc-row`/`.disc-badge.*` (list rows + badges), `.disc-actions`/`.disc-abtn.primary` (teal button), reusing the `--teal`/`--ink`/`--muted` tokens from `trips-tokens.css`. Give `.discover-map` an explicit height (e.g. `flex: 1; min-height: 60vh`) — a zero-height map renders blank (the CLAUDE.md map gotcha). Do **not** let any floating overlay cover the whole map (the #36 black-map lesson).

- [ ] **Step 7: Register route + nav entry**

In `frontend/src/router.tsx`, add the import (~line 18, beside the trips import):

```tsx
import {DiscoverPage} from './pages/discover'
```

and the route inside the **auth-only** `AppLayout` children (beside `/trips`, ~line 70):

```tsx
          { path: '/trips/:tripId', element: <TripDetailPage /> },
          { path: '/discover', element: <DiscoverPage /> },
          { path: '/settings', element: <SettingsPage /> },
```

In `frontend/src/shared/components/NavBar.tsx`, add to `navItems` (after Trips, ~line 10) — plain-text label, no emoji:

```tsx
  { to: '/trips', label: '🧳 Trips' },
  { to: '/discover', label: 'ไปไหนดี' },
```

- [ ] **Step 8: Verify build + all units green**

Run: `cd frontend && npm run build && npm test`
Expected: build succeeds; all vitest suites pass (including the updated `homeOptions.test.ts`).

- [ ] **Step 9: Commit**

```bash
git add frontend/src/pages/discover frontend/src/router.tsx frontend/src/shared/components/NavBar.tsx frontend/src/pages/settings/homeOptions.ts frontend/src/pages/settings/homeOptions.test.ts
git commit -m "feat(discover): map-forward /discover page + nav + Home option (#42)"
```

---

### Task 9: Frontend — planning actions (add-to-trip picker + create-trip), wired into `PlaceSheet`

**Files:**
- Create: `frontend/src/pages/discover/components/AddToTripDialog.tsx`
- Modify: `frontend/src/pages/discover/DiscoverPage.tsx` (wire `onAddToTrip`/`onCreateTrip`)

**Interfaces:**
- Consumes: `useListTripsQuery`, `useAddTripPlaceMutation`, `useCreateTripMutation` (`shared/api/api`), `DiscoverPlaceView`.
- Produces: the add-to-trip flow (choose a trip → `addTripPlace`) and create-trip flow (`createTrip` → `addTripPlace` on the new trip).

> **Note:** Wiring over existing tested mutations; gate is `npm run build` + interactive verify (Task 10). `addTripPlace` accepts the `ResolvedPlaceDto`-shaped fields, which `DiscoverPlaceView` already carries (`googlePlaceId,name,lat,lng,address,category,priceLevel,photoUrl,openingHoursJson`); the mutation seeds the Place profile automatically (ADR-064), so enrichment carries over.

- [ ] **Step 1: Create the AddToTripDialog**

Create `frontend/src/pages/discover/components/AddToTripDialog.tsx`:

```tsx
import {useListTripsQuery, useAddTripPlaceMutation} from '../../../shared/api/api'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onDone: (tripId: string) => void
}

export function AddToTripDialog({place, onClose, onDone}: Props) {
  const {data: trips = []} = useListTripsQuery()
  const [addTripPlace, {isLoading}] = useAddTripPlaceMutation()

  const add = async (tripId: string) => {
    await addTripPlace({
      tripId,
      googlePlaceId: place.googlePlaceId,
      name: place.name,
      lat: place.lat,
      lng: place.lng,
      address: place.address,
      category: place.category,
      priceLevel: place.priceLevel,
      photoUrl: place.photoUrl,
      openingHoursJson: place.openingHoursJson,
    }).unwrap()
    onDone(tripId)
  }

  return (
    <div className="disc-modal" role="dialog" aria-label="เพิ่มเข้าทริป">
      <div className="disc-modal-card">
        <div className="disc-modal-head">
          <span>เพิ่ม “{place.name}” เข้าทริป</span>
          <button type="button" onClick={onClose} aria-label="ปิด">✕</button>
        </div>
        <ul className="disc-trip-list">
          {trips.length === 0 && <li className="disc-empty">ยังไม่มีทริป — ใช้ “สร้างทริปใหม่” แทน</li>}
          {trips.map((t) => (
            <li key={t.id}>
              <button type="button" className="disc-trip-item" disabled={isLoading} onClick={() => add(t.id)}>{t.name}</button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Wire the actions into `DiscoverPage`**

In `frontend/src/pages/discover/DiscoverPage.tsx`, add local state + a create-trip handler and pass real callbacks to `PlaceSheet` (replacing the Task-8 stubs). Add near the top of the component:

```tsx
import {useState} from 'react'
import {useNavigate} from 'react-router-dom'
import {useCreateTripMutation} from '../../shared/api/api'
import {AddToTripDialog} from './components/AddToTripDialog'
import type {DiscoverPlaceView} from './lib/discoverFilter'
```

Inside the component:

```tsx
  const navigate = useNavigate()
  const [addForPlace, setAddForPlace] = useState<DiscoverPlaceView | null>(null)
  const [createTrip] = useCreateTripMutation()

  const handleCreateTrip = async (place: DiscoverPlaceView) => {
    const trip = await createTrip({
      name: place.name,
      startDate: new Date().toISOString().slice(0, 10),
      dayCount: 1,
      defaultTravelMode: 'Drive',
    }).unwrap()
    navigate(`/trips/${trip.id}`)
    // Seed the new trip with this place so it opens ready to plan.
    // (addTripPlace via the dialog path is reused; here we jump to the trip.)
  }
```

Update the `PlaceSheet` usage:

```tsx
        <PlaceSheet
          place={selected}
          onClose={() => dispatch(setSelectedKey(null))}
          onAddToTrip={(p) => setAddForPlace(p)}
          onCreateTrip={handleCreateTrip}
        />
```

And render the dialog when `addForPlace` is set (after the sheets):

```tsx
      {addForPlace && (
        <AddToTripDialog
          place={addForPlace}
          onClose={() => setAddForPlace(null)}
          onDone={(tripId) => { setAddForPlace(null); navigate(`/trips/${tripId}`) }}
        />
      )}
```

- [ ] **Step 3: Verify build + units**

Run: `cd frontend && npm run build && npm test`
Expected: build succeeds; all vitest suites pass.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/discover/components/AddToTripDialog.tsx frontend/src/pages/discover/DiscoverPage.tsx
git commit -m "feat(discover): add-to-trip + create-trip actions from a discovered place (#42)"
```

---

### Task 10: Interactive verification (the mandatory pre-push gate)

**Files:** none (verification only — the SPA has no render/visual harness, and a map/overlay bug ships to prod on push otherwise; CLAUDE.md #36).

- [ ] **Step 1: Run the app in a seeded, authenticated session**

Run: `cd frontend && npm run dev` (and the backend per the repo's run instructions), sign in, and ensure the account has ≥1 Trip with several saved Places across ≥2 Trips (some with opening hours / season data, and at least one visited Stop).

- [ ] **Step 2: Verify the map-forward screen**

Navigate to `/discover`. Confirm, by observation:
- The map renders full-screen with category-coloured markers and the blue "you are here" pin; there is **no** overlay covering the whole map (no black screen).
- Markers cluster when zoomed out (places across cities), split when zoomed in.
- The bottom sheet lists nearby places ranked by distance with correct badges (distance / open-now / เดือนนี้ดี / trip name).

- [ ] **Step 3: Verify the toggles + scope + actions**

- Each of the four toggles (เปิดตอนนี้ / เดือนนี้ / ช่วงเวลา / ซ่อนที่ไปแล้ว) changes the list/markers live; a visited place hides when "ซ่อนที่ไปแล้ว" is on.
- Panning/zooming the map to another city updates the visible markers + list (viewport = scope).
- Tapping a marker (and a list row) opens the place sheet; **นำทาง** opens Google Maps in a new tab; **เปิดทริป** routes to the trip (and is disabled/uses the chooser when the place is in >1 trip); **เพิ่มเข้าทริป** adds it to a chosen trip; **สร้างทริปใหม่** creates a trip and lands on it.

- [ ] **Step 4: Verify nav + Home + GPS-denied**

- The `ไปไหนดี` nav entry appears (desktop + mobile drawer) and routes to `/discover`; a family-less account can open it.
- `/settings` lists `ไปไหนดี` as a Home-page option; selecting it makes `/` land on `/discover`.
- Deny geolocation (or block it) → the map fits all saved Places instead of centring on GPS; no crash.

- [ ] **Step 5: Push**

Only after every check above passes:

```bash
git push main HEAD:main
```

(The final feature commit — Task 9 — should carry `(closes #42)` if you want GitHub to auto-close the issue on merge to `main`; otherwise close it by hand after verifying on prod.)

---

## Self-Review

**1. Spec coverage:**
- Source = own saved Places across Trips (ADR-094) → Task 1 handler. ✓
- Anchor = live GPS + switchable scope (ADR-095) → Task 8 geolocation + Task 7 `onScopeChange` (viewport=scope). ✓
- Four toggleable free signals (ADR-096) → Task 5 `applyDiscover` (open-now via reused `isOpenAt`, season via reused `monthStatus`, best-time, hide-visited) + Task 8 `FilterBar`. ✓
- Map-forward, event-driven (ADR-097) → Task 7 `DiscoverMap` + Task 8 assembly; clustering via new dep. ✓
- Actions: detail/navigate/open-trip (Task 8), add-to-trip/create-trip (Task 9); **mark-Visited deferred** (spec Scope-Out). ✓
- Top nav + selectable Home (ADR-099) → Task 8 (router, navItems, HOME_OPTIONS + test). ✓
- Read model `GET /api/places`, dedup, no new entity/migration (ADR-100) → Tasks 1–2. ✓

**2. Placeholder scan:** No "TBD/handle appropriately/etc." The only intentional token is `#42` (defined in Global Constraints — the tracking issue opened before Task 1). The Task-8 `PlaceSheet` `onAddToTrip`/`onCreateTrip` comments (`/* Task 9 */`) are deliberate no-ops replaced in Task 9, not vague placeholders.

**3. Type consistency:** `DiscoverPlaceDto` field order/names match between the C# record (Task 1) and the TS interface (Task 3). `DiscoverToggles`/`ViewportBounds` are defined in Task 5 and imported by Tasks 6 & 8. `applyDiscover`/`DiscoverPlaceView` (Task 5) are consumed by Tasks 7–9 with matching shapes. `haversineKm` (Task 4) name matches its use in Task 5. Slice actions (`setAnchor`/`setScope`/`setCategoryFilter`/`toggleSignal`/`setSelectedKey`) match between Tasks 6 & 8. `addTripPlace` payload fields (Task 9) match the `ResolvedPlaceDto` subset the mutation accepts.

**Flagged (carried from the spec, accepted at spec approval):** in-memory dedup/grouping (Task 1) over pure-SQL; `@googlemaps/markerclusterer` new dep (Task 7); mark-Visited deferred; a tracking issue must be opened before Task 1.
