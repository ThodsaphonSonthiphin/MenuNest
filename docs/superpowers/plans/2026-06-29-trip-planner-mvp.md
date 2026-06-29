# Trip Planner (MVP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a user-scoped Trip Planner module — collect places from Google Maps into a per-trip pool, then build a time-aware, map-forward itinerary that auto-computes arrival/leave times and flags timing. Expenses are out of scope (Phase 2).

**Architecture:** Mirrors the user-scoped Health module on the backend (CQRS via `Mediator`, `IApplicationDbContext`, `IUserProvisioner`, `IEntityTypeConfiguration`, attribute-routed controllers) and the Budget feature on the frontend (`pages/trips/`, one RTK Query instance, UI-only slice). Google Maps Platform calls (Places New, Routes) are server-side only (CORS CF1); the browser only loads the Maps JS API via `@vis.gl/react-google-maps`. The schedule cascade runs client-side in `useSchedule`, consuming per-leg travel times the server provides.

**Tech Stack:** .NET 10 (Domain/Application/Infrastructure/WebApi), EF Core + Azure SQL, `Mediator` (martinothamar), FluentValidation, xUnit + Moq + FluentAssertions; React 19 + Vite + Redux Toolkit (RTK Query) + react-hook-form + Syncfusion Pure React + `@vis.gl/react-google-maps`.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-06-29-trip-planner-design.md`. Decisions: ADR-005 (user-scoped), ADR-007 (Google Maps Platform, backend-proxied), ADR-008 (cascade + flag, no auto-optimize), ADR-009 (expenses = Phase 2), ADR-010 (Map-Forward handoff + `@vis.gl/react-google-maps`, NOT `@react-google-maps/api`). Glossary: `CONTEXT.md`.
- **Scope:** user-scoped — handlers call `IUserProvisioner.GetOrProvisionCurrentAsync`, never `RequireFamilyAsync`; every query filters by `Trip.UserId`. Routes sit under `ProtectedRoute` + `AppLayout`, NOT `FamilyRequiredRoute`.
- **Languages:** code/comments/commits English; user-visible UI strings Thai (lift handoff copy).
- **Maps compliance (skill `google-maps-platform`):** before writing any Google Maps code, load that skill and fetch the Places-API-New + Routes sub-skills; API key is server-side only (`GoogleMaps__ApiKey`), never in the browser bundle; store only `place_id` long-term; carry attribution id `gmp_git_agentskills_v1` on the map; run `compliance-review` after Maps code. Use **Places API (New)** and **Routes API** (`computeRoutes`/`computeRouteMatrix`) — never legacy Places/Directions/Distance Matrix.
- **Frontend UI:** Syncfusion-first (`@syncfusion/react-*`); the interactive street map is the single allowed third-party UI. Forms use react-hook-form + `Controller`. Errors via `getErrorMessage` (guidelines §6).
- **Resolved UI decisions (pre-flight, binding — override the example code where it differs):**
  - **Date/time inputs use Syncfusion** `DatePicker`/`TimePicker` (`@syncfusion/react-calendars`), **not** raw `<input type="date|time">`. Verify the exact props in the installed `.d.ts`.
  - **Tab/day strips use Syncfusion `TabComponent`** from **`@syncfusion/ej2-react-navigations`** (the ej2 legacy fallback — Pure-React `@syncfusion/react-navigations@33.1.44` has NO Tab, only Toolbar/ContextMenu, verified in its `.d.ts`; guideline §2 mandates the ej2 fallback before hand-rolling). Implement `SegmentedTabs` as a thin `TabComponent`-backed wrapper: header-only items, controlled via `selectedItem` index, driving `onChange(value)` from the tab's `selected`/`selecting` event, with content rendered by the PARENT (not Tab's content panes). Import `@syncfusion/ej2-react-navigations/styles/material.css`. The same `SegmentedTabs` is reused for the Places/Itinerary strip, the Map/List toggle, and the day strip (T13/T16/T19) — keep its props contract stable.
  - **Stop reorder uses ▲/▼ buttons** in the MVP (both call the same `reorderStops` mutation). Drag-and-drop is Phase 2.
- **Migrations:** run from `backend/src`: `dotnet ef migrations add <Name> --project MenuNest.Infrastructure --startup-project MenuNest.WebApi`.
- **JSON wire format:** the API is configured (Program.cs:173) with `JsonStringEnumConverter` and default camelCase property naming. So `TravelMode`/`PlaceCategory` serialize as **strings** (`"Drive"`, `"See"`) — the frontend string unions match — and all DTO properties are camelCase on the wire (`legToReach`, `tripPlaceId`, `dayStartTime`). `DateOnly`→`"yyyy-MM-dd"`, `TimeOnly`→`"HH:mm:ss"`.
- **Commits:** end every commit message body with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` (omitted from the snippets below for brevity — add it).

## File Structure

**Backend**
- `MenuNest.Domain/Enums/PlaceCategory.cs`, `TravelMode.cs` — enums.
- `MenuNest.Domain/Entities/Trip.cs`, `TripPlace.cs`, `ItineraryDay.cs`, `Stop.cs` — aggregate.
- `MenuNest.Infrastructure/Persistence/Configurations/Trip*Configuration.cs` (4) — EF mapping.
- `MenuNest.Infrastructure/Persistence/AppDbContext.cs` + `MenuNest.Application/Abstractions/IApplicationDbContext.cs` — add 4 DbSets.
- `MenuNest.Application/UseCases/Trips/**` — commands/queries/handlers/validators + `TripDtos.cs`.
- `MenuNest.Application/Abstractions/IPlaceResolver.cs`, `IRouteService.cs` — Maps seams + DTOs.
- `MenuNest.Infrastructure/Maps/GooglePlaceResolver.cs`, `GoogleRouteService.cs`, `HaversineRouteService.cs`, `MissingConfigPlaceResolver.cs`, `GoogleMapsOptions.cs` — Maps impls + options.
- `MenuNest.WebApi/Controllers/TripsController.cs` — endpoints.
- `tests/MenuNest.Application.UnitTests/Trips/**` — handler + domain tests.

**Frontend** (`frontend/src/`)
- `pages/trips/TripsPage.tsx`, `TripsPage.css`, `tripsSlice.ts`, `index.ts`
- `pages/trips/TripDetailPage.tsx`, `TripDetailPage.css`
- `pages/trips/hooks/useSchedule.ts`, `useResolveLink.ts`
- `pages/trips/components/` — `CreateTripDialog.tsx`, `PlaceCard.tsx`, `AddPlaceSheet.tsx`, `ItineraryStopCard.tsx`, `TravelLeg.tsx`, `DwellStepper.tsx`, `BestTimeBar.tsx`, `StopEditorDialog.tsx`, `TripMap.tsx`, `SegmentedTabs.tsx`
- Modify: `shared/api/api.ts`, `store/index.ts`, `router.tsx`, `shared/components/NavBar.tsx`, `index.html` (Spline Sans Mono), `package.json`.

---

## Task 1: Domain enums + `Trip` entity

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/PlaceCategory.cs`, `backend/src/MenuNest.Domain/Enums/TravelMode.cs`
- Create: `backend/src/MenuNest.Domain/Entities/Trip.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripTests.cs`

**Interfaces:**
- Produces: `enum PlaceCategory { Stay, Eat, See, Cafe, Shop, Other }`; `enum TravelMode { Drive, Walk, Transit }`; `Trip.Create(Guid userId, string name, DateOnly startDate, int dayCount, TravelMode defaultTravelMode, string? destination = null)`, `Trip.Rename(string)`, `Trip.UpdateDetails(string name, string? destination, TravelMode defaultTravelMode)`, `Trip.Reschedule(DateOnly startDate, int dayCount)`, `Trip.SoftDelete()`. Properties: `UserId, Name, Destination, StartDate (DateOnly), DayCount (int), DefaultTravelMode, DeletedAt`.

- [ ] **Step 1: Write the failing test**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripTests.cs
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 11, 14);

    [Fact]
    public void Create_sets_fields_and_defaults()
    {
        var t = Trip.Create(User, " เชียงใหม่ ", Start, 3, TravelMode.Drive, "Chiang Mai");
        t.UserId.Should().Be(User);
        t.Name.Should().Be("เชียงใหม่");           // trimmed
        t.Destination.Should().Be("Chiang Mai");
        t.StartDate.Should().Be(Start);
        t.DayCount.Should().Be(3);
        t.DefaultTravelMode.Should().Be(TravelMode.Drive);
        t.DeletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name) =>
        FluentActions.Invoking(() => Trip.Create(User, name, Start, 3, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_rejects_non_positive_day_count() =>
        FluentActions.Invoking(() => Trip.Create(User, "x", Start, 0, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void Reschedule_updates_start_and_count()
    {
        var t = Trip.Create(User, "x", Start, 3, TravelMode.Drive);
        t.Reschedule(new DateOnly(2026, 12, 1), 5);
        t.StartDate.Should().Be(new DateOnly(2026, 12, 1));
        t.DayCount.Should().Be(5);
    }

    [Fact]
    public void SoftDelete_stamps_DeletedAt()
    {
        var t = Trip.Create(User, "x", Start, 3, TravelMode.Drive);
        t.SoftDelete();
        t.DeletedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~Trips.Domain.TripTests`
Expected: FAIL — `Trip`/`PlaceCategory`/`TravelMode` do not exist (compile error).

- [ ] **Step 3: Write the enums**

```csharp
// backend/src/MenuNest.Domain/Enums/PlaceCategory.cs
namespace MenuNest.Domain.Enums;

public enum PlaceCategory { Stay, Eat, See, Cafe, Shop, Other }
```

```csharp
// backend/src/MenuNest.Domain/Enums/TravelMode.cs
namespace MenuNest.Domain.Enums;

public enum TravelMode { Drive, Walk, Transit }
```

- [ ] **Step 4: Write the `Trip` entity**

```csharp
// backend/src/MenuNest.Domain/Entities/Trip.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A planned journey owned by one <see cref="User"/> (user-scoped — ADR-005).
/// Holds a per-trip pool of <see cref="TripPlace"/> and a day-by-day itinerary
/// (<see cref="ItineraryDay"/> → <see cref="Stop"/>). Expenses are Phase 2 (ADR-009).
/// </summary>
public sealed class Trip : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Destination { get; private set; }
    public DateOnly StartDate { get; private set; }
    public int DayCount { get; private set; }
    public TravelMode DefaultTravelMode { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Trip() { } // EF

    public static Trip Create(
        Guid userId, string name, DateOnly startDate, int dayCount,
        TravelMode defaultTravelMode, string? destination = null)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Trip name is required.");
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");

        return new Trip
        {
            UserId = userId,
            Name = name.Trim(),
            Destination = destination?.Trim(),
            StartDate = startDate,
            DayCount = dayCount,
            DefaultTravelMode = defaultTravelMode,
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Trip name is required.");
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, string? destination, TravelMode defaultTravelMode)
    {
        Rename(name);
        Destination = destination?.Trim();
        DefaultTravelMode = defaultTravelMode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reschedule(DateOnly startDate, int dayCount)
    {
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");
        StartDate = startDate;
        DayCount = dayCount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~Trips.Domain.TripTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums backend/src/MenuNest.Domain/Entities/Trip.cs backend/tests/MenuNest.Application.UnitTests/Trips
git commit -m "feat(trips): add PlaceCategory/TravelMode enums and Trip entity"
```

---

## Task 2: `TripPlace`, `ItineraryDay`, `Stop` entities

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/TripPlace.cs`, `ItineraryDay.cs`, `Stop.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs`, `StopTests.cs`

**Interfaces:**
- Produces:
  - `TripPlace.Create(Guid tripId, string name, double lat, double lng, PlaceCategory category, string? googlePlaceId = null, string? address = null, int? priceLevel = null, string? photoUrl = null, string? openingHoursJson = null)`; `.UpdateDetails(string name, PlaceCategory category, string? address, string? feeNote, string? notes)`; `.SetBestTime(TimeOnly? start, TimeOnly? end)`. Props: `TripId, GooglePlaceId, Name, Lat, Lng, Address, Category, PriceLevel, PhotoUrl, BestTimeStart, BestTimeEnd, OpeningHoursJson, FeeNote, Notes`.
  - `ItineraryDay.Create(Guid tripId, DateOnly date, TimeOnly? dayStartTime = null)` (default 09:00); `.SetStartTime(TimeOnly)`. Props: `TripId, Date, DayStartTime`.
  - `Stop.Create(Guid itineraryDayId, Guid tripPlaceId, int sequence, int dwellMinutes, TravelMode travelModeToReach)`; `.SetSequence(int)`; `.SetDwell(int)`; `.SetTravelMode(TravelMode)`. Props: `ItineraryDayId, TripPlaceId, Sequence, DwellMinutes, TravelModeToReach, Notes`.

- [ ] **Step 1: Write the failing tests**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripPlaceTests
{
    private static readonly Guid Trip = Guid.NewGuid();

    [Fact]
    public void Create_sets_core_fields()
    {
        var p = TripPlace.Create(Trip, "วัดพระธาตุ", 18.80, 98.92, PlaceCategory.See, "places/ChIJxxx");
        p.TripId.Should().Be(Trip);
        p.Name.Should().Be("วัดพระธาตุ");
        p.Lat.Should().Be(18.80);
        p.Category.Should().Be(PlaceCategory.See);
        p.GooglePlaceId.Should().Be("places/ChIJxxx");
    }

    [Fact]
    public void Create_rejects_blank_name() =>
        FluentActions.Invoking(() => TripPlace.Create(Trip, "  ", 0, 0, PlaceCategory.Other))
            .Should().Throw<DomainException>();

    [Fact]
    public void SetBestTime_rejects_end_before_start() =>
        FluentActions.Invoking(() =>
            TripPlace.Create(Trip, "x", 0, 0, PlaceCategory.Other)
                .SetBestTime(new TimeOnly(18, 0), new TimeOnly(9, 0)))
            .Should().Throw<DomainException>();
}
```

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/Domain/StopTests.cs
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class StopTests
{
    [Fact]
    public void Day_defaults_start_to_9am()
    {
        var d = ItineraryDay.Create(Guid.NewGuid(), new DateOnly(2026, 11, 14));
        d.DayStartTime.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void Stop_rejects_non_positive_dwell() =>
        FluentActions.Invoking(() =>
            Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 0, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void SetDwell_updates_minutes()
    {
        var s = Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 60, TravelMode.Walk);
        s.SetDwell(90);
        s.DwellMinutes.Should().Be(90);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Trips.Domain.TripPlaceTests|FullyQualifiedName~Trips.Domain.StopTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write `TripPlace`**

```csharp
// backend/src/MenuNest.Domain/Entities/TripPlace.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A saved candidate location in a Trip's pool, anchored to a Google
/// <c>place_id</c> when resolved (the only Maps datum stored long-term — ADR-007).
/// Other fields are a cached snapshot from a live Places API call, never scraped.
/// </summary>
public sealed class TripPlace : Entity
{
    public Guid TripId { get; private set; }
    public string? GooglePlaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public string? Address { get; private set; }
    public PlaceCategory Category { get; private set; }
    public int? PriceLevel { get; private set; }
    public string? PhotoUrl { get; private set; }
    public TimeOnly? BestTimeStart { get; private set; }
    public TimeOnly? BestTimeEnd { get; private set; }
    public string? OpeningHoursJson { get; private set; }
    public string? FeeNote { get; private set; }
    public string? Notes { get; private set; }

    private TripPlace() { } // EF

    public static TripPlace Create(
        Guid tripId, string name, double lat, double lng, PlaceCategory category,
        string? googlePlaceId = null, string? address = null, int? priceLevel = null,
        string? photoUrl = null, string? openingHoursJson = null)
    {
        if (tripId == Guid.Empty) throw new DomainException("TripId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Place name is required.");
        if (priceLevel is < 0 or > 4) throw new DomainException("Price level must be 0–4.");

        return new TripPlace
        {
            TripId = tripId,
            Name = name.Trim(),
            Lat = lat,
            Lng = lng,
            Category = category,
            GooglePlaceId = googlePlaceId,
            Address = address?.Trim(),
            PriceLevel = priceLevel,
            PhotoUrl = photoUrl,
            OpeningHoursJson = openingHoursJson,
        };
    }

    public void UpdateDetails(string name, PlaceCategory category, string? address, string? feeNote, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Place name is required.");
        Name = name.Trim();
        Category = category;
        Address = address?.Trim();
        FeeNote = feeNote?.Trim();
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetBestTime(TimeOnly? start, TimeOnly? end)
    {
        if (start is not null && end is not null && end <= start)
            throw new DomainException("Best-time end must be after start.");
        BestTimeStart = start;
        BestTimeEnd = end;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Write `ItineraryDay` and `Stop`**

```csharp
// backend/src/MenuNest.Domain/Entities/ItineraryDay.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>One calendar day of a Trip's itinerary; owns ordered <see cref="Stop"/>s.</summary>
public sealed class ItineraryDay : Entity
{
    public Guid TripId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly DayStartTime { get; private set; }

    private ItineraryDay() { } // EF

    public static ItineraryDay Create(Guid tripId, DateOnly date, TimeOnly? dayStartTime = null)
    {
        if (tripId == Guid.Empty) throw new DomainException("TripId is required.");
        return new ItineraryDay { TripId = tripId, Date = date, DayStartTime = dayStartTime ?? new TimeOnly(9, 0) };
    }

    public void SetStartTime(TimeOnly start)
    {
        DayStartTime = start;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

```csharp
// backend/src/MenuNest.Domain/Entities/Stop.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One scheduled visit in an <see cref="ItineraryDay"/>: a reference to a
/// <see cref="TripPlace"/> plus a dwell duration and the travel mode used on the
/// leg arriving from the previous stop (<see cref="Sequence"/> 0 has no leg).
/// Arrival/leave times are derived, never stored (ADR-008).
/// </summary>
public sealed class Stop : Entity
{
    public Guid ItineraryDayId { get; private set; }
    public Guid TripPlaceId { get; private set; }
    public int Sequence { get; private set; }
    public int DwellMinutes { get; private set; }
    public TravelMode TravelModeToReach { get; private set; }
    public string? Notes { get; private set; }

    private Stop() { } // EF

    public static Stop Create(Guid itineraryDayId, Guid tripPlaceId, int sequence, int dwellMinutes, TravelMode travelModeToReach)
    {
        if (itineraryDayId == Guid.Empty) throw new DomainException("ItineraryDayId is required.");
        if (tripPlaceId == Guid.Empty) throw new DomainException("TripPlaceId is required.");
        if (sequence < 0) throw new DomainException("Sequence cannot be negative.");
        if (dwellMinutes <= 0) throw new DomainException("Dwell minutes must be positive.");

        return new Stop
        {
            ItineraryDayId = itineraryDayId,
            TripPlaceId = tripPlaceId,
            Sequence = sequence,
            DwellMinutes = dwellMinutes,
            TravelModeToReach = travelModeToReach,
        };
    }

    public void SetSequence(int sequence)
    {
        if (sequence < 0) throw new DomainException("Sequence cannot be negative.");
        Sequence = sequence;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDwell(int dwellMinutes)
    {
        if (dwellMinutes <= 0) throw new DomainException("Dwell minutes must be positive.");
        DwellMinutes = dwellMinutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTravelMode(TravelMode mode)
    {
        TravelModeToReach = mode;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Trips.Domain.TripPlaceTests|FullyQualifiedName~Trips.Domain.StopTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/TripPlace.cs backend/src/MenuNest.Domain/Entities/ItineraryDay.cs backend/src/MenuNest.Domain/Entities/Stop.cs backend/tests/MenuNest.Application.UnitTests/Trips/Domain
git commit -m "feat(trips): add TripPlace, ItineraryDay, Stop entities"
```

---

## Task 3: EF Core mapping + DbSets + migration

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripConfiguration.cs`, `TripPlaceConfiguration.cs`, `ItineraryDayConfiguration.cs`, `StopConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` (add 4 DbSets), `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` (add 4 DbSets)
- Create (generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<ts>_TripsInitial.cs`

**Interfaces:**
- Consumes: entities from Tasks 1–2.
- Produces: `IApplicationDbContext.Trips`, `.TripPlaces`, `.ItineraryDays`, `.Stops` (all `DbSet<T>`).

- [ ] **Step 1: Add DbSets to `IApplicationDbContext`**

Open `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`; add alongside the existing Health DbSets:

```csharp
DbSet<Trip> Trips { get; }
DbSet<TripPlace> TripPlaces { get; }
DbSet<ItineraryDay> ItineraryDays { get; }
DbSet<Stop> Stops { get; }
```

(Add `using MenuNest.Domain.Entities;` if not already imported.)

- [ ] **Step 2: Add DbSets to `AppDbContext`**

Open `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`; add next to the existing DbSets:

```csharp
public DbSet<Trip> Trips => Set<Trip>();
public DbSet<TripPlace> TripPlaces => Set<TripPlace>();
public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
public DbSet<Stop> Stops => Set<Stop>();
```

- [ ] **Step 3: Write the EF configurations**

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("Trips");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.UserId).IsRequired();
        b.Property(t => t.Name).IsRequired().HasMaxLength(200);
        b.Property(t => t.Destination).HasMaxLength(200);
        b.Property(t => t.StartDate).IsRequired();
        b.Property(t => t.DayCount).IsRequired();
        b.Property(t => t.DefaultTravelMode).HasConversion<int>();
        b.HasIndex(t => new { t.UserId, t.DeletedAt });
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class TripPlaceConfiguration : IEntityTypeConfiguration<TripPlace>
{
    public void Configure(EntityTypeBuilder<TripPlace> b)
    {
        b.ToTable("TripPlaces");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.TripId).IsRequired();
        b.Property(p => p.Name).IsRequired().HasMaxLength(300);
        b.Property(p => p.GooglePlaceId).HasMaxLength(400);
        b.Property(p => p.Address).HasMaxLength(500);
        b.Property(p => p.Category).HasConversion<int>();
        b.Property(p => p.OpeningHoursJson).HasColumnType("nvarchar(max)");
        b.Property(p => p.FeeNote).HasMaxLength(200);
        b.Property(p => p.Notes).HasMaxLength(2000);
        b.HasIndex(p => p.TripId);
        // dedupe re-pastes of the same Google place within a trip (filtered: only non-null)
        b.HasIndex(p => new { p.TripId, p.GooglePlaceId })
            .IsUnique()
            .HasFilter("[GooglePlaceId] IS NOT NULL");
        b.HasOne<Trip>().WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/ItineraryDayConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>
{
    public void Configure(EntityTypeBuilder<ItineraryDay> b)
    {
        b.ToTable("ItineraryDays");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.TripId).IsRequired();
        b.Property(d => d.Date).IsRequired();
        b.Property(d => d.DayStartTime).IsRequired();
        b.HasIndex(d => new { d.TripId, d.Date }).IsUnique();
        b.HasOne<Trip>().WithMany().HasForeignKey(d => d.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/StopConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class StopConfiguration : IEntityTypeConfiguration<Stop>
{
    public void Configure(EntityTypeBuilder<Stop> b)
    {
        b.ToTable("Stops");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.ItineraryDayId).IsRequired();
        b.Property(s => s.TripPlaceId).IsRequired();
        b.Property(s => s.Sequence).IsRequired();
        b.Property(s => s.DwellMinutes).IsRequired();
        b.Property(s => s.TravelModeToReach).HasConversion<int>();
        b.Property(s => s.Notes).HasMaxLength(2000);
        b.HasIndex(s => new { s.ItineraryDayId, s.Sequence });
        b.HasOne<ItineraryDay>().WithMany().HasForeignKey(s => s.ItineraryDayId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TripPlace>().WithMany().HasForeignKey(s => s.TripPlaceId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

- [ ] **Step 4: Build to verify configs compile**

Run: `dotnet build backend/src/MenuNest.Infrastructure`
Expected: build succeeds.

- [ ] **Step 5: Mirror the DbSets in the test InMemory context**

Open `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs` and add the same four `DbSet<...>` members (so handler tests can use them).

- [ ] **Step 6: Generate the migration**

Run (from `backend/src`):
```bash
dotnet ef migrations add TripsInitial --project MenuNest.Infrastructure --startup-project MenuNest.WebApi
```
Expected: a new `Migrations/<ts>_TripsInitial.cs` creating `Trips`, `TripPlaces`, `ItineraryDays`, `Stops`. Open it and confirm the four tables + the filtered unique index are present.

- [ ] **Step 7: Apply locally to verify it runs (optional, if a dev DB is configured)**

Run: `dotnet ef database update --project MenuNest.Infrastructure --startup-project MenuNest.WebApi`
Expected: tables created, no error.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs
git commit -m "feat(trips): EF config + DbSets + TripsInitial migration"
```

---

## Task 4: Trip CRUD use cases + DTOs

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/{CreateTripCommand,CreateTripValidator,CreateTripHandler}.cs`
- Create: `…/Trips/ListTrips/{ListTripsQuery,ListTripsHandler}.cs`
- Create: `…/Trips/UpdateTrip/{UpdateTripCommand,UpdateTripValidator,UpdateTripHandler}.cs`
- Create: `…/Trips/DeleteTrip/{DeleteTripCommand,DeleteTripHandler}.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/CreateTripHandlerTests.cs`, `ListTripsHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.Trips/.ItineraryDays`, `IUserProvisioner.GetOrProvisionCurrentAsync`.
- Produces: DTO records below; `CreateTripCommand(string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode) : ICommand<TripDto>`; `ListTripsQuery() : IQuery<IReadOnlyList<TripDto>>`; `UpdateTripCommand(Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode) : ICommand<TripDto>`; `DeleteTripCommand(Guid TripId) : ICommand<Unit>`.

- [ ] **Step 1: Write DTOs**

```csharp
// backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips;

public sealed record TripDto(
    Guid Id, string Name, string? Destination,
    DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode);

public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes);

public sealed record LegDto(int Seconds, int Meters);

public sealed record StopDto(
    Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes,
    TravelMode TravelModeToReach, LegDto? LegToReach);

public sealed record ItineraryDayDto(
    Guid Id, DateOnly Date, TimeOnly DayStartTime, IReadOnlyList<StopDto> Stops);

public sealed record ResolvedPlaceDto(
    string? GooglePlaceId, string Name, double Lat, double Lng, string? Address,
    PlaceCategory Category, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson);
```

- [ ] **Step 2: Write the failing handler tests**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/CreateTripHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class CreateTripHandlerTests
{
    [Fact]
    public async Task Creates_trip_and_seeds_one_day_per_day_count()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());

        var dto = await handler.Handle(
            new CreateTripCommand("เชียงใหม่", "Chiang Mai", new DateOnly(2026, 11, 14), 3, TravelMode.Drive),
            CancellationToken.None);

        dto.DayCount.Should().Be(3);
        var trip = fx.Db.Trips.Single();
        trip.UserId.Should().Be(fx.User.Id);
        var days = await fx.Db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync();
        days.Should().HaveCount(3);
        days[0].Date.Should().Be(new DateOnly(2026, 11, 14));
        days[2].Date.Should().Be(new DateOnly(2026, 11, 16));
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());
        await FluentActions.Awaiting(() => handler.Handle(
            new CreateTripCommand("  ", null, new DateOnly(2026, 11, 14), 3, TravelMode.Drive).AsTask())
        ).Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
```

> Note: `CreateTripCommand(...).AsTask()` won't compile — call `handler.Handle(new CreateTripCommand(...), CancellationToken.None)` directly inside `FluentActions.Awaiting(() => handler.Handle(...).AsTask())`. Use `.AsTask()` on the returned `ValueTask` to satisfy `ThrowAsync`.

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/ListTripsHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class ListTripsHandlerTests
{
    [Fact]
    public async Task Returns_only_current_users_non_deleted_trips()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Trips.Add(Trip.Create(fx.User.Id, "Mine", new DateOnly(2026, 11, 1), 2, TravelMode.Drive));
        var others = Trip.Create(Guid.NewGuid(), "Other", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(others);
        var deleted = Trip.Create(fx.User.Id, "Gone", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        deleted.SoftDelete();
        fx.Db.Trips.Add(deleted);
        await fx.Db.SaveChangesAsync();

        var result = await new ListTripsHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        result.Should().ContainSingle(t => t.Name == "Mine");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~Trips.CreateTripHandlerTests`
Expected: FAIL — handlers not defined.

- [ ] **Step 4: Write `CreateTrip`**

```csharp
// CreateTripCommand.cs
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed record CreateTripCommand(
    string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode)
    : ICommand<TripDto>;
```
```csharp
// CreateTripValidator.cs
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed class CreateTripValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DayCount).InclusiveBetween(1, 60);
    }
}
```
```csharp
// CreateTripHandler.cs
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed class CreateTripHandler : ICommandHandler<CreateTripCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateTripCommand> _validator;

    public CreateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateTripCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripDto> Handle(CreateTripCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var trip = Trip.Create(user.Id, c.Name, c.StartDate, c.DayCount, c.DefaultTravelMode, c.Destination);
        _db.Trips.Add(trip);
        for (var i = 0; i < c.DayCount; i++)
            _db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i)));

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode);
    }
}
```

- [ ] **Step 5: Write `ListTrips`, `UpdateTrip`, `DeleteTrip`**

```csharp
// ListTripsQuery.cs
using Mediator;
namespace MenuNest.Application.UseCases.Trips.ListTrips;
public sealed record ListTripsQuery() : IQuery<IReadOnlyList<TripDto>>;
```
```csharp
// ListTripsHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.ListTrips;

public sealed class ListTripsHandler : IQueryHandler<ListTripsQuery, IReadOnlyList<TripDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListTripsHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<TripDto>> Handle(ListTripsQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        return await _db.Trips
            .Where(t => t.UserId == user.Id && t.DeletedAt == null)
            .OrderByDescending(t => t.StartDate)
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode))
            .ToListAsync(ct);
    }
}
```
```csharp
// UpdateTripCommand.cs
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateTrip;
public sealed record UpdateTripCommand(
    Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode)
    : ICommand<TripDto>;
```
```csharp
// UpdateTripValidator.cs
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.UpdateTrip;
public sealed class UpdateTripValidator : AbstractValidator<UpdateTripCommand>
{
    public UpdateTripValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DayCount).InclusiveBetween(1, 60);
    }
}
```
```csharp
// UpdateTripHandler.cs — updates details + reschedules; reconciles ItineraryDay rows to DayCount.
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

public sealed class UpdateTripHandler : ICommandHandler<UpdateTripCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTripCommand> _validator;
    public UpdateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTripCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripDto> Handle(UpdateTripCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        trip.UpdateDetails(c.Name, c.Destination, c.DefaultTravelMode);
        trip.Reschedule(c.StartDate, c.DayCount);

        var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);
        // add missing trailing days
        for (var i = days.Count; i < c.DayCount; i++)
            _db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i)));
        // remove surplus trailing days (Stops cascade)
        foreach (var extra in days.Skip(c.DayCount))
            _db.ItineraryDays.Remove(extra);
        // realign remaining dates to the new start
        for (var i = 0; i < Math.Min(days.Count, c.DayCount); i++)
            days[i].SetStartTime(days[i].DayStartTime); // touch; date realignment handled below
        // NOTE: keep dates simple — reset each kept day's Date to StartDate+offset
        for (var i = 0; i < Math.Min(days.Count, c.DayCount); i++)
            typeof(ItineraryDay).GetProperty("Date")!.SetValue(days[i], c.StartDate.AddDays(i));

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode);
    }
}
```

> Reflection on `Date` is a smell. Replace it by adding a domain method `ItineraryDay.SetDate(DateOnly date)` (mirrors `SetStartTime`) and call `days[i].SetDate(c.StartDate.AddDays(i))`. Add that method in Task 2's entity when implementing; the test for reschedule lives in Task 2 (`Day_realigns_date`). Do not ship the reflection version.

```csharp
// DeleteTripCommand.cs
using Mediator;
namespace MenuNest.Application.UseCases.Trips.DeleteTrip;
public sealed record DeleteTripCommand(Guid TripId) : ICommand<Unit>;
```
```csharp
// DeleteTripHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.DeleteTrip;

public sealed class DeleteTripHandler : ICommandHandler<DeleteTripCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteTripHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteTripCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");
        trip.SoftDelete();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 6: Add `ItineraryDay.SetDate` (resolves the reflection smell)**

In `backend/src/MenuNest.Domain/Entities/ItineraryDay.cs` add:
```csharp
public void SetDate(DateOnly date) { Date = date; UpdatedAt = DateTime.UtcNow; }
```
Then change `UpdateTripHandler` to use `days[i].SetDate(c.StartDate.AddDays(i));` and delete the reflection lines.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Trips.CreateTripHandlerTests|FullyQualifiedName~Trips.ListTripsHandlerTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips backend/src/MenuNest.Domain/Entities/ItineraryDay.cs backend/tests/MenuNest.Application.UnitTests/Trips
git commit -m "feat(trips): trip CRUD use cases (create seeds days, list/update/delete)"
```

---

## Task 5: `TripsController` — trip CRUD endpoints

**Files:**
- Create: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`

**Interfaces:**
- Consumes: the Task 4 commands/queries via `IMediator`.
- Produces: `GET/POST /api/trips`, `PUT/DELETE /api/trips/{id}`.

- [ ] **Step 1: Write the controller (trip CRUD section)**

```csharp
// backend/src/MenuNest.WebApi/Controllers/TripsController.cs
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class TripsController : ControllerBase
{
    private readonly IMediator _mediator;
    public TripsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/trips")]
    public async Task<ActionResult<IReadOnlyList<TripDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListTripsQuery(), ct));

    [HttpPost("api/trips")]
    public async Task<ActionResult<TripDto>> Create([FromBody] CreateTripCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpPut("api/trips/{id:guid}")]
    public async Task<ActionResult<TripDto>> Update(Guid id, [FromBody] UpdateTripBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateTripCommand(id, body.Name, body.Destination, body.StartDate, body.DayCount, body.DefaultTravelMode), ct));

    [HttpDelete("api/trips/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteTripCommand(id), ct);
        return NoContent();
    }
}

public sealed record UpdateTripBody(
    string Name, string? Destination, DateOnly StartDate, int DayCount,
    MenuNest.Domain.Enums.TravelMode DefaultTravelMode);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/MenuNest.WebApi`
Expected: success.

- [ ] **Step 3: Smoke-test via Swagger (manual)**

Run the API (`dotnet run --project backend/src/MenuNest.WebApi`), open Swagger, `POST /api/trips` with a body, confirm 200 + a `TripDto`, then `GET /api/trips` returns it.

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/TripsController.cs
git commit -m "feat(trips): TripsController CRUD endpoints"
```

---

## Task 6: `IPlaceResolver` + Google Places resolver + config + DI

> **Maps grounding gate (do FIRST, before writing any code in this task):** load the
> `google-maps-platform` skill; fetch its index (`https://www.gstatic.com/googlemapsplatform-agent-skills/index.json?client=claude-code`)
> and the **Places API (New)** sub-skill; confirm the current Text Search / Place
> Details request shape and field mask. The code below is the integration *shape* —
> reconcile endpoint URLs/field names against the freshly fetched docs before finalising.

**Files:**
- Create: `backend/src/MenuNest.Application/Abstractions/IPlaceResolver.cs`
- Create: `backend/src/MenuNest.Infrastructure/Maps/GoogleMapsOptions.cs`, `GooglePlaceResolver.cs`, `MissingConfigPlaceResolver.cs`
- Modify: `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GooglePlaceResolverTests.cs`

**Interfaces:**
- Produces: `IPlaceResolver.ResolveFromUrlAsync(string url, CancellationToken ct) -> Task<ResolvedPlaceDto>`; `GoogleMapsOptions { SectionName = "GoogleMaps"; ApiKey; MapId; BrowserKey }`.

- [ ] **Step 1: Write the abstraction + options**

```csharp
// backend/src/MenuNest.Application/Abstractions/IPlaceResolver.cs
using MenuNest.Application.UseCases.Trips;
namespace MenuNest.Application.Abstractions;

public interface IPlaceResolver
{
    /// <summary>Resolve a shared Google Maps URL to authoritative place data via a live API.</summary>
    Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct);
}
```
```csharp
// backend/src/MenuNest.Infrastructure/Maps/GoogleMapsOptions.cs
namespace MenuNest.Infrastructure.Maps;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";
    public string? ApiKey { get; set; }      // server-side: Places + Routes + Geocoding
    public string? BrowserKey { get; set; }   // Maps JS (referrer-restricted) — surfaced to SPA build
    public string? MapId { get; set; }        // "DEMO_MAP_ID" in dev
}
```

- [ ] **Step 2: Write the failing resolver test**

The resolver follows a redirect, extracts a query, then calls Places. Test it with a stubbed `HttpMessageHandler` so no live call happens.

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GooglePlaceResolverTests.cs
using System.Net;
using System.Net.Http;
using FluentAssertions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Options;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GooglePlaceResolverTests
{
    private sealed class StubHandler : Queue<HttpResponseMessage>, IDisposable
    {
        public List<string> Requested { get; } = new();
        public void Dispose() { }
    }

    private sealed class SequencedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    [Fact]
    public async Task Resolves_place_id_and_name_via_places_text_search()
    {
        var handler = new SequencedHandler(req =>
        {
            // 1) redirect unfurl of the short link → long URL with the place name
            if (req.RequestUri!.Host.Contains("maps.app.goo.gl"))
            {
                var r = new HttpResponseMessage(HttpStatusCode.Redirect);
                r.Headers.Location = new Uri("https://www.google.com/maps/place/Wat+Phra+That/@18.8,98.9,17z");
                return r;
            }
            // 2) Places Text Search returns the authoritative place
            var body = """
            {"places":[{"id":"ChIJabc","displayName":{"text":"Wat Phra That"},
              "location":{"latitude":18.8049,"longitude":98.9217},
              "formattedAddress":"Chiang Mai","priceLevel":"PRICE_LEVEL_FREE",
              "regularOpeningHours":{"weekdayDescriptions":["Mon: 6AM-6PM"]}}]}
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var http = new HttpClient(handler) { };
        var factory = new SingleClientFactory(http);
        var opts = Options.Create(new GoogleMapsOptions { ApiKey = "demo" });

        var resolver = new GooglePlaceResolver(factory, opts);
        var dto = await resolver.ResolveFromUrlAsync("https://maps.app.goo.gl/abc", CancellationToken.None);

        dto.GooglePlaceId.Should().Be("ChIJabc");
        dto.Name.Should().Be("Wat Phra That");
        dto.Lat.Should().BeApproximately(18.8049, 0.0001);
        dto.Category.Should().Be(PlaceCategory.Other); // category is user-chosen later
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~Trips.Maps.GooglePlaceResolverTests`
Expected: FAIL — `GooglePlaceResolver` not defined.

- [ ] **Step 4: Implement `GooglePlaceResolver` + `MissingConfigPlaceResolver`**

```csharp
// backend/src/MenuNest.Infrastructure/Maps/GooglePlaceResolver.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MenuNest.Infrastructure.Maps;

/// <summary>
/// Resolves a shared Google Maps URL to authoritative place data. The short link is
/// unfurled server-side (CORS CF1), then a Places API (New) Text Search returns the
/// place_id + snapshot — scraped coords are never the stored truth (ToS, ADR-007).
/// </summary>
public sealed class GooglePlaceResolver : IPlaceResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly GoogleMapsOptions _opts;

    public GooglePlaceResolver(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts)
    { _http = http; _opts = opts.Value; }

    public async Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new DomainException("Maps is not configured.");

        var client = _http.CreateClient();
        // 1) unfurl short links by following the redirect to the long URL
        var longUrl = url;
        var head = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (head.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently && head.Headers.Location is not null)
            longUrl = head.Headers.Location.ToString();

        // 2) extract a text query from the /place/<name>/ segment of the long URL
        var query = ExtractPlaceQuery(longUrl)
            ?? throw new DomainException("Could not read that Google Maps link. Enter the place manually.");

        // 3) Places API (New) Text Search (field mask per the Places-New sub-skill)
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText");
        req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
        req.Headers.Add("X-Goog-FieldMask",
            "places.id,places.displayName,places.location,places.formattedAddress,places.priceLevel,places.regularOpeningHours");
        req.Content = JsonContent.Create(new { textQuery = query });
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var place = doc.RootElement.GetProperty("places").EnumerateArray().FirstOrDefault();
        if (place.ValueKind == JsonValueKind.Undefined)
            throw new DomainException("No place found for that link. Enter it manually.");

        var loc = place.GetProperty("location");
        return new ResolvedPlaceDto(
            GooglePlaceId: place.GetProperty("id").GetString(),
            Name: place.GetProperty("displayName").GetProperty("text").GetString() ?? query,
            Lat: loc.GetProperty("latitude").GetDouble(),
            Lng: loc.GetProperty("longitude").GetDouble(),
            Address: place.TryGetProperty("formattedAddress", out var a) ? a.GetString() : null,
            Category: PlaceCategory.Other,
            PriceLevel: MapPriceLevel(place),
            PhotoUrl: null,
            OpeningHoursJson: place.TryGetProperty("regularOpeningHours", out var h) ? h.GetRawText() : null);
    }

    internal static string? ExtractPlaceQuery(string longUrl)
    {
        var marker = "/place/";
        var i = longUrl.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = longUrl[(i + marker.Length)..];
        var end = rest.IndexOf('/');
        var seg = end >= 0 ? rest[..end] : rest;
        return Uri.UnescapeDataString(seg.Replace('+', ' ')).Trim();
    }

    private static int? MapPriceLevel(JsonElement place) =>
        place.TryGetProperty("priceLevel", out var p) ? p.GetString() switch
        {
            "PRICE_LEVEL_FREE" => 0,
            "PRICE_LEVEL_INEXPENSIVE" => 1,
            "PRICE_LEVEL_MODERATE" => 2,
            "PRICE_LEVEL_EXPENSIVE" => 3,
            "PRICE_LEVEL_VERY_EXPENSIVE" => 4,
            _ => null,
        } : null;
}
```
```csharp
// backend/src/MenuNest.Infrastructure/Maps/MissingConfigPlaceResolver.cs
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Exceptions;
namespace MenuNest.Infrastructure.Maps;

/// <summary>Registered when no Maps API key is configured — fail with a clear message.</summary>
public sealed class MissingConfigPlaceResolver : IPlaceResolver
{
    public Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct)
        => throw new DomainException("Maps link resolving is not configured. Add the place manually.");
}
```

Add `using System.Net;` and `using System.Net.Http.Json;` to `GooglePlaceResolver.cs` as needed.

- [ ] **Step 5: Register in DI (conditional, mirroring the Blob pattern)**

In `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`, inside `AddInfrastructure`:
```csharp
services.AddHttpClient();
services.Configure<GoogleMapsOptions>(configuration.GetSection(GoogleMapsOptions.SectionName));
var mapsKey = configuration[$"{GoogleMapsOptions.SectionName}:ApiKey"];
if (!string.IsNullOrWhiteSpace(mapsKey))
    services.AddScoped<IPlaceResolver, GooglePlaceResolver>();
else
    services.AddScoped<IPlaceResolver, MissingConfigPlaceResolver>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~Trips.Maps.GooglePlaceResolverTests`
Expected: PASS. (Also add a `ExtractPlaceQuery` unit test for a `/place/Name/@..` URL → "Name".)

- [ ] **Step 7: Run `compliance-review` (skill) on the new Maps code, then commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/IPlaceResolver.cs backend/src/MenuNest.Infrastructure/Maps backend/src/MenuNest.Infrastructure/DependencyInjection.cs backend/tests/MenuNest.Application.UnitTests/Trips/Maps
git commit -m "feat(trips): IPlaceResolver + Google Places resolver (server-side, ToS-compliant)"
```

---

## Task 7: Resolve + Place pool use cases + endpoints

**Files:**
- Create: `…/UseCases/Trips/ResolvePlace/{ResolvePlaceCommand,ResolvePlaceValidator,ResolvePlaceHandler}.cs`
- Create: `…/UseCases/Trips/AddTripPlace/{AddTripPlaceCommand,AddTripPlaceValidator,AddTripPlaceHandler}.cs`
- Create: `…/UseCases/Trips/ListTripPlaces/{ListTripPlacesQuery,ListTripPlacesHandler}.cs`
- Create: `…/UseCases/Trips/UpdateTripPlace/{…}.cs`, `…/Trips/DeleteTripPlace/{…}.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs`

**Interfaces:**
- Consumes: `IPlaceResolver`, `IApplicationDbContext.TripPlaces/.Trips`.
- Produces: `ResolvePlaceCommand(string Url) : ICommand<ResolvedPlaceDto>`; `AddTripPlaceCommand(Guid TripId, string Name, double Lat, double Lng, PlaceCategory Category, string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson) : ICommand<TripPlaceDto>`; `ListTripPlacesQuery(Guid TripId) : IQuery<IReadOnlyList<TripPlaceDto>>`; `UpdateTripPlaceCommand(Guid TripId, Guid PlaceId, string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd) : ICommand<TripPlaceDto>`; `DeleteTripPlaceCommand(Guid TripId, Guid PlaceId) : ICommand<Unit>`.

- [ ] **Step 1: Write failing test (AddTripPlace ownership + persistence)**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class AddTripPlaceHandlerTests
{
    [Fact]
    public async Task Adds_place_to_owned_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var dto = await new AddTripPlaceHandler(fx.Db, fx.UserProvisioner.Object, new AddTripPlaceValidator())
            .Handle(new AddTripPlaceCommand(trip.Id, "Wat", 18.8, 98.9, PlaceCategory.See, "ChIJabc", null, 0, null, null),
                CancellationToken.None);

        dto.Name.Should().Be("Wat");
        fx.Db.TripPlaces.Should().ContainSingle(p => p.TripId == trip.Id);
    }

    [Fact]
    public async Task Rejects_place_on_trip_not_owned()
    {
        using var fx = new HandlerTestFixture();
        var foreign = Trip.Create(Guid.NewGuid(), "x", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(foreign);
        await fx.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => new AddTripPlaceHandler(fx.Db, fx.UserProvisioner.Object, new AddTripPlaceValidator())
            .Handle(new AddTripPlaceCommand(foreign.Id, "Wat", 0, 0, PlaceCategory.Other, null, null, null, null, null),
                CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test … --filter FullyQualifiedName~Trips.AddTripPlaceHandlerTests` → FAIL.

- [ ] **Step 3: Write `ResolvePlace`**

```csharp
// ResolvePlaceCommand.cs
using Mediator;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;
public sealed record ResolvePlaceCommand(string Url) : ICommand<ResolvedPlaceDto>;
```
```csharp
// ResolvePlaceValidator.cs
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;
public sealed class ResolvePlaceValidator : AbstractValidator<ResolvePlaceCommand>
{
    public ResolvePlaceValidator() => RuleFor(x => x.Url).NotEmpty().Must(u =>
        Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
        .WithMessage("Provide a valid Google Maps link.");
}
```
```csharp
// ResolvePlaceHandler.cs
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;

public sealed class ResolvePlaceHandler : ICommandHandler<ResolvePlaceCommand, ResolvedPlaceDto>
{
    private readonly IPlaceResolver _resolver;
    private readonly IUserProvisioner _users;
    private readonly IValidator<ResolvePlaceCommand> _validator;
    public ResolvePlaceHandler(IPlaceResolver resolver, IUserProvisioner users, IValidator<ResolvePlaceCommand> validator)
    { _resolver = resolver; _users = users; _validator = validator; }

    public async ValueTask<ResolvedPlaceDto> Handle(ResolvePlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        await _users.GetOrProvisionCurrentAsync(ct); // ensure authenticated
        return await _resolver.ResolveFromUrlAsync(c.Url, ct);
    }
}
```

- [ ] **Step 4: Write `AddTripPlace` (+ a `RequireOwnedTripAsync` helper)**

```csharp
// AddTripPlaceCommand.cs
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;
public sealed record AddTripPlaceCommand(
    Guid TripId, string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson)
    : ICommand<TripPlaceDto>;
```
```csharp
// AddTripPlaceValidator.cs
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;
public sealed class AddTripPlaceValidator : AbstractValidator<AddTripPlaceCommand>
{
    public AddTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.PriceLevel).InclusiveBetween(0, 4).When(x => x.PriceLevel.HasValue);
    }
}
```
```csharp
// AddTripPlaceHandler.cs
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;

public sealed class AddTripPlaceHandler : ICommandHandler<AddTripPlaceCommand, TripPlaceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AddTripPlaceCommand> _validator;
    public AddTripPlaceHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AddTripPlaceCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripPlaceDto> Handle(AddTripPlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var place = TripPlace.Create(c.TripId, c.Name, c.Lat, c.Lng, c.Category,
            c.GooglePlaceId, c.Address, c.PriceLevel, c.PhotoUrl, c.OpeningHoursJson);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync(ct);
        return ToDto(place);
    }

    internal static TripPlaceDto ToDto(TripPlace p) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.BestTimeStart, p.BestTimeEnd, p.OpeningHoursJson, p.FeeNote, p.Notes);
}
```

- [ ] **Step 5: Write `ListTripPlaces`, `UpdateTripPlace`, `DeleteTripPlace`** (each: provision user → verify trip ownership → act → return). `UpdateTripPlace` calls `place.UpdateDetails(...)` then `place.SetBestTime(BestTimeStart, BestTimeEnd)`; reuse `AddTripPlaceHandler.ToDto`. `ListTripPlaces` filters `TripPlaces.Where(p => p.TripId == c.TripId)` after confirming the trip belongs to the user, ordered by `Name`. Persist with `SaveChangesAsync`. (Full code mirrors Step 4's ownership-check shape — verify ownership via `_db.Trips.AnyAsync(...)`, throw `DomainException("Trip not found.")` if absent; for place-scoped ops also `_db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId)`.)

- [ ] **Step 6: Add place endpoints to `TripsController`**

```csharp
    [HttpPost("api/trips/resolve-place")]
    public async Task<ActionResult<ResolvedPlaceDto>> Resolve([FromBody] ResolvePlaceCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpGet("api/trips/{id:guid}/places")]
    public async Task<ActionResult<IReadOnlyList<TripPlaceDto>>> ListPlaces(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListTripPlacesQuery(id), ct));

    [HttpPost("api/trips/{id:guid}/places")]
    public async Task<ActionResult<TripPlaceDto>> AddPlace(Guid id, [FromBody] AddPlaceBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AddTripPlaceCommand(id, b.Name, b.Lat, b.Lng, b.Category,
            b.GooglePlaceId, b.Address, b.PriceLevel, b.PhotoUrl, b.OpeningHoursJson), ct));

    [HttpPut("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<ActionResult<TripPlaceDto>> UpdatePlace(Guid id, Guid placeId, [FromBody] UpdatePlaceBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeStart, b.BestTimeEnd), ct));

    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<IActionResult> DeletePlace(Guid id, Guid placeId, CancellationToken ct)
    { await _mediator.Send(new DeleteTripPlaceCommand(id, placeId), ct); return NoContent(); }
```
Add the body records (`AddPlaceBody`, `UpdatePlaceBody`) at the bottom of the file mirroring the command fields (minus the route-supplied ids).

- [ ] **Step 7: Run tests + build** — `dotnet test … --filter FullyQualifiedName~Trips.AddTripPlaceHandlerTests` → PASS; `dotnet build backend/src/MenuNest.WebApi`.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/tests/MenuNest.Application.UnitTests/Trips
git commit -m "feat(trips): resolve-place + trip place pool CRUD endpoints"
```

---

## Task 8: `IRouteService` (Routes API + Haversine fallback) + cache

> **Maps grounding gate:** fetch the **Routes API** sub-skill; confirm the
> `computeRouteMatrix` request/response shape + field mask before finalising the impl.

**Files:**
- Create: `backend/src/MenuNest.Application/Abstractions/IRouteService.cs` (+ `LegTime`, `RoutePoint`)
- Create: `backend/src/MenuNest.Infrastructure/Maps/GoogleRouteService.cs`, `HaversineRouteService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs`

**Interfaces:**
- Produces: `record RoutePoint(double Lat, double Lng);` `record LegTime(int Seconds, int Meters);`
  `IRouteService.GetLegTimesAsync(IReadOnlyList<RoutePoint> orderedPoints, TravelMode mode, CancellationToken ct) -> Task<IReadOnlyList<LegTime>>` (returns `points.Count - 1` legs; leg `i` = point `i`→`i+1`).

- [ ] **Step 1: Write abstraction**

```csharp
// backend/src/MenuNest.Application/Abstractions/IRouteService.cs
using MenuNest.Domain.Enums;
namespace MenuNest.Application.Abstractions;

public sealed record RoutePoint(double Lat, double Lng);
public sealed record LegTime(int Seconds, int Meters);

public interface IRouteService
{
    /// <summary>Travel time/distance for each consecutive leg of an ordered point list.</summary>
    Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> orderedPoints, TravelMode mode, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing Haversine test**

```csharp
// HaversineRouteServiceTests.cs
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class HaversineRouteServiceTests
{
    [Fact]
    public async Task Returns_one_leg_fewer_than_points_with_positive_times()
    {
        var svc = new HaversineRouteService();
        var pts = new List<RoutePoint> { new(18.80, 98.92), new(18.79, 98.99), new(18.77, 99.00) };
        var legs = await svc.GetLegTimesAsync(pts, TravelMode.Drive, CancellationToken.None);
        legs.Should().HaveCount(2);
        legs[0].Meters.Should().BeGreaterThan(0);
        legs[0].Seconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Empty_or_single_point_returns_no_legs()
    {
        var svc = new HaversineRouteService();
        (await svc.GetLegTimesAsync(new List<RoutePoint> { new(0, 0) }, TravelMode.Walk, default)).Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run → FAIL.**

- [ ] **Step 4: Implement `HaversineRouteService` (fallback) and `GoogleRouteService` (primary)**

```csharp
// HaversineRouteService.cs
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
namespace MenuNest.Infrastructure.Maps;

/// <summary>Key-free fallback: great-circle distance × 1.3 road factor ÷ mode speed.</summary>
public sealed class HaversineRouteService : IRouteService
{
    private const double RoadFactor = 1.3;
    private static double SpeedMps(TravelMode m) => m switch
    { TravelMode.Walk => 1.4, TravelMode.Transit => 8.3, _ => 11.1 }; // ~5 / 30 / 40 km/h

    public Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> pts, TravelMode mode, CancellationToken ct)
    {
        var legs = new List<LegTime>();
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var meters = Haversine(pts[i], pts[i + 1]) * RoadFactor;
            legs.Add(new LegTime((int)Math.Round(meters / SpeedMps(mode)), (int)Math.Round(meters)));
        }
        return Task.FromResult<IReadOnlyList<LegTime>>(legs);
    }

    private static double Haversine(RoutePoint a, RoutePoint b)
    {
        const double R = 6_371_000;
        double dLat = Deg(b.Lat - a.Lat), dLng = Deg(b.Lng - a.Lng);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(Deg(a.Lat)) * Math.Cos(Deg(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
    private static double Deg(double d) => d * Math.PI / 180.0;
}
```
```csharp
// GoogleRouteService.cs — decorates Haversine: try Routes API computeRouteMatrix, cache per leg, fall back on error.
using System.Net.Http.Json;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace MenuNest.Infrastructure.Maps;

public sealed class GoogleRouteService : IRouteService
{
    private readonly IHttpClientFactory _http;
    private readonly GoogleMapsOptions _opts;
    private readonly IMemoryCache _cache;
    private readonly HaversineRouteService _fallback = new();
    private readonly ILogger<GoogleRouteService> _log;

    public GoogleRouteService(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts, IMemoryCache cache, ILogger<GoogleRouteService> log)
    { _http = http; _opts = opts.Value; _cache = cache; _log = log; }

    public async Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> pts, TravelMode mode, CancellationToken ct)
    {
        if (pts.Count < 2) return Array.Empty<LegTime>();
        var result = new LegTime[pts.Count - 1];
        var misses = new List<int>();
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            if (_cache.TryGetValue(Key(pts[i], pts[i + 1], mode), out LegTime? hit) && hit is not null) result[i] = hit;
            else misses.Add(i);
        }
        if (misses.Count == 0) return result;

        try
        {
            // computeRouteMatrix over the missing (origin,dest) pairs — see Routes sub-skill for exact shape.
            foreach (var i in misses)
            {
                var leg = await ComputeOneAsync(pts[i], pts[i + 1], mode, ct);
                _cache.Set(Key(pts[i], pts[i + 1], mode), leg, TimeSpan.FromHours(12));
                result[i] = leg;
            }
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Routes API failed; using Haversine fallback.");
            var fb = await _fallback.GetLegTimesAsync(pts, mode, ct);
            for (var i = 0; i < result.Length; i++) result[i] ??= fb[i];
            return result;
        }
    }

    private async Task<LegTime> ComputeOneAsync(RoutePoint o, RoutePoint d, TravelMode mode, CancellationToken ct)
    {
        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/distanceMatrix/v2:computeRouteMatrix");
        req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
        req.Headers.Add("X-Goog-FieldMask", "originIndex,destinationIndex,duration,distanceMeters,condition");
        req.Content = JsonContent.Create(new
        {
            origins = new[] { Wp(o) },
            destinations = new[] { Wp(d) },
            travelMode = mode switch { TravelMode.Walk => "WALK", TravelMode.Transit => "TRANSIT", _ => "DRIVE" },
        });
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var el = doc.RootElement.EnumerateArray().First();
        var seconds = ParseDuration(el.GetProperty("duration").GetString());
        var meters = el.TryGetProperty("distanceMeters", out var m) ? m.GetInt32() : 0;
        return new LegTime(seconds, meters);

        static object Wp(RoutePoint p) => new { waypoint = new { location = new { latLng = new { latitude = p.Lat, longitude = p.Lng } } } };
        static int ParseDuration(string? s) => int.TryParse(s?.TrimEnd('s'), out var v) ? v : 0;
    }

    private static string Key(RoutePoint o, RoutePoint d, TravelMode mode)
        => $"leg:{o.Lat:F5},{o.Lng:F5}->{d.Lat:F5},{d.Lng:F5}:{mode}";
}
```

- [ ] **Step 5: DI registration (conditional, + memory cache)**

```csharp
services.AddMemoryCache();
if (!string.IsNullOrWhiteSpace(mapsKey))
    services.AddScoped<IRouteService, GoogleRouteService>();
else
    services.AddScoped<IRouteService, HaversineRouteService>();
```

- [ ] **Step 6: Run tests → PASS; build.**

- [ ] **Step 7: Run `compliance-review`, then commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/IRouteService.cs backend/src/MenuNest.Infrastructure/Maps backend/src/MenuNest.Infrastructure/DependencyInjection.cs backend/tests/MenuNest.Application.UnitTests/Trips/Maps/HaversineRouteServiceTests.cs
git commit -m "feat(trips): IRouteService (Routes API + Haversine fallback) with per-leg cache"
```

---

## Task 9: Itinerary use cases + endpoints

**Files:**
- Create: `…/UseCases/Trips/GetItinerary/{GetItineraryQuery,GetItineraryHandler}.cs`
- Create: `…/UseCases/Trips/AddStop/{AddStopCommand,AddStopValidator,AddStopHandler}.cs`
- Create: `…/UseCases/Trips/UpdateStop/{…}.cs`, `…/Trips/RemoveStop/{…}.cs`, `…/Trips/ReorderStops/{…}.cs`, `…/Trips/SetDayStartTime/{…}.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs`, `ReorderStopsHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext` (Trips/ItineraryDays/Stops/TripPlaces), `IRouteService`, `IUserProvisioner`.
- Produces: `GetItineraryQuery(Guid TripId) : IQuery<IReadOnlyList<ItineraryDayDto>>`; `AddStopCommand(Guid TripId, Guid DayId, Guid TripPlaceId, int DwellMinutes, TravelMode TravelModeToReach) : ICommand<StopDto>`; `UpdateStopCommand(Guid TripId, Guid StopId, int? DwellMinutes, TravelMode? TravelModeToReach) : ICommand<Unit>`; `RemoveStopCommand(Guid TripId, Guid StopId) : ICommand<Unit>`; `ReorderStopsCommand(Guid TripId, Guid DayId, IReadOnlyList<Guid> OrderedStopIds) : ICommand<Unit>`; `SetDayStartTimeCommand(Guid TripId, Guid DayId, TimeOnly StartTime) : ICommand<Unit>`.

- [ ] **Step 1: Write failing tests**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetItineraryHandlerTests
{
    [Fact]
    public async Task Returns_days_with_ordered_stops_and_leg_times_for_non_first_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p1 = TripPlace.Create(trip.Id, "A", 18.80, 98.92, PlaceCategory.See);
        var p2 = TripPlace.Create(trip.Id, "B", 18.79, 98.99, PlaceCategory.Eat);
        fx.Db.TripPlaces.AddRange(p1, p2);
        fx.Db.Stops.Add(Stop.Create(day.Id, p1.Id, 0, 60, TravelMode.Drive));
        fx.Db.Stops.Add(Stop.Create(day.Id, p2.Id, 1, 45, TravelMode.Drive));
        await fx.Db.SaveChangesAsync();

        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegTime> { new(900, 4200) }); // one leg for two points

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        days.Should().HaveCount(1);
        days[0].Stops.Should().HaveCount(2);
        days[0].Stops[0].LegToReach.Should().BeNull();           // first stop: no leg
        days[0].Stops[1].LegToReach!.Seconds.Should().Be(900);   // second stop: leg from first
    }
}
```

```csharp
// backend/tests/MenuNest.Application.UnitTests/Trips/ReorderStopsHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class ReorderStopsHandlerTests
{
    [Fact]
    public async Task Reorders_sequences_to_match_supplied_order()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.See);
        var q = TripPlace.Create(trip.Id, "B", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.AddRange(p, q);
        var s0 = Stop.Create(day.Id, p.Id, 0, 60, TravelMode.Drive);
        var s1 = Stop.Create(day.Id, q.Id, 1, 60, TravelMode.Drive);
        fx.Db.Stops.AddRange(s0, s1);
        await fx.Db.SaveChangesAsync();

        await new ReorderStopsHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ReorderStopsCommand(trip.Id, day.Id, new[] { s1.Id, s0.Id }), CancellationToken.None);

        var ordered = await fx.Db.Stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence).ToListAsync();
        ordered[0].Id.Should().Be(s1.Id);
        ordered[1].Id.Should().Be(s0.Id);
    }
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement `GetItinerary` (loads days+stops+places, asks `IRouteService` per day)**

```csharp
// GetItineraryQuery.cs
using Mediator;
namespace MenuNest.Application.UseCases.Trips.GetItinerary;
public sealed record GetItineraryQuery(Guid TripId) : IQuery<IReadOnlyList<ItineraryDayDto>>;
```
```csharp
// GetItineraryHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.GetItinerary;

public sealed class GetItineraryHandler : IQueryHandler<GetItineraryQuery, IReadOnlyList<ItineraryDayDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IRouteService _routes;
    public GetItineraryHandler(IApplicationDbContext db, IUserProvisioner users, IRouteService routes)
    { _db = db; _users = users; _routes = routes; }

    public async ValueTask<IReadOnlyList<ItineraryDayDto>> Handle(GetItineraryQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == q.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);
        var stops = await _db.Stops
            .Where(s => _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == trip.Id))
            .OrderBy(s => s.Sequence).ToListAsync(ct);
        var placeIds = stops.Select(s => s.TripPlaceId).Distinct().ToList();
        var places = await _db.TripPlaces.Where(p => placeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var result = new List<ItineraryDayDto>(days.Count);
        foreach (var day in days)
        {
            var dayStops = stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence).ToList();
            var points = dayStops.Select(s => new RoutePoint(places[s.TripPlaceId].Lat, places[s.TripPlaceId].Lng)).ToList();
            var legs = dayStops.Count > 1
                ? await _routes.GetLegTimesAsync(points, trip.DefaultTravelMode, ct)
                : Array.Empty<LegTime>();

            var stopDtos = new List<StopDto>(dayStops.Count);
            for (var i = 0; i < dayStops.Count; i++)
            {
                var s = dayStops[i];
                LegDto? leg = i == 0 ? null : new LegDto(legs[i - 1].Seconds, legs[i - 1].Meters);
                stopDtos.Add(new StopDto(s.Id, s.TripPlaceId, s.Sequence, s.DwellMinutes, s.TravelModeToReach, leg));
            }
            result.Add(new ItineraryDayDto(day.Id, day.Date, day.DayStartTime, stopDtos));
        }
        return result;
    }
}
```

- [ ] **Step 4: Implement `AddStop`, `UpdateStop`, `RemoveStop`, `ReorderStops`, `SetDayStartTime`**

All verify ownership: provision user → `_db.Trips.AnyAsync(t => t.Id == TripId && t.UserId == user.Id && t.DeletedAt == null)` else `DomainException("Trip not found.")`; day/stop ops also verify the day/stop belongs to that trip.

```csharp
// AddStopHandler.cs (core) — append at end of day (max sequence + 1) by default
public async ValueTask<StopDto> Handle(AddStopCommand c, CancellationToken ct)
{
    await _validator.ValidateAndThrowAsync(c, ct);
    var user = await _users.GetOrProvisionCurrentAsync(ct);
    var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId
        && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct)
        ?? throw new DomainException("Itinerary day not found.");
    var placeOk = await _db.TripPlaces.AnyAsync(p => p.Id == c.TripPlaceId && p.TripId == c.TripId, ct);
    if (!placeOk) throw new DomainException("Place not found in this trip.");

    var nextSeq = await _db.Stops.Where(s => s.ItineraryDayId == day.Id).CountAsync(ct);
    var stop = Stop.Create(day.Id, c.TripPlaceId, nextSeq, c.DwellMinutes, c.TravelModeToReach);
    _db.Stops.Add(stop);
    await _db.SaveChangesAsync(ct);
    return new StopDto(stop.Id, stop.TripPlaceId, stop.Sequence, stop.DwellMinutes, stop.TravelModeToReach, null);
}
```
```csharp
// ReorderStopsHandler.cs (core)
public async ValueTask<Unit> Handle(ReorderStopsCommand c, CancellationToken ct)
{
    var user = await _users.GetOrProvisionCurrentAsync(ct);
    var ownsDay = await _db.ItineraryDays.AnyAsync(d => d.Id == c.DayId
        && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct);
    if (!ownsDay) throw new DomainException("Itinerary day not found.");

    var stops = await _db.Stops.Where(s => s.ItineraryDayId == c.DayId).ToListAsync(ct);
    for (var i = 0; i < c.OrderedStopIds.Count; i++)
    {
        var stop = stops.FirstOrDefault(s => s.Id == c.OrderedStopIds[i])
            ?? throw new DomainException("Stop does not belong to this day.");
        stop.SetSequence(i);
    }
    await _db.SaveChangesAsync(ct);
    return Unit.Value;
}
```
`UpdateStopHandler`: load the stop (joined to the owned trip), call `stop.SetDwell(c.DwellMinutes.Value)` and/or `stop.SetTravelMode(c.TravelModeToReach.Value)` when provided, save. `RemoveStopHandler`: load + `_db.Stops.Remove(stop)` + resequence the remaining stops on that day (re-number `0..n` by current `Sequence`), save. `SetDayStartTimeHandler`: load the day (owned) + `day.SetStartTime(c.StartTime)` + save.

- [ ] **Step 5: Add itinerary endpoints to `TripsController`**

```csharp
    [HttpGet("api/trips/{id:guid}/itinerary")]
    public async Task<ActionResult<IReadOnlyList<ItineraryDayDto>>> GetItinerary(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetItineraryQuery(id), ct));

    [HttpPost("api/trips/{id:guid}/days/{dayId:guid}/stops")]
    public async Task<ActionResult<StopDto>> AddStop(Guid id, Guid dayId, [FromBody] AddStopBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AddStopCommand(id, dayId, b.TripPlaceId, b.DwellMinutes, b.TravelModeToReach), ct));

    [HttpPatch("api/trips/{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> UpdateStop(Guid id, Guid stopId, [FromBody] UpdateStopBody b, CancellationToken ct)
    { await _mediator.Send(new UpdateStopCommand(id, stopId, b.DwellMinutes, b.TravelModeToReach), ct); return NoContent(); }

    [HttpDelete("api/trips/{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> RemoveStop(Guid id, Guid stopId, CancellationToken ct)
    { await _mediator.Send(new RemoveStopCommand(id, stopId), ct); return NoContent(); }

    [HttpPost("api/trips/{id:guid}/days/{dayId:guid}/reorder")]
    public async Task<IActionResult> Reorder(Guid id, Guid dayId, [FromBody] ReorderBody b, CancellationToken ct)
    { await _mediator.Send(new ReorderStopsCommand(id, dayId, b.OrderedStopIds), ct); return NoContent(); }

    [HttpPatch("api/trips/{id:guid}/days/{dayId:guid}")]
    public async Task<IActionResult> SetDayStart(Guid id, Guid dayId, [FromBody] SetDayStartBody b, CancellationToken ct)
    { await _mediator.Send(new SetDayStartTimeCommand(id, dayId, b.StartTime), ct); return NoContent(); }
```
Add the body records (`AddStopBody`, `UpdateStopBody`, `ReorderBody`, `SetDayStartBody`).

- [ ] **Step 6: Run tests + build** → PASS; `dotnet build backend/src/MenuNest.WebApi`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/tests/MenuNest.Application.UnitTests/Trips
git commit -m "feat(trips): itinerary use cases (get with leg times, add/update/remove/reorder stops, day start)"
```

---

## Task 10: Frontend deps + font + RTK Query Trips section

**Files:**
- Modify: `frontend/package.json` (add `@vis.gl/react-google-maps`), `frontend/index.html` (Spline Sans Mono), `frontend/src/shared/api/api.ts` (tagTypes + Trips endpoints + types)

**Interfaces:**
- Produces (RTK hooks): `useListTripsQuery`, `useGetTripQuery`/`useGetItineraryQuery`, `useCreateTripMutation`, `useUpdateTripMutation`, `useDeleteTripMutation`, `useResolvePlaceMutation`, `useListTripPlacesQuery`, `useAddTripPlaceMutation`, `useUpdateTripPlaceMutation`, `useDeleteTripPlaceMutation`, `useAddStopMutation`, `useUpdateStopMutation`, `useRemoveStopMutation`, `useReorderStopsMutation`, `useSetDayStartTimeMutation`. Types: `TripDto, TripPlaceDto, StopDto, LegDto, ItineraryDayDto, ResolvedPlaceDto, PlaceCategory, TravelMode`.

- [ ] **Step 1: Install the map library + verify the Syncfusion date/tab packages**

Run: `cd frontend && npm install @vis.gl/react-google-maps`
Then verify `@syncfusion/react-calendars` (DatePicker/TimePicker) and `@syncfusion/react-navigations` (Tab) are installed (their CSS is already imported in `main.tsx`): `npm ls @syncfusion/react-calendars @syncfusion/react-navigations`. If either is missing, `npm install @syncfusion/react-calendars @syncfusion/react-navigations` (match the existing `@syncfusion/react-*` version, currently `^33.1.44`).
Expected: all three resolve; `npm run build` still succeeds. (Per the resolved UI decisions, date/time and tab strips use these — not raw `<input>` / hand-rolled tabs.)

- [ ] **Step 2: Add Spline Sans Mono font**

In `frontend/index.html` `<head>`, add (Noto Sans Thai is already loaded the same way — match the existing `<link>` style):
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Spline+Sans+Mono:wght@500;700&display=swap" rel="stylesheet">
```

- [ ] **Step 3: Add Trips types + tagTypes + endpoints to `api.ts`**

In `frontend/src/shared/api/api.ts`: add to the `tagTypes` array: `'Trips', 'TripDetail', 'TripPlaces', 'TripItinerary'`. Add a new endpoints section (mirror the Budget section's builder syntax):

```typescript
// -------------------- Trips --------------------
export type TravelMode = 'Drive' | 'Walk' | 'Transit'
export type PlaceCategory = 'Stay' | 'Eat' | 'See' | 'Cafe' | 'Shop' | 'Other'

export interface TripDto { id: string; name: string; destination: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode }
export interface TripPlaceDto {
  id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number
  address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null
  bestTimeStart: string | null; bestTimeEnd: string | null; openingHoursJson: string | null
  feeNote: string | null; notes: string | null
}
export interface LegDto { seconds: number; meters: number }
export interface StopDto { id: string; tripPlaceId: string; sequence: number; dwellMinutes: number; travelModeToReach: TravelMode; legToReach: LegDto | null }
export interface ItineraryDayDto { id: string; date: string; dayStartTime: string; stops: StopDto[] }
export interface ResolvedPlaceDto { googlePlaceId: string | null; name: string; lat: number; lng: number; address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null; openingHoursJson: string | null }

// inside the endpoints builder object:
listTrips: build.query<TripDto[], void>({
  query: () => '/api/trips',
  providesTags: ['Trips'],
}),
createTrip: build.mutation<TripDto, {name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode}>({
  query: (b) => ({url: '/api/trips', method: 'POST', body: b}),
  invalidatesTags: ['Trips'],
}),
updateTrip: build.mutation<TripDto, {id: string; name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode}>({
  query: ({id, ...b}) => ({url: `/api/trips/${id}`, method: 'PUT', body: b}),
  invalidatesTags: (_r, _e, a) => ['Trips', {type: 'TripDetail', id: a.id}, {type: 'TripItinerary', id: a.id}],
}),
deleteTrip: build.mutation<void, string>({
  query: (id) => ({url: `/api/trips/${id}`, method: 'DELETE'}),
  invalidatesTags: ['Trips'],
}),
resolvePlace: build.mutation<ResolvedPlaceDto, {url: string}>({
  query: (b) => ({url: '/api/trips/resolve-place', method: 'POST', body: b}),
}),
listTripPlaces: build.query<TripPlaceDto[], string>({
  query: (tripId) => `/api/trips/${tripId}/places`,
  providesTags: (_r, _e, id) => [{type: 'TripPlaces', id}],
}),
addTripPlace: build.mutation<TripPlaceDto, {tripId: string} & Omit<TripPlaceDto, 'id' | 'tripId' | 'bestTimeStart' | 'bestTimeEnd' | 'feeNote' | 'notes'>>({
  query: ({tripId, ...b}) => ({url: `/api/trips/${tripId}/places`, method: 'POST', body: b}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
}),
updateTripPlace: build.mutation<TripPlaceDto, {tripId: string; placeId: string; name: string; category: PlaceCategory; address?: string | null; feeNote?: string | null; notes?: string | null; bestTimeStart?: string | null; bestTimeEnd?: string | null}>({
  query: ({tripId, placeId, ...b}) => ({url: `/api/trips/${tripId}/places/${placeId}`, method: 'PUT', body: b}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
}),
deleteTripPlace: build.mutation<void, {tripId: string; placeId: string}>({
  query: ({tripId, placeId}) => ({url: `/api/trips/${tripId}/places/${placeId}`, method: 'DELETE'}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
}),
getItinerary: build.query<ItineraryDayDto[], string>({
  query: (tripId) => `/api/trips/${tripId}/itinerary`,
  providesTags: (_r, _e, id) => [{type: 'TripItinerary', id}],
}),
addStop: build.mutation<StopDto, {tripId: string; dayId: string; tripPlaceId: string; dwellMinutes: number; travelModeToReach: TravelMode}>({
  query: ({tripId, dayId, ...b}) => ({url: `/api/trips/${tripId}/days/${dayId}/stops`, method: 'POST', body: b}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
updateStop: build.mutation<void, {tripId: string; stopId: string; dwellMinutes?: number | null; travelModeToReach?: TravelMode | null}>({
  query: ({tripId, stopId, ...b}) => ({url: `/api/trips/${tripId}/stops/${stopId}`, method: 'PATCH', body: b}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
removeStop: build.mutation<void, {tripId: string; stopId: string}>({
  query: ({tripId, stopId}) => ({url: `/api/trips/${tripId}/stops/${stopId}`, method: 'DELETE'}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
reorderStops: build.mutation<void, {tripId: string; dayId: string; orderedStopIds: string[]}>({
  query: ({tripId, dayId, orderedStopIds}) => ({url: `/api/trips/${tripId}/days/${dayId}/reorder`, method: 'POST', body: {orderedStopIds}}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
setDayStartTime: build.mutation<void, {tripId: string; dayId: string; startTime: string}>({
  query: ({tripId, dayId, startTime}) => ({url: `/api/trips/${tripId}/days/${dayId}`, method: 'PATCH', body: {startTime}}),
  invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
```

- [ ] **Step 4: Verify the hooks generate**

Run: `cd frontend && npm run build`
Expected: TypeScript compiles; the `useXxx` hooks are exported from `api.ts`.

- [ ] **Step 5: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/index.html frontend/src/shared/api/api.ts
git commit -m "feat(trips): add @vis.gl/react-google-maps, Spline Sans Mono, Trips RTK endpoints"
```

---

## Task 11: Slice + store + router + nav + barrel

**Files:**
- Create: `frontend/src/pages/trips/tripsSlice.ts`, `frontend/src/pages/trips/index.ts`
- Modify: `frontend/src/store/index.ts`, `frontend/src/router.tsx`, `frontend/src/shared/components/NavBar.tsx`

**Interfaces:**
- Produces: `tripsSlice` (default export reducer) with actions `setActiveDay`, `setActiveTab`, `setPlacesView`, `setPlaceCategoryFilter`, `setActiveStop`, dialog flags. Routes `/trips`, `/trips/:tripId`. Nav item `🧳 Trips`.

- [ ] **Step 1: Write `tripsSlice.ts`**

```typescript
// frontend/src/pages/trips/tripsSlice.ts
import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'
import type {PlaceCategory} from '../../shared/api/api'

export type TripTab = 'places' | 'itinerary'
export type PlacesView = 'map' | 'list'

interface TripsState {
  activeDayId: string | null
  activeTab: TripTab
  placesView: PlacesView
  placeCategoryFilter: PlaceCategory | 'all'
  activeStopId: string | null
  createTripOpen: boolean
  addPlaceOpen: boolean
  stopEditorStopId: string | null
}

const initialState: TripsState = {
  activeDayId: null, activeTab: 'itinerary', placesView: 'map',
  placeCategoryFilter: 'all', activeStopId: null,
  createTripOpen: false, addPlaceOpen: false, stopEditorStopId: null,
}

const tripsSlice = createSlice({
  name: 'trips',
  initialState,
  reducers: {
    setActiveDay(s, a: PayloadAction<string | null>) { s.activeDayId = a.payload },
    setActiveTab(s, a: PayloadAction<TripTab>) { s.activeTab = a.payload },
    setPlacesView(s, a: PayloadAction<PlacesView>) { s.placesView = a.payload },
    setPlaceCategoryFilter(s, a: PayloadAction<PlaceCategory | 'all'>) { s.placeCategoryFilter = a.payload },
    setActiveStop(s, a: PayloadAction<string | null>) { s.activeStopId = a.payload },
    setCreateTripOpen(s, a: PayloadAction<boolean>) { s.createTripOpen = a.payload },
    setAddPlaceOpen(s, a: PayloadAction<boolean>) { s.addPlaceOpen = a.payload },
    setStopEditor(s, a: PayloadAction<string | null>) { s.stopEditorStopId = a.payload },
  },
})

export const {
  setActiveDay, setActiveTab, setPlacesView, setPlaceCategoryFilter,
  setActiveStop, setCreateTripOpen, setAddPlaceOpen, setStopEditor,
} = tripsSlice.actions
export default tripsSlice.reducer
```

- [ ] **Step 2: Register in `store/index.ts`**

Add the import and the reducer entry after `budget`:
```typescript
import tripsSlice from '../pages/trips/tripsSlice'
// ...
    budget: budgetSlice,
    trips: tripsSlice,
```

- [ ] **Step 3: Add routes (under `AppLayout`, NOT `FamilyRequiredRoute`)**

In `frontend/src/router.tsx`, in the same `AppLayout` children array where `/health` and `/pomodoro` live (outside `FamilyRequiredRoute`), add:
```tsx
{ path: '/trips', element: <TripsPage /> },
{ path: '/trips/:tripId', element: <TripDetailPage /> },
```
Add the imports `import {TripsPage, TripDetailPage} from './pages/trips'` (lazy-load to match the file's convention if other routes are lazy).

- [ ] **Step 4: Add nav item**

In `frontend/src/shared/components/NavBar.tsx`, add to `navItems` after the Pomodoro entry:
```typescript
{ to: '/trips', label: '🧳 Trips' },
```

- [ ] **Step 5: Barrel**

```typescript
// frontend/src/pages/trips/index.ts
export {TripsPage} from './TripsPage'
export {TripDetailPage} from './TripDetailPage'
```

> `TripsPage`/`TripDetailPage` are created in Tasks 12–13; this barrel + the router import will fail to compile until then. Implement Task 12 immediately after, or temporarily stub both as `export function TripsPage(){return null}` to keep the build green between commits.

- [ ] **Step 6: Build (with stubs) + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/trips frontend/src/store/index.ts frontend/src/router.tsx frontend/src/shared/components/NavBar.tsx
git commit -m "feat(trips): slice + store + routes (user-scoped) + nav item"
```

---

## Task 12: `TripsPage` (trip list + create dialog)

**Files:**
- Create: `frontend/src/pages/trips/TripsPage.tsx`, `TripsPage.css`, `components/CreateTripDialog.tsx`

**Interfaces:**
- Consumes: `useListTripsQuery`, `useCreateTripMutation`, `tripsSlice.setCreateTripOpen`.

- [ ] **Step 1: Write `TripsPage.tsx`** (list of trip cards + FAB → create dialog; loading/empty/error states)

```tsx
// frontend/src/pages/trips/TripsPage.tsx
import {useNavigate} from 'react-router-dom'
import {Button} from '@syncfusion/react-buttons'
import {useListTripsQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/hooks'
import {setCreateTripOpen} from './tripsSlice'
import {CreateTripDialog} from './components/CreateTripDialog'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import './TripsPage.css'

export function TripsPage() {
  const nav = useNavigate()
  const dispatch = useAppDispatch()
  const open = useAppSelector(s => s.trips.createTripOpen)
  const {data: trips, isLoading, error} = useListTripsQuery()

  return (
    <section className="trips-page">
      <header className="trips-header">
        <h1>🧳 ทริปของฉัน</h1>
        <Button color="Primary" onClick={() => dispatch(setCreateTripOpen(true))}>+ ทริปใหม่</Button>
      </header>

      {isLoading && <p className="trips-muted">กำลังโหลด…</p>}
      {error && <p className="field-error">{getErrorMessage(error)}</p>}
      {trips?.length === 0 && <p className="trips-empty">ยังไม่มีทริป — สร้างทริปแรกของคุณ</p>}

      <div className="trips-grid">
        {trips?.map(t => (
          <button key={t.id} className="trip-card" onClick={() => nav(`/trips/${t.id}`)}>
            <div className="trip-card-name">{t.name}</div>
            <div className="trip-card-meta">{t.destination ?? ''} · {t.dayCount} วัน</div>
            <div className="trip-card-dates">{t.startDate}</div>
          </button>
        ))}
      </div>

      {open && <CreateTripDialog onClose={() => dispatch(setCreateTripOpen(false))} onCreated={(id) => { dispatch(setCreateTripOpen(false)); nav(`/trips/${id}`) }} />}
    </section>
  )
}
```

- [ ] **Step 2: Write `CreateTripDialog.tsx`** (react-hook-form + Syncfusion `TextBox`/`NumericTextBox`/`DropDownList` in `Dialog`)

```tsx
// frontend/src/pages/trips/components/CreateTripDialog.tsx
import {Controller, useForm} from 'react-hook-form'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox, NumericTextBox} from '@syncfusion/react-inputs'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {Button} from '@syncfusion/react-buttons'
import {useCreateTripMutation, type TravelMode} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues { name: string; destination: string; startDate: string; dayCount: number; defaultTravelMode: TravelMode }
const MODES = [{label: 'รถยนต์', value: 'Drive'}, {label: 'เดิน', value: 'Walk'}, {label: 'ขนส่งสาธารณะ', value: 'Transit'}]

export function CreateTripDialog({onClose, onCreated}: {onClose: () => void; onCreated: (id: string) => void}) {
  const {control, handleSubmit, formState} = useForm<FormValues>({
    defaultValues: {name: '', destination: '', startDate: new Date().toISOString().slice(0, 10), dayCount: 3, defaultTravelMode: 'Drive'},
  })
  const [createTrip, {isLoading, error}] = useCreateTripMutation()

  const submit = handleSubmit(async (v) => {
    const t = await createTrip({name: v.name, destination: v.destination || null, startDate: v.startDate, dayCount: v.dayCount, defaultTravelMode: v.defaultTravelMode}).unwrap()
    onCreated(t.id)
  })

  return (
    <Dialog open onClose={onClose} modal header="สร้างทริปใหม่" style={{width: '420px'}}>
      <form onSubmit={submit} noValidate className="trip-form">
        <label>ชื่อทริป <span className="field-required">*</span></label>
        <Controller control={control} name="name" rules={{required: 'กรุณากรอกชื่อทริป'}}
          render={({field, fieldState}) => (<>
            <TextBox value={field.value} onChange={(e: {value: string}) => field.onChange(e.value)} placeholder="เช่น เชียงใหม่ 3 วัน" />
            {fieldState.error && <p className="field-error">{fieldState.error.message}</p>}
          </>)} />

        <label>ปลายทาง</label>
        <Controller control={control} name="destination"
          render={({field}) => <TextBox value={field.value} onChange={(e: {value: string}) => field.onChange(e.value)} placeholder="Chiang Mai" />} />

        <label>วันเริ่ม <span className="field-required">*</span></label>
        <Controller control={control} name="startDate" rules={{required: 'เลือกวันเริ่ม'}}
          render={({field}) => <input type="date" value={field.value} onChange={(e) => field.onChange(e.target.value)} />} />

        <label>จำนวนวัน <span className="field-required">*</span></label>
        <Controller control={control} name="dayCount" rules={{required: true, min: 1, max: 60}}
          render={({field}) => <NumericTextBox value={field.value} min={1} max={60} onChange={(e: {value: number}) => field.onChange(e.value)} />} />

        <label>การเดินทางหลัก</label>
        <Controller control={control} name="defaultTravelMode"
          render={({field}) => <DropDownList dataSource={MODES} fields={{text: 'label', value: 'value'}} value={field.value} onChange={(e: {value: unknown}) => field.onChange(e.value as TravelMode)} />} />

        {error && <p className="field-error">{getErrorMessage(error)}</p>}
        <div className="trip-form-actions">
          <Button type="button" onClick={onClose}>ยกเลิก</Button>
          <Button type="submit" color="Primary" disabled={isLoading || !formState.isValid}>สร้าง</Button>
        </div>
      </form>
    </Dialog>
  )
}
```

> Verify the exact `TextBox`/`NumericTextBox`/`DropDownList`/`Button`/`Dialog` prop names against the installed `@syncfusion/react-*` `.d.ts` before finalising (the `onChange` event payload shape differs across versions). **Per the resolved UI decisions, replace `<input type="date">` with the Syncfusion `DatePicker` from `@syncfusion/react-calendars`** (bridged through the same `Controller`); confirm its value/onChange prop names in the `.d.ts`.

- [ ] **Step 3: Write `TripsPage.css`** (teal tokens scoped to `.trips-page`; cards grid). Define the teal CSS variables here (see Task 19) or import the shared tokens file.

- [ ] **Step 4: Build + manual check** — `npm run build`; run the app, `/trips` lists trips, "+ ทริปใหม่" creates one and navigates to it.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/TripsPage.tsx frontend/src/pages/trips/TripsPage.css frontend/src/pages/trips/components/CreateTripDialog.tsx
git commit -m "feat(trips): trips list page + create-trip dialog"
```

---

## Task 13: `TripDetailPage` shell — tabs + Places list view

**Files:**
- Create: `frontend/src/pages/trips/TripDetailPage.tsx`, `TripDetailPage.css`, `components/SegmentedTabs.tsx`, `components/PlaceCard.tsx`

**Interfaces:**
- Consumes: `useGetTripQuery` (or `useListTripsQuery` + find), `useListTripPlacesQuery`, `useGetItineraryQuery`, slice tab/view actions.
- Produces: `SegmentedTabs` (custom segmented control, mirrors Budget `.bdg-chip` precedent); `PlaceCard`.

- [ ] **Step 1: Write `SegmentedTabs.tsx`** — **per the resolved UI decisions, back it with the Syncfusion `Tab` (`@syncfusion/react-navigations`)** for the tab/day strips (controlled `value` + `onChange`, content rendered by the parent — drive Tab's `selected`/`selecting` to the parent callback; verify the exact API in the `.d.ts`). The custom-CSS segmented control below is acceptable **only** for the binary Map/List toggle if no Syncfusion equivalent is installed.

```tsx
// frontend/src/pages/trips/components/SegmentedTabs.tsx
export function SegmentedTabs<T extends string>({value, options, onChange}: {
  value: T; options: {label: string; value: T}[]; onChange: (v: T) => void
}) {
  return (
    <div className="seg-tabs" role="tablist">
      {options.map(o => (
        <button key={o.value} role="tab" aria-selected={o.value === value}
          className={`seg-tab${o.value === value ? ' active' : ''}`} onClick={() => onChange(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}
```

- [ ] **Step 2: Write `PlaceCard.tsx`** (category dot + name + category·price; click selects)

```tsx
// frontend/src/pages/trips/components/PlaceCard.tsx
import type {TripPlaceDto} from '../../../shared/api/api'

const CAT_COLOR: Record<string, string> = {Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76', Cafe: '#b4791f', Shop: '#c2418f', Other: '#94a3b8'}
const CAT_LABEL: Record<string, string> = {Stay: 'ที่พัก', Eat: 'ร้านอาหาร', See: 'ที่เที่ยว', Cafe: 'คาเฟ่', Shop: 'ช้อปปิ้ง', Other: 'อื่นๆ'}

export function PlaceCard({place, onClick}: {place: TripPlaceDto; onClick?: () => void}) {
  return (
    <button className="place-card" onClick={onClick}>
      <span className="place-dot" style={{background: CAT_COLOR[place.category]}} />
      <span className="place-body">
        <span className="place-name">{place.name}</span>
        <span className="place-sub">{CAT_LABEL[place.category]}{place.priceLevel != null ? ` · ${'฿'.repeat(Math.max(1, place.priceLevel))}` : ''}</span>
      </span>
    </button>
  )
}
```

- [ ] **Step 3: Write `TripDetailPage.tsx`** (tabs Places/Itinerary; Places tab = Map/List toggle + list; Add-place FAB)

```tsx
// frontend/src/pages/trips/TripDetailPage.tsx
import {useParams} from 'react-router-dom'
import {Button} from '@syncfusion/react-buttons'
import {useListTripsQuery, useListTripPlacesQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/hooks'
import {setActiveTab, setPlacesView, setAddPlaceOpen} from './tripsSlice'
import {SegmentedTabs} from './components/SegmentedTabs'
import {PlaceCard} from './components/PlaceCard'
import {AddPlaceSheet} from './components/AddPlaceSheet'
import {ItineraryTab} from './components/ItineraryTab'
import {TripMap} from './components/TripMap'
import './TripDetailPage.css'

export function TripDetailPage() {
  const {tripId = ''} = useParams()
  const dispatch = useAppDispatch()
  const tab = useAppSelector(s => s.trips.activeTab)
  const placesView = useAppSelector(s => s.trips.placesView)
  const addOpen = useAppSelector(s => s.trips.addPlaceOpen)
  const {data: trips} = useListTripsQuery()
  const trip = trips?.find(t => t.id === tripId)
  const {data: places} = useListTripPlacesQuery(tripId)

  return (
    <section className="trip-detail">
      <header className="trip-detail-header">
        <div className="trip-detail-name">{trip?.name ?? '…'}</div>
        <div className="trip-detail-meta">{trip?.destination} · {trip?.dayCount} วัน</div>
      </header>

      <SegmentedTabs value={tab} onChange={(v) => dispatch(setActiveTab(v))}
        options={[{label: 'คลังสถานที่', value: 'places'}, {label: 'แผนเที่ยว', value: 'itinerary'}]} />

      {tab === 'places' && (
        <div className="trip-places">
          <div className="trip-places-toolbar">
            <SegmentedTabs value={placesView} onChange={(v) => dispatch(setPlacesView(v))}
              options={[{label: 'แผนที่', value: 'map'}, {label: 'รายการ', value: 'list'}]} />
            <Button color="Primary" onClick={() => dispatch(setAddPlaceOpen(true))}>+ เพิ่มสถานที่</Button>
          </div>
          {placesView === 'map'
            ? <TripMap places={places ?? []} />
            : (places?.length ? <div className="place-list">{places.map(p => <PlaceCard key={p.id} place={p} />)}</div>
                              : <p className="trips-empty">ยังไม่มีสถานที่ — วางลิงก์จาก Google Maps เพื่อเริ่ม</p>)}
        </div>
      )}

      {tab === 'itinerary' && <ItineraryTab tripId={tripId} />}

      {addOpen && <AddPlaceSheet tripId={tripId} onClose={() => dispatch(setAddPlaceOpen(false))} />}
    </section>
  )
}
```

> `AddPlaceSheet`, `ItineraryTab`, `TripMap` are built in Tasks 14/16/18. Stub them as `() => null` to keep the build green, then implement.

- [ ] **Step 4: Write `TripDetailPage.css`** (segmented tabs `.seg-tabs/.seg-tab`, toolbar, place list). Pull teal tokens from Task 19.

- [ ] **Step 5: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/trips/TripDetailPage.tsx frontend/src/pages/trips/TripDetailPage.css frontend/src/pages/trips/components/SegmentedTabs.tsx frontend/src/pages/trips/components/PlaceCard.tsx
git commit -m "feat(trips): trip detail shell with tabs + places list view"
```

---

## Task 14: Add Place via paste-link (resolve → pre-fill → save)

**Files:**
- Create: `frontend/src/pages/trips/components/AddPlaceSheet.tsx`

**Interfaces:**
- Consumes: `useResolvePlaceMutation`, `useAddTripPlaceMutation`.

- [ ] **Step 1: Write `AddPlaceSheet.tsx`** (bottom sheet: paste link → "ดึงข้อมูล" → resolve → pre-filled name/coords/category picker → save)

```tsx
// frontend/src/pages/trips/components/AddPlaceSheet.tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {Button} from '@syncfusion/react-buttons'
import {useResolvePlaceMutation, useAddTripPlaceMutation, type ResolvedPlaceDto, type PlaceCategory} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

const CATS = [
  {label: '⛩️ ที่เที่ยว', value: 'See'}, {label: '🍜 ร้านอาหาร', value: 'Eat'},
  {label: '☕ คาเฟ่', value: 'Cafe'}, {label: '🛏️ ที่พัก', value: 'Stay'},
  {label: '🛍️ ช้อปปิ้ง', value: 'Shop'}, {label: '📍 อื่นๆ', value: 'Other'},
]

export function AddPlaceSheet({tripId, onClose}: {tripId: string; onClose: () => void}) {
  const [url, setUrl] = useState('')
  const [resolved, setResolved] = useState<ResolvedPlaceDto | null>(null)
  const [category, setCategory] = useState<PlaceCategory>('See')
  const [resolvePlace, {isLoading: resolving, error: resolveError}] = useResolvePlaceMutation()
  const [addPlace, {isLoading: saving, error: saveError}] = useAddTripPlaceMutation()

  const doResolve = async () => {
    const r = await resolvePlace({url}).unwrap()
    setResolved(r)
    setCategory(r.category)
  }
  const doSave = async () => {
    if (!resolved) return
    await addPlace({
      tripId, googlePlaceId: resolved.googlePlaceId, name: resolved.name, lat: resolved.lat, lng: resolved.lng,
      address: resolved.address, category, priceLevel: resolved.priceLevel, photoUrl: resolved.photoUrl,
      openingHoursJson: resolved.openingHoursJson,
    }).unwrap()
    onClose()
  }

  return (
    <Dialog open onClose={onClose} modal header="เพิ่มสถานที่จาก Google Maps" style={{width: '440px'}}>
      <div className="add-place-sheet">
        <label>วางลิงก์จาก Google Maps</label>
        <div className="add-place-row">
          <TextBox value={url} onChange={(e: {value: string}) => setUrl(e.value)} placeholder="https://maps.app.goo.gl/…" />
          <Button color="Primary" disabled={!url || resolving} onClick={doResolve}>{resolving ? 'กำลังดึง…' : 'ดึงข้อมูล'}</Button>
        </div>
        {resolveError && <p className="field-error">{getErrorMessage(resolveError)}</p>}

        {resolved && (
          <div className="add-place-preview">
            <div className="place-name">{resolved.name}</div>
            <div className="place-coords">{resolved.lat.toFixed(5)}, {resolved.lng.toFixed(5)}</div>
            {resolved.address && <div className="place-sub">{resolved.address}</div>}
            <label>หมวดหมู่</label>
            <DropDownList dataSource={CATS} fields={{text: 'label', value: 'value'}} value={category}
              onChange={(e: {value: unknown}) => setCategory(e.value as PlaceCategory)} />
            {saveError && <p className="field-error">{getErrorMessage(saveError)}</p>}
            <div className="trip-form-actions">
              <Button type="button" onClick={onClose}>ยกเลิก</Button>
              <Button color="Primary" disabled={saving} onClick={doSave}>บันทึกลงทริป</Button>
            </div>
          </div>
        )}
        <p className="add-place-hint">หรือ · วางลิงก์เอง (รองรับเฉพาะวางลิงก์ใน MVP — แชร์จากแอป/bookmarklet = Phase 2)</p>
      </div>
    </Dialog>
  )
}
```

- [ ] **Step 2: Build + manual check** — paste a real Google Maps share link (backend must have `GoogleMaps__ApiKey` set, dev = Demo Key) → name/coords appear → save → place shows in the list and on the map.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlaceSheet.tsx
git commit -m "feat(trips): add-place sheet (paste Google Maps link → resolve → save)"
```

---

## Task 15: `useSchedule` cascade hook (the calculation) — TDD

**Files:**
- Create: `frontend/src/pages/trips/hooks/useSchedule.ts`
- Test: `frontend/src/pages/trips/hooks/useSchedule.test.ts`

**Interfaces:**
- Produces: pure functions exported for testing —
  `computeSchedule(day: ItineraryDayDto): ScheduledStop[]` where
  `interface ScheduledStop { stop: StopDto; arrival: string /* HH:mm */; depart: string }`, and
  `flagStop(place: TripPlaceDto, arrival: string, depart: string): 'green' | 'amber'`.
  `useSchedule(day, placesById)` wraps them into `{ scheduled: (ScheduledStop & {flag})[]; dayEnd: string; totalTravelSeconds: number }`.

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/pages/trips/hooks/useSchedule.test.ts
import {describe, it, expect} from 'vitest'
import {computeSchedule, flagStop} from './useSchedule'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, legToReach: legSec == null ? null : {seconds: legSec, meters: 1000},
})

describe('computeSchedule', () => {
  it('cascades arrival = prev depart + leg; depart = arrival + dwell', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60), stop('3', 2, 90, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:00'); expect(s[0].depart).toBe('10:00')
    expect(s[1].arrival).toBe('10:25'); expect(s[1].depart).toBe('11:10')
    expect(s[2].arrival).toBe('11:40'); expect(s[2].depart).toBe('13:10')
  })
})

describe('flagStop', () => {
  const place = (bestStart: string | null, bestEnd: string | null, hoursJson: string | null): TripPlaceDto => ({
    id: 'p', tripId: 't', googlePlaceId: null, name: 'x', lat: 0, lng: 0, address: null,
    category: 'See', priceLevel: null, photoUrl: null, bestTimeStart: bestStart, bestTimeEnd: bestEnd,
    openingHoursJson: hoursJson, feeNote: null, notes: null,
  })

  it('green when arrival within best window', () => {
    expect(flagStop(place('08:00:00', '10:00:00', null), '09:00', '10:00')).toBe('green')
  })
  it('amber when arrival before best window', () => {
    expect(flagStop(place('17:30:00', '18:30:00', null), '13:50', '15:20')).toBe('amber')
  })
  it('green when no best window set (nothing to flag against)', () => {
    expect(flagStop(place(null, null, null), '13:50', '15:20')).toBe('green')
  })
})
```

- [ ] **Step 2: Run → FAIL**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `useSchedule.ts`**

```ts
// frontend/src/pages/trips/hooks/useSchedule.ts
import {useMemo} from 'react'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'

export interface ScheduledStop { stop: StopDto; arrival: string; depart: string }
export type StopFlag = 'green' | 'amber'

const toMin = (hhmm: string) => { const [h, m] = hhmm.slice(0, 5).split(':').map(Number); return h * 60 + m }
const fromMin = (min: number) => `${String(Math.floor((min % 1440) / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`

/** Forward cascade: arrival[0] = dayStart; depart = arrival + dwell; arrival[i+1] = depart + leg (ADR-008). */
export function computeSchedule(day: ItineraryDayDto): ScheduledStop[] {
  const result: ScheduledStop[] = []
  let cursor = toMin(day.dayStartTime)
  for (const stop of [...day.stops].sort((a, b) => a.sequence - b.sequence)) {
    const arrival = cursor + (stop.legToReach ? Math.round(stop.legToReach.seconds / 60) : 0)
    const depart = arrival + stop.dwellMinutes
    result.push({stop, arrival: fromMin(arrival), depart: fromMin(depart)})
    cursor = depart
  }
  return result
}

/** Green when the arrival falls inside the place's best-time window (when one is set); amber otherwise. */
export function flagStop(place: TripPlaceDto, arrival: string, _depart: string): StopFlag {
  if (!place.bestTimeStart || !place.bestTimeEnd) return 'green'
  const a = toMin(arrival)
  return a >= toMin(place.bestTimeStart) && a <= toMin(place.bestTimeEnd) ? 'green' : 'amber'
}

export function useSchedule(day: ItineraryDayDto, placesById: Record<string, TripPlaceDto>) {
  return useMemo(() => {
    const scheduled = computeSchedule(day).map(s => ({
      ...s,
      flag: placesById[s.stop.tripPlaceId] ? flagStop(placesById[s.stop.tripPlaceId], s.arrival, s.depart) : 'green' as StopFlag,
    }))
    const totalTravelSeconds = day.stops.reduce((sum, st) => sum + (st.legToReach?.seconds ?? 0), 0)
    const dayEnd = scheduled.length ? scheduled[scheduled.length - 1].depart : day.dayStartTime.slice(0, 5)
    return {scheduled, dayEnd, totalTravelSeconds}
  }, [day, placesById])
}
```

> Opening-hours flagging: `openingHoursJson` is a raw Places snapshot; parsing it for an open/closed check is deferred to a follow-up (best-time window covers the MVP flag). When added, fold a "closed at arrival" check into `flagStop` returning amber.

- [ ] **Step 4: Run → PASS**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/hooks/useSchedule.ts frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): useSchedule cascade hook + best-time flag (tested)"
```

---

## Task 16: Itinerary tab (Smart Schedule)

**Files:**
- Create: `frontend/src/pages/trips/components/ItineraryTab.tsx`, `ItineraryStopCard.tsx`, `TravelLeg.tsx`

**Interfaces:**
- Consumes: `useGetItineraryQuery`, `useListTripPlacesQuery`, `useReorderStopsMutation`, `useAddStopMutation`, `useSchedule`, slice `setActiveDay`/`setStopEditor`.

- [ ] **Step 1: Write `TravelLeg.tsx`** (the "🚗 18 นาที · 6.2 กม" row between cards)

```tsx
// frontend/src/pages/trips/components/TravelLeg.tsx
import type {LegDto, TravelMode} from '../../../shared/api/api'
const ICON: Record<TravelMode, string> = {Drive: '🚗', Walk: '🚶', Transit: '🚃'}
export function TravelLeg({leg, mode}: {leg: LegDto; mode: TravelMode}) {
  return (
    <div className="travel-leg">
      <span className="leg-pill">{ICON[mode]} {Math.round(leg.seconds / 60)} นาที</span>
      <span className="leg-line" />
      <span className="leg-dist">{(leg.meters / 1000).toFixed(1)} กม.</span>
    </div>
  )
}
```

- [ ] **Step 2: Write `ItineraryStopCard.tsx`** (arrival rail + name + dwell chip + best-time flag chip + reorder buttons + tap → editor)

```tsx
// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'

export function ItineraryStopCard({place, arrival, depart, dwell, flag, bestLabel, onEdit, onUp, onDown, canUp, canDown}: {
  place: TripPlaceDto; arrival: string; depart: string; dwell: number; flag: StopFlag
  bestLabel: string | null; onEdit: () => void; onUp: () => void; onDown: () => void; canUp: boolean; canDown: boolean
}) {
  return (
    <div className={`stop-card${flag === 'amber' ? ' warn' : ''}`}>
      <div className="stop-rail"><div className="stop-arr">{arrival}</div><div className="stop-dep">→{depart}</div></div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{place.name}</div>
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {dwell} น.</span>
          {bestLabel && <span className={`chip ${flag === 'amber' ? 'warn' : 'good'}`}>{flag === 'amber' ? '⚠' : '✓'} {bestLabel}</span>}
        </div>
      </button>
      <div className="stop-reorder">
        <button disabled={!canUp} onClick={onUp} aria-label="ขึ้น">▲</button>
        <button disabled={!canDown} onClick={onDown} aria-label="ลง">▼</button>
      </div>
    </div>
  )
}
```

> Reorder is via ▲/▼ buttons in MVP (deterministic + accessible + testable); both call `reorderStops` with the new id order. Native HTML5 drag-and-drop calling the same mutation is a Phase-2 polish (handoff shows a drag handle — keep the visual affordance but wire buttons for now).

- [ ] **Step 3: Write `ItineraryTab.tsx`** (day tabs → dark summary bar → cascade stop list)

```tsx
// frontend/src/pages/trips/components/ItineraryTab.tsx
import {useGetItineraryQuery, useListTripPlacesQuery, useReorderStopsMutation, type TripPlaceDto} from '../../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../../store/hooks'
import {setActiveDay, setStopEditor} from '../tripsSlice'
import {useSchedule} from '../hooks/useSchedule'
import {SegmentedTabs} from './SegmentedTabs'
import {ItineraryStopCard} from './ItineraryStopCard'
import {TravelLeg} from './TravelLeg'
import {StopEditorDialog} from './StopEditorDialog'
import {fmtTime} from '../../../shared/utils/format' // existing util or inline

function bestLabel(p: TripPlaceDto): string | null {
  if (!p.bestTimeStart || !p.bestTimeEnd) return null
  return `ช่วงดี ${p.bestTimeStart.slice(0,5)}–${p.bestTimeEnd.slice(0,5)}`
}

export function ItineraryTab({tripId}: {tripId: string}) {
  const dispatch = useAppDispatch()
  const activeDayId = useAppSelector(s => s.trips.activeDayId)
  const editorStopId = useAppSelector(s => s.trips.stopEditorStopId)
  const {data: days} = useGetItineraryQuery(tripId)
  const {data: places} = useListTripPlacesQuery(tripId)
  const [reorder] = useReorderStopsMutation()

  if (!days?.length) return <p className="trips-muted">กำลังโหลดแผน…</p>
  const dayId = activeDayId && days.some(d => d.id === activeDayId) ? activeDayId : days[0].id
  const day = days.find(d => d.id === dayId)!
  const placesById = Object.fromEntries((places ?? []).map(p => [p.id, p]))
  const {scheduled, dayEnd, totalTravelSeconds} = useSchedule(day, placesById)

  const move = (index: number, dir: -1 | 1) => {
    const ids = scheduled.map(s => s.stop.id)
    const j = index + dir
    if (j < 0 || j >= ids.length) return
    ;[ids[index], ids[j]] = [ids[j], ids[index]]
    reorder({tripId, dayId, orderedStopIds: ids})
  }

  return (
    <div className="itinerary-tab">
      <SegmentedTabs value={dayId} onChange={(v) => dispatch(setActiveDay(v))}
        options={days.map((d, i) => ({label: `วัน ${i + 1}`, value: d.id}))} />

      <div className="day-summary">
        <span>เริ่ม <b>{day.dayStartTime.slice(0, 5)}</b></span>
        <span>เสร็จ <b>{dayEnd}</b></span>
        <span>เดินทางรวม <b>{Math.round(totalTravelSeconds / 60)} น.</b></span>
      </div>

      <div className="stop-list">
        {scheduled.map((s, i) => {
          const place = placesById[s.stop.tripPlaceId]
          return (
            <div key={s.stop.id}>
              {i > 0 && s.stop.legToReach && <TravelLeg leg={s.stop.legToReach} mode={s.stop.travelModeToReach} />}
              {place && <ItineraryStopCard
                place={place} arrival={s.arrival} depart={s.depart} dwell={s.stop.dwellMinutes}
                flag={s.flag} bestLabel={bestLabel(place)}
                onEdit={() => dispatch(setStopEditor(s.stop.id))}
                onUp={() => move(i, -1)} onDown={() => move(i, 1)} canUp={i > 0} canDown={i < scheduled.length - 1} />}
            </div>
          )
        })}
        {scheduled.length === 0 && <p className="trips-empty">ยังไม่มีจุดแวะ — เพิ่มจากคลังสถานที่</p>}
      </div>

      {editorStopId && <StopEditorDialog tripId={tripId} day={day} stopId={editorStopId} placesById={placesById}
        onClose={() => dispatch(setStopEditor(null))} />}
    </div>
  )
}
```

> Remove the `fmtTime` import if not used; inline helpers are fine. Adding a stop to a day from the Places library: surface an "เพิ่มลงแผน" action on each `PlaceCard` (or in the stop list footer) that calls `useAddStopMutation({tripId, dayId, tripPlaceId, dwellMinutes: 60, travelModeToReach: trip.defaultTravelMode})`. Wire it where it fits the layout.

- [ ] **Step 4: Build + manual check** — switch day tabs; arrival times cascade; ▲/▼ reorders and times recompute; amber chip on a badly-timed stop.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/TravelLeg.tsx
git commit -m "feat(trips): smart-schedule itinerary tab (cascade times, legs, reorder, best-time flag)"
```

---

## Task 17: Stop Editor (dwell + best-time + travel mode)

**Files:**
- Create: `frontend/src/pages/trips/components/StopEditorDialog.tsx`, `DwellStepper.tsx`, `BestTimeBar.tsx`

**Interfaces:**
- Consumes: `useUpdateStopMutation`, `useUpdateTripPlaceMutation`, `useRemoveStopMutation`, `computeSchedule`.

- [ ] **Step 1: Write `DwellStepper.tsx`** (− / value / + with 15-min steps + quick chips)

```tsx
// frontend/src/pages/trips/components/DwellStepper.tsx
import {Button} from '@syncfusion/react-buttons'
const CHIPS = [30, 60, 90, 120]
export function DwellStepper({value, onChange}: {value: number; onChange: (v: number) => void}) {
  return (
    <div className="dwell-stepper">
      <div className="dwell-row">
        <Button onClick={() => onChange(Math.max(15, value - 15))} aria-label="ลด">−</Button>
        <div className="dwell-value">{value} <span>นาที</span></div>
        <Button color="Primary" onClick={() => onChange(value + 15)} aria-label="เพิ่ม">+</Button>
      </div>
      <div className="dwell-chips">
        {CHIPS.map(c => <button key={c} className={`chip${c === value ? ' active' : ''}`} onClick={() => onChange(c)}>{c}</button>)}
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Write `BestTimeBar.tsx`** (manual best-time window inputs; crowd-by-hour chart is manual/optional for v1 — render a simple static bar row or omit). **Per the resolved UI decisions, use Syncfusion `TimePicker` (`@syncfusion/react-calendars`) for the start/end inputs**, not raw `<input type="time">`; confirm value/onChange props in the `.d.ts`.

```tsx
// frontend/src/pages/trips/components/BestTimeBar.tsx
export function BestTimeBar({start, end, onChange}: {
  start: string | null; end: string | null; onChange: (start: string | null, end: string | null) => void
}) {
  return (
    <div className="best-time-bar">
      <label>ช่วงเวลาที่ดีที่สุด (ใส่เอง)</label>
      <div className="best-time-row">
        <input type="time" value={start?.slice(0, 5) ?? ''} onChange={(e) => onChange(e.target.value ? `${e.target.value}:00` : null, end)} />
        <span>–</span>
        <input type="time" value={end?.slice(0, 5) ?? ''} onChange={(e) => onChange(start, e.target.value ? `${e.target.value}:00` : null)} />
      </div>
      <p className="best-time-hint">crowd-by-hour (popular times) ไม่มีใน Places API — v1 กรอกช่วงเองตามนี้</p>
    </div>
  )
}
```

- [ ] **Step 3: Write `StopEditorDialog.tsx`** (dwell + best-time + travel mode + computed "ถึง → ออก" preview; save calls updateStop + updateTripPlace)

```tsx
// frontend/src/pages/trips/components/StopEditorDialog.tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {Button} from '@syncfusion/react-buttons'
import {useUpdateStopMutation, useUpdateTripPlaceMutation, useRemoveStopMutation,
  type ItineraryDayDto, type TripPlaceDto, type TravelMode} from '../../../shared/api/api'
import {computeSchedule} from '../hooks/useSchedule'
import {DwellStepper} from './DwellStepper'
import {BestTimeBar} from './BestTimeBar'

const MODES = [{label: '🚗 รถยนต์', value: 'Drive'}, {label: '🚶 เดิน', value: 'Walk'}, {label: '🚃 ขนส่ง', value: 'Transit'}]

export function StopEditorDialog({tripId, day, stopId, placesById, onClose}: {
  tripId: string; day: ItineraryDayDto; stopId: string; placesById: Record<string, TripPlaceDto>; onClose: () => void
}) {
  const stop = day.stops.find(s => s.id === stopId)!
  const place = placesById[stop.tripPlaceId]
  const [dwell, setDwell] = useState(stop.dwellMinutes)
  const [mode, setMode] = useState<TravelMode>(stop.travelModeToReach)
  const [bestStart, setBestStart] = useState(place?.bestTimeStart ?? null)
  const [bestEnd, setBestEnd] = useState(place?.bestTimeEnd ?? null)
  const [updateStop, {isLoading: s1}] = useUpdateStopMutation()
  const [updatePlace, {isLoading: s2}] = useUpdateTripPlaceMutation()
  const [removeStop] = useRemoveStopMutation()

  // local preview of this stop's computed arrival/leave with the edited dwell
  const preview = computeSchedule({...day, stops: day.stops.map(s => s.id === stopId ? {...s, dwellMinutes: dwell, travelModeToReach: mode} : s)})
    .find(p => p.stop.id === stopId)

  const save = async () => {
    await updateStop({tripId, stopId, dwellMinutes: dwell, travelModeToReach: mode}).unwrap()
    if (place && (bestStart !== place.bestTimeStart || bestEnd !== place.bestTimeEnd)) {
      await updatePlace({tripId, placeId: place.id, name: place.name, category: place.category,
        address: place.address, feeNote: place.feeNote, notes: place.notes, bestTimeStart: bestStart, bestTimeEnd: bestEnd}).unwrap()
    }
    onClose()
  }

  return (
    <Dialog open onClose={onClose} modal header={place?.name ?? 'แก้ไขจุดแวะ'} style={{width: '440px'}}>
      <div className="stop-editor">
        <BestTimeBar start={bestStart} end={bestEnd} onChange={(s, e) => { setBestStart(s); setBestEnd(e) }} />
        <label>จะอยู่ที่นี่กี่นาที</label>
        <DwellStepper value={dwell} onChange={setDwell} />
        <label>การเดินทางมาที่นี่</label>
        <DropDownList dataSource={MODES} fields={{text: 'label', value: 'value'}} value={mode}
          onChange={(e: {value: unknown}) => setMode(e.value as TravelMode)} />
        {preview && <div className="computed-box">ถึง {preview.arrival} → ออก (อัตโนมัติ) {preview.depart}</div>}
        <div className="trip-form-actions">
          <Button type="button" onClick={() => { removeStop({tripId, stopId}); onClose() }}>ลบจุดนี้</Button>
          <Button color="Primary" disabled={s1 || s2} onClick={save}>บันทึก</Button>
        </div>
      </div>
    </Dialog>
  )
}
```

- [ ] **Step 4: Build + manual check** — open a stop, change dwell → preview "ออก" updates; set a best-time window earlier than arrival → after save the card turns amber.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/StopEditorDialog.tsx frontend/src/pages/trips/components/DwellStepper.tsx frontend/src/pages/trips/components/BestTimeBar.tsx
git commit -m "feat(trips): stop editor (dwell stepper, manual best-time, travel mode, computed preview)"
```

---

## Task 18: `TripMap` (Google basemap + category pins + route)

> **Maps grounding gate:** fetch the **Maps JavaScript API** / `@vis.gl/react-google-maps`
> sub-skill; confirm `APIProvider` / `Map` / `AdvancedMarker` usage and the `mapId`
> requirement (CF9) and height requirement (CF2) before finalising.

**Files:**
- Create: `frontend/src/pages/trips/components/TripMap.tsx`
- Modify: `frontend/.env`/SWA build config note — `VITE_GOOGLE_MAPS_BROWSER_KEY`, `VITE_GOOGLE_MAPS_MAP_ID`

**Interfaces:**
- Consumes: `places: TripPlaceDto[]` (and optionally ordered `stops` for the route polyline). Browser key from `import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY`.

- [ ] **Step 1: Write `TripMap.tsx`**

```tsx
// frontend/src/pages/trips/components/TripMap.tsx
// Google Maps: Syncfusion has no interactive street map (frontend-guidelines §2 allowed exception).
import {APIProvider, Map, AdvancedMarker, Pin} from '@vis.gl/react-google-maps'
import type {TripPlaceDto} from '../../../shared/api/api'

const CAT_COLOR: Record<string, string> = {Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76', Cafe: '#b4791f', Shop: '#c2418f', Other: '#0e8f9e'}
const KEY = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) ?? 'DEMO_MAP_ID'

export function TripMap({places}: {places: TripPlaceDto[]}) {
  if (!KEY) return <div className="trip-map-fallback">ตั้งค่า VITE_GOOGLE_MAPS_BROWSER_KEY เพื่อแสดงแผนที่</div>
  const center = places.length ? {lat: places[0].lat, lng: places[0].lng} : {lat: 13.7563, lng: 100.5018}
  return (
    <APIProvider apiKey={KEY}>
      <div className="trip-map">{/* CF2: parent must have a fixed height (see CSS) */}
        <Map defaultZoom={12} defaultCenter={center} mapId={MAP_ID} gestureHandling="greedy" disableDefaultUI
          internalUsageAttributionIds={['gmp_git_agentskills_v1']}>
          {places.map(p => (
            <AdvancedMarker key={p.id} position={{lat: p.lat, lng: p.lng}} title={p.name}>
              <Pin background={CAT_COLOR[p.category]} borderColor="#fff" glyphColor="#fff" />
            </AdvancedMarker>
          ))}
        </Map>
      </div>
    </APIProvider>
  )
}
```

> Route polyline ordered by the active day's stop sequence is a follow-up enhancement — add a `Polyline`/`deck.gl` overlay (the skill notes polylines via the routes pattern) once the basemap renders. Keep MVP to pins + the existing list/itinerary cascade.

- [ ] **Step 2: Add the browser-key env vars**

Document in `frontend/.env.example` (and set locally): `VITE_GOOGLE_MAPS_BROWSER_KEY=<demo or referrer-restricted key>`, `VITE_GOOGLE_MAPS_MAP_ID=DEMO_MAP_ID`. Note in the SWA deploy config that these are build-time vars.

- [ ] **Step 3: Build + manual check** — Places tab → Map view renders the basemap with one pin per place (with a valid browser key / Demo Key); without a key, the fallback message shows (no crash).

- [ ] **Step 4: Run `compliance-review` on the Maps component, then commit**

```bash
git add frontend/src/pages/trips/components/TripMap.tsx frontend/.env.example
git commit -m "feat(trips): Google basemap with category pins (@vis.gl/react-google-maps, attribution)"
```

---

## Task 19: Teal tokens + responsive desktop split

**Files:**
- Create: `frontend/src/pages/trips/trips-tokens.css` (imported by the page CSS files)
- Modify: `TripDetailPage.css` (desktop two-pane), `TripDetailPage.tsx` (use `useBreakpoint`)

**Interfaces:**
- Consumes: `useBreakpoint()` → `'mobile' | 'tablet' | 'desktop'`.

- [ ] **Step 1: Write `trips-tokens.css`** (teal accent + Spline Sans Mono on numeric elements; scoped so the global orange Syncfusion theme is untouched)

```css
/* frontend/src/pages/trips/trips-tokens.css */
.trips-page, .trip-detail {
  --teal: #0e8f9e; --teal-deep: #0b7a87; --teal-soft: #e3f5f6;
  --ink: #0f172a; --page: #f8fafc; --surface: #fff; --border: #eef2f6;
  --muted: #94a3b8; --good: #1f9d76; --good-bg: #eafaf3; --warn: #b4791f; --warn-bg: #fff4e0;
  background: var(--page); color: var(--ink);
}
.stop-arr, .stop-dep, .place-coords, .leg-pill, .dwell-value, .computed-box, .day-summary b {
  font-family: 'Spline Sans Mono', ui-monospace, monospace;
}
.seg-tabs { display: flex; gap: 6px; }
.seg-tab { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 6px 14px; cursor: pointer; }
.seg-tab.active { background: var(--teal); border-color: var(--teal); color: #fff; }
.day-summary { display: flex; gap: 16px; background: var(--ink); color: #fff; border-radius: 12px; padding: 10px 14px; }
.stop-card { display: flex; gap: 11px; background: var(--surface); border: 1px solid var(--border); border-radius: 13px; padding: 12px; }
.stop-card.warn { border-color: var(--warn); box-shadow: 0 0 0 1px var(--warn) inset; }
.chip { font-size: 11px; padding: 2px 8px; border-radius: 999px; }
.chip.good { background: var(--good-bg); color: var(--good); }
.chip.warn { background: var(--warn-bg); color: var(--warn); }
.travel-leg { display: flex; align-items: center; gap: 8px; padding: 7px 0 7px 58px; color: var(--teal-deep); font-size: 11.5px; }
.trip-map { height: 60vh; min-height: 320px; width: 100%; }   /* CF2: explicit height */
```

- [ ] **Step 2: Desktop split in `TripDetailPage`** (≥1024 → two-pane: itinerary/places column + persistent map)

In `TripDetailPage.tsx`, read `const bp = useBreakpoint()` and add a `className={bp === 'desktop' ? 'trip-detail desktop' : 'trip-detail'}`. In `TripDetailPage.css`:
```css
.trip-detail.desktop { display: grid; grid-template-columns: 464px 1fr; gap: 16px; height: calc(100vh - 64px); }
.trip-detail.desktop .trip-map { height: 100%; }
@media (max-width: 1023px) { .trip-detail { display: block; } }
```
On desktop, render the map in the right pane always (places + itinerary tabs on the left); on mobile keep the tabbed single column (map only on the Places→Map view).

- [ ] **Step 3: Import tokens** — add `import './trips-tokens.css'` at the top of `TripsPage.tsx` and `TripDetailPage.tsx` (or `@import` in the page CSS).

- [ ] **Step 4: Build + manual responsive check** — desktop shows the split; mobile stacks.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/trips-tokens.css frontend/src/pages/trips/TripDetailPage.css frontend/src/pages/trips/TripDetailPage.tsx
git commit -m "feat(trips): teal tokens, Spline Sans Mono numerals, desktop split layout"
```

---

## Final verification (whole feature)

- [ ] **Backend:** `dotnet test backend/tests/MenuNest.Application.UnitTests` → all green; `dotnet build` clean.
- [ ] **Frontend:** `cd frontend && npm run build` clean; `npx vitest run` green.
- [ ] **E2E smoke (manual, per spec §10):** sign in (no family) → create trip → paste a Google Maps link → place saved → add 3–4 stops to a day → cascade times appear → reorder → times recompute → set an early best-time window → amber flag → desktop split renders.
- [ ] **Maps compliance:** the `google-maps-platform` appendix (cost notice, products used, key restrictions, ToS) was surfaced; `compliance-review` run on all Maps code.
