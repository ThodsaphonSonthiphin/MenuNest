# Discover place delete — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "ลบจุดนี้" control to the Discover (ไปไหนดี) place sheet that removes the place from one named Trip, taking its scheduled Stops with it.

**Architecture:** The Discover pin stays a read-time group over N `TripPlace` rows, so the read model starts carrying the per-Trip row id and a scheduled-Stop count (ADR-166/168). The delete reuses the existing `DELETE /api/trips/{tripId}/places/{placeId}`, which gains an opt-in `cascade` switch so the trips-page caller and the MCP tool keep today's refusal behaviour (ADR-167). No entity, no `DbSet<>`, no EF migration.

**Tech Stack:** .NET 9 / EF Core / Mediator / xUnit + Moq + FluentAssertions (backend) · React + Redux Toolkit Query + vitest (frontend)

**Spec:** `docs/superpowers/specs/2026-08-15-discover-place-delete-design.md`

## Global Constraints

- **Open the GitHub issue before Task 1** — `gh issue create --repo ThodsaphonSonthiphin/MenuNest`. Every commit subject below ends with `(#96)`; substitute the real number. The final commit of Task 4 may use `(closes #96)`.
- **The git remote is named `main`, not `origin`.** Push with `git push main HEAD:main`.
- **`frontend/.husky/pre-commit` runs the FULL suite on every commit** — backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build`, ~40s+. Every commit must leave the whole suite green. Never `--no-verify`.
- **Stage explicit paths only.** Never `git add -A` / `git add .` — `daily-state.md` and `AGENTS.md` are dirty working files that must not enter a feature commit.
- **No emoji anywhere in the UI.** Icons are inline SVG components; `@syncfusion/react-icons` is not installed.
- **Thai copy is exact.** Use the strings in this plan verbatim, including the spacing around `·` and `—`.
- **Do not change** `ListMyPlacesHandler:43`'s grouping key, the `GroupBy(TripId).First()` dedupe at `:57-60`, or anything about ADR-155. Out of scope.
- **Backend tests: Moq, not NSubstitute.** `Substitute.For<>` will not compile.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs` | `PlaceTripRefDto` gains `TripPlaceId` + `ScheduledStopCount` | 1 |
| `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs` | one `Stops` read now feeds both the visited badge and the count | 1 |
| `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs` | proves the two new fields | 1 |
| `backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceCommand.cs` | `bool Cascade = false` | 2 |
| `backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceHandler.cs` | refuse, or cascade + resequence | 2 |
| `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` | `[FromQuery] bool cascade` | 2 |
| `backend/tests/MenuNest.Application.UnitTests/Trips/DeleteTripPlaceCascadeTests.cs` | **new**, relational (SQLite) — FK cascade is invisible on InMemory | 2 |
| `frontend/src/shared/api/api.ts` | DTO type + `cascade` on the mutation | 3 |
| `frontend/src/pages/discover/lib/deleteFlow.ts` | **new** — the pure state/copy rules, the only unit-testable part | 3 |
| `frontend/src/pages/discover/lib/deleteFlow.test.ts` | **new** | 3 |
| `frontend/src/pages/discover/components/PlaceSheet.tsx` | the button, the chooser, the inline confirm | 4 |
| `frontend/src/pages/discover/DiscoverPage.tsx` | the "ลบแล้ว" toast | 4 |
| `frontend/src/pages/discover/DiscoverPage.css` | danger button + chooser + confirm styles | 4 |

Task 1 must land before Task 3 (the frontend type mirrors the DTO). Task 2 is independent of Task 1. Task 4 consumes Tasks 2 and 3.

---

### Task 1: The read model carries the per-Trip row id and scheduled count

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs:7`
- Modify: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs:35-39,57-60`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PlaceTripRefDto(Guid TripId, string TripName, Guid TripPlaceId, int ScheduledStopCount)`. Task 3 mirrors it in TypeScript as `{tripId, tripName, tripPlaceId, scheduledStopCount}`.

`ListMyPlacesHandler.cs:57` is the **only** construction site of `PlaceTripRefDto` in the repo (verified by grep across `backend/`), so widening the record breaks nothing else.

- [ ] **Step 1: Write the two failing tests**

Append to `ListMyPlacesHandlerTests.cs`, inside the class (before `Dispose`):

```csharp
    [Fact]
    public async Task Each_trip_ref_carries_its_own_trip_place_id()
    {
        var t1 = Trip.Create(_user.Id, "Kanchanaburi", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        var t2 = Trip.Create(_user.Id, "Japan", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(t1, t2);
        var p1 = TripPlace.Create(t1.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        var p2 = TripPlace.Create(t2.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        _db.TripPlaces.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Trips.Single(x => x.TripId == t1.Id).TripPlaceId.Should().Be(p1.Id);
        result[0].Trips.Single(x => x.TripId == t2.Id).TripPlaceId.Should().Be(p2.Id);
    }

    [Fact]
    public async Task Scheduled_stop_count_is_per_trip_and_zero_when_unscheduled()
    {
        var t1 = Trip.Create(_user.Id, "Kanchanaburi", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        var t2 = Trip.Create(_user.Id, "Japan", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(t1, t2);
        var p1 = TripPlace.Create(t1.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        var p2 = TripPlace.Create(t2.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        _db.TripPlaces.AddRange(p1, p2);
        var d1 = ItineraryDay.Create(t1.Id, new DateOnly(2026, 11, 1));
        var d2 = ItineraryDay.Create(t1.Id, new DateOnly(2026, 11, 2));
        _db.ItineraryDays.AddRange(d1, d2);
        _db.Stops.Add(Stop.Create(d1.Id, p1.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d2.Id, p1.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Trips.Single(x => x.TripId == t1.Id).ScheduledStopCount.Should().Be(2);
        result[0].Trips.Single(x => x.TripId == t2.Id).ScheduledStopCount.Should().Be(0);
        result[0].Visited.Should().BeFalse();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListMyPlacesHandlerTests"
```

Expected: **build failure** — `'PlaceTripRefDto' does not contain a definition for 'TripPlaceId'` (CS1061). A compile error is the correct red here; the type does not exist yet.

- [ ] **Step 3: Widen the DTO**

Replace `PlaceDtos.cs:7`:

```csharp
/// <summary>A Trip that contains a discovered Place (for the "อยู่ในทริป: …" line).</summary>
/// <param name="TripPlaceId">
/// ADR-166: the row id inside THIS Trip, which is what makes a delete addressable —
/// a Discover pin is a read-time group over N rows and carries no id of its own.
/// </param>
/// <param name="ScheduledStopCount">
/// ADR-168: how many Stops in this Trip reference that row, so the delete confirmation can
/// name the number instead of guessing. Free — the Stops table is already read for Visited.
/// </param>
public sealed record PlaceTripRefDto(Guid TripId, string TripName, Guid TripPlaceId, int ScheduledStopCount);
```

- [ ] **Step 4: Make one Stops read feed both facts**

Replace `ListMyPlacesHandler.cs:35-39`:

```csharp
        // One read of the Stops table serves both the "มาแล้ว" badge and ADR-168's count.
        // The IsVisited predicate moves out of SQL deliberately: same table, same index, same
        // round trip, and the count comes back for free rather than costing a second query.
        var stopRows = await _db.Stops
            .Where(s => placeIds.Contains(s.TripPlaceId))
            .Select(s => new { s.TripPlaceId, s.IsVisited })
            .ToListAsync(ct);

        var visitedPlaceIds = stopRows.Where(s => s.IsVisited).Select(s => s.TripPlaceId).ToHashSet();
        var stopCountByPlaceId = stopRows.GroupBy(s => s.TripPlaceId).ToDictionary(g => g.Key, g => g.Count());
```

Replace `ListMyPlacesHandler.cs:57-60`:

```csharp
            var trips = g.Select(r => new PlaceTripRefDto(
                              r.TripId,
                              r.TripName,
                              r.Place.Id,
                              stopCountByPlaceId.TryGetValue(r.Place.Id, out var n) ? n : 0))
                         .GroupBy(x => x.TripId)
                         .Select(x => x.First())
                         .ToList();
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListMyPlacesHandlerTests"
```

Expected: PASS, **all** tests in the class — `Rolls_up_visited_when_any_stop_for_the_place_is_visited` proves the rewrite did not break the badge.

- [ ] **Step 6: Run the whole backend suite**

```bash
cd backend && dotnet test
```

Expected: 0 failures. Iterate to zero — a small non-zero error count is never the full list.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs \
        backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs
git commit -m "feat(places): Discover's read model carries the per-Trip row id and scheduled count (#96)"
```

---

### Task 2: The delete can take its scheduled Stops with it

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceCommand.cs:3`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceHandler.cs:14-32`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:83-85`
- Create: `backend/tests/MenuNest.Application.UnitTests/Trips/DeleteTripPlaceCascadeTests.cs`
- Do **not** modify: `backend/tests/MenuNest.Application.UnitTests/Trips/DeleteTripPlaceHandlerTests.cs` — it must pass untouched, which is the proof that existing callers are unaffected.

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `DeleteTripPlaceCommand(Guid TripId, Guid PlaceId, bool Cascade = false)` and the route `DELETE /api/trips/{id}/places/{placeId}?cascade=true`. Task 3 calls that route.

The new tests use SQLite, not the InMemory `HandlerTestFixture` the existing tests use: `StopChecklistEntry → Stop` is a database-level `DeleteBehavior.Cascade` (`StopChecklistEntryConfiguration.cs:20`) and the InMemory provider ignores it.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/DeleteTripPlaceCascadeTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-167's cascade, on a RELATIONAL context: StopChecklistEntry follows its Stop through a
/// database-level cascade the InMemory provider silently ignores, and Stop → TripPlace is
/// NoAction, so delete ORDER inside the one SaveChanges is load-bearing.
/// </summary>
public sealed class DeleteTripPlaceCascadeTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public DeleteTripPlaceCascadeTests()
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

    private DeleteTripPlaceHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new DeleteTripPlaceHandler(_db, users.Object);
    }

    private Trip NewTrip(string name = "Trip")
    {
        var t = Trip.Create(_user.Id, name, new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        _db.Trips.Add(t);
        return t;
    }

    [Fact]
    public async Task Without_cascade_a_scheduled_place_is_still_refused()
    {
        var t = NewTrip();
        var place = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(place);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await FluentActions
            .Awaiting(() => NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, place.Id), CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>().WithMessage("*ถูกจัดลงตาราง*");

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Cascade_removes_the_stop_and_closes_the_gap_it_left()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        var other1 = TripPlace.Create(t.Id, "Cafe", 12.9, 99.4, PlaceCategory.Eat);
        var other2 = TripPlace.Create(t.Id, "Museum", 13.0, 99.5, PlaceCategory.See);
        _db.TripPlaces.AddRange(target, other1, other2);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, other1.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(day.Id, target.Id, 1, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(day.Id, other2.Id, 2, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == target.Id).Should().BeFalse();
        _db.Stops.Any(s => s.TripPlaceId == target.Id).Should().BeFalse();
        _db.Stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence)
           .Select(s => s.Sequence).Should().BeEquivalentTo(new[] { 0, 1 }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Cascade_handles_a_place_scheduled_on_two_days_and_resequences_both()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        var other = TripPlace.Create(t.Id, "Cafe", 12.9, 99.4, PlaceCategory.Eat);
        _db.TripPlaces.AddRange(target, other);
        var d1 = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        var d2 = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 2));
        _db.ItineraryDays.AddRange(d1, d2);
        _db.Stops.Add(Stop.Create(d1.Id, target.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d1.Id, other.Id, 1, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d2.Id, target.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d2.Id, other.Id, 1, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.Stops.Count().Should().Be(2);
        _db.Stops.Single(s => s.ItineraryDayId == d1.Id).Sequence.Should().Be(0);
        _db.Stops.Single(s => s.ItineraryDayId == d2.Id).Sequence.Should().Be(0);
    }

    [Fact]
    public async Task Cascade_takes_the_stops_checklist_entries_with_it()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(target);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var stop = Stop.Create(day.Id, target.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(stop);
        var item = ChecklistItem.Create(_user.Id, "พาสปอร์ต");
        _db.ChecklistItems.Add(item);
        _db.StopChecklistEntries.Add(StopChecklistEntry.Create(stop.Id, item.Id));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.StopChecklistEntries.Any(e => e.StopId == stop.Id).Should().BeFalse();
        _db.ChecklistItems.Any(i => i.Id == item.Id).Should().BeTrue(); // the library item survives
    }

    [Fact]
    public async Task Cascade_on_an_unscheduled_place_just_deletes_the_row()
    {
        var t = NewTrip();
        var place = TripPlace.Create(t.Id, "Museum", 13.0, 99.5, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, place.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Cascade_does_not_reach_another_users_trip()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        var theirs = Trip.Create(other.Id, "Theirs", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(theirs);
        var place = TripPlace.Create(theirs.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        await FluentActions
            .Awaiting(() => NewHandler().Handle(new DeleteTripPlaceCommand(theirs.Id, place.Id, Cascade: true), CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Cascade_leaves_another_trips_copy_of_the_same_place_alone()
    {
        var t1 = NewTrip("Kanchanaburi");
        var t2 = NewTrip("Japan");
        var p1 = TripPlace.Create(t1.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        var p2 = TripPlace.Create(t2.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        _db.TripPlaces.AddRange(p1, p2);
        var day = ItineraryDay.Create(t2.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, p2.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t1.Id, p1.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == p2.Id).Should().BeTrue();
        _db.Stops.Any(s => s.TripPlaceId == p2.Id).Should().BeTrue();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~DeleteTripPlaceCascadeTests"
```

Expected: **build failure** — `DeleteTripPlaceCommand` does not take three arguments (CS1729). The `Cascade:` named argument does not exist yet.

- [ ] **Step 3: Add the opt-in switch to the command**

Replace `DeleteTripPlaceCommand.cs:3`:

```csharp
// ADR-167: Cascade defaults to FALSE so every existing caller — TripsController, the MCP
// TripTools delete, and the trips page's unconfirmed "เอาออกจากทริปนี้" button — keeps today's
// refusal. Only a caller that has shown a confirmation opts in.
public sealed record DeleteTripPlaceCommand(Guid TripId, Guid PlaceId, bool Cascade = false) : ICommand<Unit>;
```

- [ ] **Step 4: Implement the cascade branch**

Replace the body of `Handle` in `DeleteTripPlaceHandler.cs:14-32`:

```csharp
    public async ValueTask<Unit> Handle(DeleteTripPlaceCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        var scheduled = await _db.Stops
            .Where(s => s.TripPlaceId == c.PlaceId
                     && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == c.TripId))
            .ToListAsync(ct);

        if (scheduled.Count > 0)
        {
            if (!c.Cascade)
                throw new DomainException("ลบไม่ได้ — สถานที่นี้ถูกจัดลงตารางแล้ว ลบจุดในแผนก่อน");

            var removedIds = scheduled.Select(s => s.Id).ToList();
            _db.Stops.RemoveRange(scheduled);

            // Close the gaps, one day at a time — a Place can be scheduled across several days,
            // so this is RemoveStopHandler:27-33's invariant applied per affected day. The rows
            // are only marked Deleted until SaveChanges, so they must be filtered out by id.
            foreach (var dayId in scheduled.Select(s => s.ItineraryDayId).Distinct())
            {
                var remaining = await _db.Stops
                    .Where(s => s.ItineraryDayId == dayId && !removedIds.Contains(s.Id))
                    .OrderBy(s => s.Sequence)
                    .ToListAsync(ct);
                for (var i = 0; i < remaining.Count; i++) remaining[i].SetSequence(i);
            }
        }

        // Stop → TripPlace is DeleteBehavior.NoAction (StopConfiguration.cs:23) — there is no
        // database cascade, so the Stops above and this row must land in ONE SaveChanges, with
        // EF ordering the dependents first.
        _db.TripPlaces.Remove(place);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
```

- [ ] **Step 5: Run the new tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~DeleteTripPlaceCascadeTests"
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Run the untouched tests that guard the existing callers**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~DeleteTripPlaceHandlerTests"
```

Expected: PASS, 2 tests, **with no edits to that file**. If either needed changing, the default is wrong — fix the command, not the test.

- [ ] **Step 7: Expose the switch on the route**

Replace `TripsController.cs:83-85`:

```csharp
    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<IActionResult> DeletePlace(Guid id, Guid placeId, [FromQuery] bool cascade, CancellationToken ct)
    { await _mediator.Send(new DeleteTripPlaceCommand(id, placeId, cascade), ct); return NoContent(); }
```

An absent `cascade` binds to `false`. Leave `TripTools.cs:137` (MCP) alone — it keeps sending the two-argument command on purpose: the cascade is guarded by a confirmation, and MCP has none.

- [ ] **Step 8: Run the whole backend suite**

```bash
cd backend && dotnet test
```

Expected: 0 failures.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceCommand.cs \
        backend/src/MenuNest.Application/UseCases/Trips/DeleteTripPlace/DeleteTripPlaceHandler.cs \
        backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/DeleteTripPlaceCascadeTests.cs
git commit -m "feat(trips): a place delete can take its scheduled stops with it, opt-in per call (#96)"
```

---

### Task 3: Frontend contract and the pure delete-flow rules

**Files:**
- Modify: `frontend/src/shared/api/api.ts:563` and `:1431-1434`
- Create: `frontend/src/pages/discover/lib/deleteFlow.ts`
- Test: `frontend/src/pages/discover/lib/deleteFlow.test.ts`

**Interfaces:**
- Consumes: Task 1's `PlaceTripRefDto` shape; Task 2's `?cascade=true`.
- Produces: `DeleteFlow` (`{stage:'idle'} | {stage:'choosing'} | {stage:'confirming', trip}`), `startDelete(trips): DeleteFlow`, `confirmCopy(placeName, trip): {title, warning, keep}`. Task 4 renders exactly these.

The SPA's vitest runs in `environment: 'node'` with no jsdom and no React Testing Library, so a component test is impossible. Putting the branch rules in `lib/` is what makes them testable at all — the same move `discoverFilter.ts` and `originPassthrough.ts` already made in this folder.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/discover/lib/deleteFlow.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import type {PlaceTripRefDto} from '../../../shared/api/api'
import {startDelete, confirmCopy} from './deleteFlow'

const trip = (name: string, scheduledStopCount = 0): PlaceTripRefDto => ({
  tripId: `id-${name}`,
  tripName: name,
  tripPlaceId: `tp-${name}`,
  scheduledStopCount,
})

describe('startDelete', () => {
  it('skips the chooser when the place is in exactly one trip', () => {
    const t = trip('เที่ยวกาญจนบุรี')
    expect(startDelete([t])).toEqual({stage: 'confirming', trip: t})
  })

  it('asks which trip when the place is in more than one', () => {
    expect(startDelete([trip('a'), trip('b')])).toEqual({stage: 'choosing'})
  })

  it('stays idle when there is no trip to delete from', () => {
    expect(startDelete([])).toEqual({stage: 'idle'})
  })
})

describe('confirmCopy', () => {
  it('names how many stops the delete will take', () => {
    const c = confirmCopy('หอพักระยอง ฟอเรสท์', trip('เที่ยวกาญจนบุรี', 2))
    expect(c.title).toBe('เอา "หอพักระยอง ฟอเรสท์" ออกจาก เที่ยวกาญจนบุรี?')
    expect(c.warning).toBe('จุดนี้อยู่ในแผนของทริปนี้ 2 จุด — จะถูกลบไปด้วย')
  })

  it('drops the warning entirely when nothing is scheduled', () => {
    expect(confirmCopy('x', trip('t', 0)).warning).toBeNull()
  })

  it('always says the place profile survives', () => {
    expect(confirmCopy('x', trip('t', 3)).keep).toBe('โน้ต · ลิงก์รีวิว · ช่วงเวลาที่ดี ยังอยู่ในคลังของคุณ')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && npx vitest run src/pages/discover/lib/deleteFlow.test.ts
```

Expected: FAIL — `Failed to resolve import "./deleteFlow"`.

- [ ] **Step 3: Widen the API types**

Replace `api.ts:563`:

```ts
export interface PlaceTripRefDto { tripId: string; tripName: string; tripPlaceId: string; scheduledStopCount: number }
```

Replace `api.ts:1431-1434`:

```ts
        deleteTripPlace: build.mutation<void, {tripId: string; placeId: string; cascade?: boolean}>({
            query: ({tripId, placeId, cascade}) => ({
                url: `/api/trips/${tripId}/places/${placeId}${cascade ? '?cascade=true' : ''}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}, 'MyPlaces'],
        }),
```

`invalidatesTags` is unchanged and already correct: `'MyPlaces'` is the tag `listMyPlaces` provides (`api.ts:1421`), so Discover refetches itself after a delete.

- [ ] **Step 4: Write the flow module**

Create `frontend/src/pages/discover/lib/deleteFlow.ts`:

```ts
import type {PlaceTripRefDto} from '../../../shared/api/api'

/**
 * ADR-166/168. A Discover pin is a read-time group over N TripPlace rows, so a delete has to
 * name a Trip. With exactly one Trip there is nothing to name, so the chooser is skipped.
 */
export type DeleteFlow =
  | {stage: 'idle'}
  | {stage: 'choosing'}
  | {stage: 'confirming'; trip: PlaceTripRefDto}

export function startDelete(trips: readonly PlaceTripRefDto[]): DeleteFlow {
  if (trips.length === 0) return {stage: 'idle'} // ADR-155 says unreachable; not a crash if it happens
  if (trips.length === 1) return {stage: 'confirming', trip: trips[0]}
  return {stage: 'choosing'}
}

export interface ConfirmCopy {
  title: string
  /** null when nothing is scheduled — ADR-168 hides the whole warning row rather than rewording it. */
  warning: string | null
  keep: string
}

export function confirmCopy(placeName: string, trip: PlaceTripRefDto): ConfirmCopy {
  return {
    title: `เอา "${placeName}" ออกจาก ${trip.tripName}?`,
    warning:
      trip.scheduledStopCount > 0
        ? `จุดนี้อยู่ในแผนของทริปนี้ ${trip.scheduledStopCount} จุด — จะถูกลบไปด้วย`
        : null,
    keep: 'โน้ต · ลิงก์รีวิว · ช่วงเวลาที่ดี ยังอยู่ในคลังของคุณ',
  }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd frontend && npx vitest run src/pages/discover/lib/deleteFlow.test.ts
```

Expected: PASS, 6 tests.

- [ ] **Step 6: Typecheck and run the whole frontend suite**

```bash
cd frontend && npx tsc --noEmit && npx vitest run
```

Expected: no type errors, 0 failing tests.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/discover/lib/deleteFlow.ts \
        frontend/src/pages/discover/lib/deleteFlow.test.ts
git commit -m "feat(discover): delete-flow rules and the cascade-aware delete mutation (#96)"
```

---

### Task 4: The control on the sheet

**Files:**
- Modify: `frontend/src/pages/discover/components/PlaceSheet.tsx`
- Modify: `frontend/src/pages/discover/DiscoverPage.tsx`
- Modify: `frontend/src/pages/discover/DiscoverPage.css`

**Interfaces:**
- Consumes: `startDelete`, `confirmCopy`, `DeleteFlow` from Task 3; `useDeleteTripPlaceMutation` with `cascade`.
- Produces: the finished feature. Nothing depends on it.

Mockup: *MenuNest design system* → **Screens** → `discover-place-delete`. Build against that card, not from memory.

- [ ] **Step 1: Add the delete flow to `PlaceSheet`**

In `PlaceSheet.tsx`, extend the imports and state:

```tsx
import {useDeleteTripPlaceMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/api/errors'
import {startDelete, confirmCopy, type DeleteFlow} from '../lib/deleteFlow'
import {CategoryIcon, CheckIcon, CloseIcon, NavArrowIcon, OpenIcon, PlusIcon, SunIcon, TripIcon, TrashIcon} from './DiscoverIcons'
```

Add inside the component, after the existing `choosing` state:

```tsx
  const [flow, setFlow] = useState<DeleteFlow>({stage: 'idle'})
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [deletePlace, {isLoading: deleting}] = useDeleteTripPlaceMutation()

  const runDelete = async (trip: {tripId: string; tripPlaceId: string}) => {
    setDeleteError(null)
    try {
      await deletePlace({tripId: trip.tripId, placeId: trip.tripPlaceId, cascade: true}).unwrap()
      setFlow({stage: 'idle'})
      onDeleted()
    } catch (err) {
      setDeleteError(getErrorMessage(err))
    }
  }
```

Add `onDeleted: () => void` to `Props`.

`getErrorMessage` lives at `frontend/src/shared/utils/getErrorMessage.ts`; `PlaceEditorDialog.tsx:10` imports it with exactly the relative path above, and `PlaceSheet.tsx` sits at the same depth.

- [ ] **Step 2: Render the button, the chooser and the confirm**

Inside `.disc-actions`, after the existing "สร้างทริปใหม่" row:

```tsx
        {flow.stage === 'idle' && place.trips.length > 0 && (
          <button
            type="button"
            className="disc-abtn danger"
            onClick={() => setFlow(startDelete(place.trips))}
          >
            <TrashIcon />ลบจุดนี้
          </button>
        )}
        {flow.stage === 'choosing' && (
          <div className="disc-del-choose">
            <div className="disc-del-lab">เอาออกจากทริปไหน?</div>
            {place.trips.map((t) => (
              <button
                key={t.tripId}
                type="button"
                className="disc-abtn ghost"
                onClick={() => setFlow({stage: 'confirming', trip: t})}
              >
                <TripIcon />{t.tripName}
              </button>
            ))}
            <button type="button" className="disc-abtn ghost" onClick={() => setFlow({stage: 'idle'})}>
              ยกเลิก
            </button>
          </div>
        )}
        {flow.stage === 'confirming' && (() => {
          const copy = confirmCopy(place.name, flow.trip)
          return (
            <div className="disc-confirm">
              <div className="disc-cf-title">{copy.title}</div>
              {copy.warning && (
                <div className="disc-cf-line warn"><CalendarIcon />{copy.warning}</div>
              )}
              <div className="disc-cf-line keep"><KeepIcon />{copy.keep}</div>
              {deleteError && <p className="trips-field-error">{deleteError}</p>}
              <div className="disc-cf-row">
                <button type="button" className="disc-abtn ghost" disabled={deleting} onClick={() => setFlow({stage: 'idle'})}>
                  ยกเลิก
                </button>
                <button type="button" className="disc-abtn danger-solid" disabled={deleting} onClick={() => runDelete(flow.trip)}>
                  ลบ
                </button>
              </div>
            </div>
          )
        })()}
```

- [ ] **Step 3: Add the three icons**

Append to `frontend/src/pages/discover/components/DiscoverIcons.tsx`. `IconProps` and the shared `STROKE` constant are already defined at the top of that file (`:13-23`) — reuse them, do not redeclare:

```tsx
/** Path data matches PlaceEditorDialog.tsx:138, so both delete controls show one glyph. */
export function TrashIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" />
    </svg>
  )
}

export function CalendarIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M3 10h18M8 3v4M16 3v4" />
    </svg>
  )
}

export function KeepIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <path d="M5 4h11l3 3v13H5z" />
      <path d="M9 4v5h6" />
    </svg>
  )
}
```

No emoji, inline SVG only.

- [ ] **Step 4: Wire the toast on `DiscoverPage`**

In `DiscoverPage.tsx`, add the state and pass the callback:

```tsx
  const [deletedNote, setDeletedNote] = useState(false)
```

Pass `onDeleted={() => setDeletedNote(true)}` to `<PlaceSheet …>` at `:241-247`, and render, as a sibling inside the same `.disc-dock` block:

```tsx
        {deletedNote && <div className="disc-armed-toast" role="status">ลบแล้ว</div>}
```

Clear it on a timer so it does not stick:

```tsx
  useEffect(() => {
    if (!deletedNote) return
    const id = window.setTimeout(() => setDeletedNote(false), 2500)
    return () => window.clearTimeout(id)
  }, [deletedNote])
```

**Nothing closes the sheet by hand.** `DiscoverPage.tsx:69-73` derives `selected` from `places.find((pl) => pl.key === selectedKey)`, so when the last Trip's row is deleted the group leaves `places` on refetch, `selected` becomes `null`, and `:240-250` swaps back to `PlaceBottomSheet` on its own. When other Trips remain, the same derivation re-renders the sheet with one chip fewer.

- [ ] **Step 5: Style it**

In `DiscoverPage.css`, immediately after the existing `.disc-abtn` rules, add:

```css
/* Delete (ADR-166/167/168). Colours are the mockup card's --bad tokens, written as
   literals here because DiscoverPage.css defines no custom properties of its own. */
.disc-abtn.danger {
  background: #fff;
  border-color: #f6cdc9;
  color: #b42318;
}
.disc-abtn.danger-solid {
  background: #b42318;
  color: #fff;
  border-color: transparent;
  box-shadow: 0 6px 16px rgba(180, 35, 24, 0.32);
}

.disc-del-choose {
  border: 1.5px dashed #f6cdc9;
  background: #fdeceb;
  border-radius: 14px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.disc-del-lab {
  font-size: 11.5px;
  font-weight: 800;
  color: #b42318;
  padding: 1px 2px 2px;
}
.disc-del-choose .disc-abtn {
  padding: 10px;
  font-size: 13px;
}

.disc-confirm {
  border: 1.5px solid #f6cdc9;
  background: #fff;
  border-radius: 14px;
  padding: 12px;
  box-shadow: 0 8px 22px rgba(180, 35, 24, 0.1);
  display: flex;
  flex-direction: column;
  gap: 9px;
}
.disc-cf-title {
  font-size: 13.5px;
  font-weight: 800;
  color: #0f172a;
  line-height: 1.35;
}
.disc-cf-line {
  display: flex;
  gap: 7px;
  align-items: flex-start;
  font-size: 11.5px;
  line-height: 1.5;
  font-weight: 600;
  border-radius: 10px;
  padding: 8px 9px;
}
.disc-cf-line svg {
  width: 14px;
  height: 14px;
  flex: none;
  margin-top: 1px;
}
.disc-cf-line.warn {
  background: #fff4e0;
  color: #7a5310;
}
.disc-cf-line.keep {
  background: #f8fafc;
  border: 1px solid #eef2f6;
  color: #475569;
}
.disc-cf-row {
  display: flex;
  gap: 9px;
  margin-top: 1px;
}
.disc-cf-row .disc-abtn {
  flex: 1;
  padding: 11px;
  font-size: 13px;
}
```

Check these against the mockup card's `<style>` block once rendered — the card is the source of truth for spacing and colour.

- [ ] **Step 6: Typecheck, build, and run the suite**

```bash
cd frontend && npx tsc --noEmit && npm run build && npx vitest run
```

Expected: clean. **None of these gates can see this feature render** — they pass on a sheet whose confirm never appears. Step 7 is the real check.

- [ ] **Step 7: Verify it interactively — mandatory before commit**

Run the app, sign in, open `/discover`, and confirm all four:

1. a **scheduled** place — the confirm's count matches the Stops that actually vanish from that trip's itinerary, and the surviving stops renumber with no gap;
2. an **unscheduled** place — no yellow warning row at all;
3. a place in **two** Trips — after deleting one, the sheet stays open with one chip gone;
4. the **last** Trip — the pin leaves the map, the sheet closes itself, "ลบแล้ว" appears.

Then open the mockup card and diff the rendered sheet against it — spacing, colour, button order. The review gates do not do this, and a mockup-backed UI task has shipped visibly wrong through every gate before (#46).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/discover/components/PlaceSheet.tsx \
        frontend/src/pages/discover/components/DiscoverIcons.tsx \
        frontend/src/pages/discover/DiscoverPage.tsx \
        frontend/src/pages/discover/DiscoverPage.css
git commit -m "feat(discover): delete a place from a chosen trip, stops and all (closes #96)"
```

---

## Docs to commit alongside

`docs/adr/166-*.md`, `docs/adr/167-*.md`, `docs/adr/168-*.md`, the spec at `docs/superpowers/specs/2026-08-15-discover-place-delete-design.md`, this plan, and the evidence file at `docs/decision-map/discover-place-delete/evidence/current-model-er.md` are all currently untracked. SDD implementers stage code and tests only, so commit the docs explicitly or they orphan.

```bash
git add docs/adr/166-*.md docs/adr/167-*.md docs/adr/168-*.md \
        docs/superpowers/specs/2026-08-15-discover-place-delete-design.md \
        docs/superpowers/plans/2026-08-15-discover-place-delete.md \
        docs/decision-map/discover-place-delete/
git commit -m "docs(discover): ADR-166/167/168 + spec and plan for the Discover place delete (#96)"
```
