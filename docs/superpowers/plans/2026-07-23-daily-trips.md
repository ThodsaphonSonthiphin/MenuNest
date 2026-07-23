# Daily Trips (ทริปประจำวัน) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user mark a single-day **Trip** as "ประจำวัน" (`IsDaily`) so it runs evergreen as "today" and is grouped in a "ประจำวัน" section on `/trips` — reusing the existing Current-time-start machinery (issue #49).

**Architecture:** One scalar `bool IsDaily` on the `Trip` aggregate. Enabling is a *handler* op (`Trip` has no `Days` navigation): guard `DayCount == 1`, set the flag, and force the single `ItineraryDay.UseCurrentTimeAsStart = true`. The flag is set at creation (`CreateTrip`) and via a dedicated `SetTripDaily` PATCH/MCP toggle — never `UpdateTrip` (a full-replace PUT would clear it). The evergreen invariant is enforced in the backend (retiming + set-current-time refuse to un-pin a daily trip), gated on `IsDaily` (not day count). Frontend adds a `/trips` section, a card treatment, a detail/create switch, and itinerary UI locks.

**Tech Stack:** .NET (Clean Architecture: Domain / Application MediatR-style `Mediator` / Infrastructure EF Core + SQL Server), xUnit + Moq + FluentAssertions; React + Redux Toolkit Query + Syncfusion; EF migrations applied to prod **manually**.

**Design source:** `docs/superpowers/specs/2026-07-23-daily-trips-design.md`; ADR-130…137; CONTEXT.md → *Daily trip*; confirmed mock: Claude Design → *MenuNest design system* → Screens → *"Issue #49 — ทริปประจำวัน (Daily trips)"*.

## Global Constraints

- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) **and** frontend `tsc --noEmit` + `npm run build`. Do not `--no-verify`.
- **Stage narrowly:** `git add <explicit paths>` only. Never `git add -A`/`.`. Never stage `daily-state.md` or `AGENTS.md`.
- **Commit messages reference the ticket:** conventional-commit style `type(scope): summary`; the final merge commit ends with `(closes #49)`, intermediate commits use `(#49)`.
- **Git remote is `main`** (not `origin`): push with `git push main HEAD:main`; `gh` needs `--repo ThodsaphonSonthiphin/MenuNest`.
- **Backend tests use Moq** (`new Mock<IUserProvisioner>()`), never NSubstitute. Handler tests build on `HandlerTestFixture` (InMemory `Db`, `UserProvisioner.Object`, seeded `User`).
- **`IsDaily` is a scalar on an already-mapped entity** → **no** `IApplicationDbContext`/three-implementer edit (the CS0535 rule is only for a new `DbSet<>`).
- **Migrations are applied to prod BY HAND** and must be applied *before* the new code deploys (Get/List project `IsDaily` in server-side SQL). See Task 11 and CLAUDE.md.
- **No emoji in UI** — icons are inline-SVG components (`TripFormIcons.tsx` / `NavIcon.tsx`).
- **Frontend has no component/visual test harness** — UI tasks are verified interactively and diffed against the mock before push.

---

### Task 1: Domain flag + guards + EF config + migration

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/Trip.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddTripIsDaily.cs` (+ `.Designer.cs`, snapshot — generated)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/TripDailyTests.cs`

**Interfaces:**
- Produces: `Trip.IsDaily` (`bool`, default `false`); `Trip.SetDaily(bool)`; `Trip.Create(..., bool isDaily = false)`; `Trip.Reschedule` now rejects `IsDaily && dayCount > 1`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/TripDailyTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class TripDailyTests
{
    private static Trip OneDay() =>
        Trip.Create(Guid.NewGuid(), "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);

    [Fact]
    public void New_trip_is_not_daily_by_default()
        => OneDay().IsDaily.Should().BeFalse();

    [Fact]
    public void SetDaily_true_on_single_day_sets_flag()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.IsDaily.Should().BeTrue();
    }

    [Fact]
    public void SetDaily_true_throws_when_multi_day()
    {
        var trip = Trip.Create(Guid.NewGuid(), "Trip", new DateOnly(2026, 7, 23), 3, TravelMode.Drive);
        var act = () => trip.SetDaily(true);
        act.Should().Throw<DomainException>();
        trip.IsDaily.Should().BeFalse("a rejected enable must not mutate the flag");
    }

    [Fact]
    public void SetDaily_false_is_always_allowed()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.SetDaily(false);
        trip.IsDaily.Should().BeFalse();
    }

    [Fact]
    public void Create_as_daily_with_multi_day_throws()
    {
        var act = () => Trip.Create(Guid.NewGuid(), "X", new DateOnly(2026, 7, 23), 2, TravelMode.Drive, null, isDaily: true);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_to_multi_day_throws_while_daily()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        var act = () => trip.Reschedule(new DateOnly(2026, 8, 1), 2);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_single_day_is_allowed_while_daily()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.Reschedule(new DateOnly(2026, 8, 1), 1);
        trip.StartDate.Should().Be(new DateOnly(2026, 8, 1));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~TripDailyTests`
Expected: FAIL — `Trip` has no `SetDaily` / `Create` has no `isDaily` param (compile error).

- [ ] **Step 3: Add the property, factory param, and guards to `Trip.cs`**

In `backend/src/MenuNest.Domain/Entities/Trip.cs`, add the property after `DefaultTravelMode` (line 19):

```csharp
    public TravelMode DefaultTravelMode { get; private set; }
    public bool IsDaily { get; private set; }
    public DateTime? DeletedAt { get; private set; }
```

Change `Create` to accept `isDaily` (append after the existing optional `destination`) and guard it:

```csharp
    public static Trip Create(
        Guid userId, string name, DateOnly startDate, int dayCount,
        TravelMode defaultTravelMode, string? destination = null, bool isDaily = false)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Trip name is required.");
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");
        if (isDaily && dayCount != 1) throw new DomainException("A daily trip must be a single day.");

        return new Trip
        {
            UserId = userId,
            Name = name.Trim(),
            Destination = destination?.Trim(),
            StartDate = startDate,
            DayCount = dayCount,
            DefaultTravelMode = defaultTravelMode,
            IsDaily = isDaily,
        };
    }
```

Add the guard to `Reschedule`:

```csharp
    public void Reschedule(DateOnly startDate, int dayCount)
    {
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");
        if (IsDaily && dayCount > 1)
            throw new DomainException("A daily trip must stay a single day. Turn off daily mode first.");
        StartDate = startDate;
        DayCount = dayCount;
        UpdatedAt = DateTime.UtcNow;
    }
```

Add the `SetDaily` mutator (after `Reschedule`):

```csharp
    public void SetDaily(bool isDaily)
    {
        if (isDaily && DayCount != 1)
            throw new DomainException("A daily trip must be a single day.");
        IsDaily = isDaily;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Add the EF configuration line**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripConfiguration.cs`, after the `DefaultTravelMode` line (19):

```csharp
        b.Property(t => t.DefaultTravelMode).HasConversion<int>();
        b.Property(t => t.IsDaily).IsRequired().HasDefaultValue(false);
```

- [ ] **Step 5: Run the domain tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~TripDailyTests`
Expected: PASS (7 tests).

- [ ] **Step 6: Generate the migration**

Run (from `backend`):
```bash
dotnet ef migrations add AddTripIsDaily \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: a new `Migrations/<timestamp>_AddTripIsDaily.cs` whose `Up`/`Down` match this shape (verify — hand-fix only if the generated body differs):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(
        name: "IsDaily",
        table: "Trips",
        type: "bit",
        nullable: false,
        defaultValue: false);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "IsDaily", table: "Trips");
}
```
Also confirm `AppDbContextModelSnapshot.cs` gained a `b.Property<bool>("IsDaily")` entry on the `Trip` block (auto-written — do not hand-edit). **Do not apply to prod yet** (Task 11).

- [ ] **Step 7: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS (model validation green — a scalar bool maps cleanly; SQLite tests apply the new config).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/Trip.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
        backend/tests/MenuNest.Application.UnitTests/Trips/TripDailyTests.cs
git commit -m "feat(trips): Trip.IsDaily flag + single-day guards + migration (#49)"
```

---

### Task 2: Surface `IsDaily` on `TripDto` (read path)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetTrip/GetTripHandler.cs:20`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ListTrips/ListTripsHandler.cs:20`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/CreateTripHandler.cs:28`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs:52`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetTripHandlerTests.cs` (add one fact) — create if absent.

**Interfaces:**
- Produces: `TripDto(..., bool IsDaily)` — `IsDaily` is the **last** positional field.
- Consumes: `Trip.IsDaily` (Task 1).

- [ ] **Step 1: Write the failing test**

Add to (or create) `backend/tests/MenuNest.Application.UnitTests/Trips/GetTripHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetTrip;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetTripHandlerTests
{
    [Fact]
    public async Task GetTrip_returns_IsDaily_false_for_a_normal_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 7, 23), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var dto = await new GetTripHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new GetTripQuery(trip.Id), CancellationToken.None);

        dto.IsDaily.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetTripHandlerTests`
Expected: FAIL — `TripDto` has no `IsDaily` member (compile error).

- [ ] **Step 3: Append `IsDaily` to `TripDto`**

In `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`:

```csharp
public sealed record TripDto(
    Guid Id, string Name, string? Destination,
    DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode, bool IsDaily);
```

- [ ] **Step 4: Add `IsDaily` to all four construction sites**

`GetTripHandler.cs:20`:
```csharp
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode, t.IsDaily))
```
`ListTripsHandler.cs:20`:
```csharp
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode, t.IsDaily))
```
`CreateTripHandler.cs:28`:
```csharp
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
```
`UpdateTripHandler.cs:52`:
```csharp
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
```

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS (no test constructs `TripDto` directly, so no other breaks; the new fact passes).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.Application/UseCases/Trips/GetTrip/GetTripHandler.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ListTrips/ListTripsHandler.cs \
        backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/CreateTripHandler.cs \
        backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetTripHandlerTests.cs
git commit -m "feat(trips): expose IsDaily on TripDto (#49)"
```

---

### Task 3: `SetTripDaily` command + handler

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/SetTripDaily/SetTripDailyCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/SetTripDaily/SetTripDailyHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/SetTripDailyHandlerTests.cs`

**Interfaces:**
- Produces: `SetTripDailyCommand(Guid TripId, bool IsDaily) : ICommand<TripDto>`; handler guards `DayCount == 1` on enable and forces the single day's `UseCurrentTimeAsStart = true`.
- Consumes: `Trip.SetDaily` (Task 1), `TripDto(...)` (Task 2), `ItineraryDay.SetUseCurrentTimeAsStart`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/SetTripDailyHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetTripDaily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class SetTripDailyHandlerTests
{
    private static (Trip trip, ItineraryDay day) SeedSingleDay(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        fx.Db.SaveChanges();
        return (trip, day);
    }

    [Fact]
    public async Task Enable_sets_flag_and_forces_day_current_time()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day) = SeedSingleDay(fx);

        var dto = await new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, true), CancellationToken.None);

        dto.IsDaily.Should().BeTrue();
        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_on_multi_day_trip_throws()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 7, 23), 3, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 3; i++) fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        var act = () => new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, true), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Disable_clears_flag_but_leaves_day_current_time_untouched()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day) = SeedSingleDay(fx);
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        await fx.Db.SaveChangesAsync();

        var dto = await new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, false), CancellationToken.None);

        dto.IsDaily.Should().BeFalse();
        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue("disable only unlocks; it does not force the day flag off");
    }

    [Fact]
    public async Task Cannot_set_daily_on_another_users_trip()
    {
        using var fx = new HandlerTestFixture();
        var other = Trip.Create(Guid.NewGuid(), "Other", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        fx.Db.Trips.Add(other);
        await fx.Db.SaveChangesAsync();

        var act = () => new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(other.Id, true), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~SetTripDailyHandlerTests`
Expected: FAIL — `SetTripDaily` namespace/types do not exist.

- [ ] **Step 3: Create the command**

`backend/src/MenuNest.Application/UseCases/Trips/SetTripDaily/SetTripDailyCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Trips.SetTripDaily;

public sealed record SetTripDailyCommand(Guid TripId, bool IsDaily) : ICommand<TripDto>;
```

- [ ] **Step 4: Create the handler**

`backend/src/MenuNest.Application/UseCases/Trips/SetTripDaily/SetTripDailyHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetTripDaily;

public sealed class SetTripDailyHandler : ICommandHandler<SetTripDailyCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public SetTripDailyHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<TripDto> Handle(SetTripDailyCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(
            t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        trip.SetDaily(c.IsDaily); // throws if enabling while DayCount > 1 (ADR-133)

        // Enabling forces the single day evergreen (ADR-132) — Trip has no Days nav,
        // so the cross-entity write is done here, not in the domain.
        if (c.IsDaily)
        {
            var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.TripId == trip.Id, ct)
                ?? throw new DomainException("Itinerary day not found.");
            day.SetUseCurrentTimeAsStart(true);
        }

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~SetTripDailyHandlerTests`
Expected: PASS (4 tests). The `Mediator` source generator auto-registers the new handler — no DI edit.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/SetTripDaily/ \
        backend/tests/MenuNest.Application.UnitTests/Trips/SetTripDailyHandlerTests.cs
git commit -m "feat(trips): SetTripDaily command — guard + force evergreen (#49)"
```

---

### Task 4: `SetTripDaily` HTTP endpoint + MCP tool

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs`

**Interfaces:**
- Produces: `PATCH /api/trips/{id}/daily` (body `{ isDaily }`) → `TripDto`; MCP `set_trip_daily(tripId, isDaily)`.
- Consumes: `SetTripDailyCommand` (Task 3).

- [ ] **Step 1: Add the controller action + body record**

In `TripsController.cs`, add a `using`:
```csharp
using MenuNest.Application.UseCases.Trips.SetTripDaily;
```
Add the action after `SetDayUseCurrentTime` (after line 130):
```csharp
    [HttpPatch("api/trips/{id:guid}/daily")]
    public async Task<ActionResult<TripDto>> SetDaily(Guid id, [FromBody] SetTripDailyBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new SetTripDailyCommand(id, b.IsDaily), ct));
```
Add the body record near the other bodies (after `SetDayUseCurrentTimeBody`, line 173):
```csharp
public sealed record SetTripDailyBody(bool IsDaily);
```

- [ ] **Step 2: Add the MCP tool**

In `TripTools.cs`, add a `using`:
```csharp
using MenuNest.Application.UseCases.Trips.SetTripDaily;
```
Add the tool after `update_trip` (after line 63):
```csharp
    [McpServerTool, Description("Turn a trip's 'daily' mode on or off. A daily trip must be single-day (dayCount==1) — enabling a multi-day trip is rejected; remove the extra days first. Enabling also forces the day to always start from the current time (evergreen 'today'). Returns the updated trip.")]
    public async Task<TripDto> set_trip_daily(
        [Description("Trip ID")] Guid tripId,
        [Description("true to make the trip a daily/recurring 'run-as-today' route; false to turn daily mode off")] bool isDaily,
        CancellationToken ct)
        => await mediator.Send(new SetTripDailyCommand(tripId, isDaily), ct);
```

- [ ] **Step 3: Build to verify wiring**

Run: `dotnet build backend/MenuNest.sln -c Release`
Expected: PASS.

- [ ] **Step 4: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs
git commit -m "feat(trips): SetTripDaily PATCH endpoint + set_trip_daily MCP tool (#49)"
```

---

### Task 5: Create-as-daily (CreateTrip)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/CreateTripCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/CreateTripValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/CreateTripHandler.cs`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (`create_trip`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/CreateTripHandlerTests.cs` (add facts)

**Interfaces:**
- Produces: `CreateTripCommand(..., bool IsDaily = false)`; when `true`, the seeded day gets `UseCurrentTimeAsStart = true`; validator enforces `IsDaily ⇒ DayCount == 1`.
- Consumes: `Trip.Create(..., isDaily)` (Task 1).

- [ ] **Step 1: Write the failing tests**

Add to `backend/tests/MenuNest.Application.UnitTests/Trips/CreateTripHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Create_as_daily_seeds_one_day_with_current_time_start()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());

        var dto = await handler.Handle(
            new CreateTripCommand("ไปทำงาน", null, new DateOnly(2026, 7, 23), 1, TravelMode.Drive, IsDaily: true),
            CancellationToken.None);

        dto.IsDaily.Should().BeTrue();
        var trip = fx.Db.Trips.Single();
        var day = await fx.Db.ItineraryDays.SingleAsync(d => d.TripId == trip.Id);
        day.UseCurrentTimeAsStart.Should().BeTrue();
    }

    [Fact]
    public async Task Create_as_daily_with_multi_day_is_rejected()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());
        await FluentActions.Awaiting(() => handler.Handle(
            new CreateTripCommand("X", null, new DateOnly(2026, 7, 23), 3, TravelMode.Drive, IsDaily: true),
            CancellationToken.None).AsTask()
        ).Should().ThrowAsync<FluentValidation.ValidationException>();
    }
```

(Note the named `IsDaily:` argument — the existing 2 tests keep their 5-positional-arg calls and still compile because `IsDaily` has a default.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CreateTripHandlerTests`
Expected: FAIL — `CreateTripCommand` has no `IsDaily` param.

- [ ] **Step 3: Add `IsDaily` to the command**

`CreateTripCommand.cs`:
```csharp
public sealed record CreateTripCommand(
    string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode, bool IsDaily = false)
    : ICommand<TripDto>;
```

- [ ] **Step 4: Add the validator rule**

`CreateTripValidator.cs`:
```csharp
    public CreateTripValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DayCount).InclusiveBetween(1, 60);
        RuleFor(x => x.DayCount).Equal(1).When(x => x.IsDaily)
            .WithMessage("A daily trip must be a single day.");
    }
```

- [ ] **Step 5: Thread `IsDaily` through the handler**

`CreateTripHandler.cs` — pass the flag to `Trip.Create`, seed the day flag, and add the DTO arg:
```csharp
        var trip = Trip.Create(user.Id, c.Name, c.StartDate, c.DayCount, c.DefaultTravelMode, c.Destination, c.IsDaily);
        _db.Trips.Add(trip);
        for (var i = 0; i < c.DayCount; i++)
        {
            var day = ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i));
            if (c.IsDaily) day.SetUseCurrentTimeAsStart(true); // single day → evergreen (ADR-132)
            _db.ItineraryDays.Add(day);
        }

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
```

- [ ] **Step 6: Add `isDaily` to the `create_trip` MCP tool**

In `TripTools.cs`, `create_trip` — add the parameter (before `CancellationToken ct`) and pass it:
```csharp
        [Description("Default travel mode new legs inherit: Drive, Walk, or Transit")] TravelMode defaultTravelMode,
        [Description("Optional: create the trip as a 'daily' recurring run-as-today route. Requires dayCount == 1.")] bool isDaily,
        CancellationToken ct)
        => await mediator.Send(new CreateTripCommand(name, destination, startDate, dayCount, defaultTravelMode, isDaily), ct);
```

- [ ] **Step 7: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/CreateTrip/ \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/CreateTripHandlerTests.cs
git commit -m "feat(trips): create-as-daily (CreateTrip + validator + MCP) (#49)"
```

---

### Task 6: Backend evergreen guards (D5)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourHandler.cs:57-58`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/SetDayUseCurrentTime/SetDayUseCurrentTimeHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/DailyEvergreenGuardTests.cs`

**Interfaces:**
- Produces: on a daily trip, `RetimeStopToHour` does not clear `UseCurrentTimeAsStart`; `SetDayUseCurrentTime(false)` throws.
- Consumes: `Trip.IsDaily` (Task 1).

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/DailyEvergreenGuardTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class DailyEvergreenGuardTests
{
    [Fact]
    public async Task SetDayUseCurrentTime_false_is_refused_on_a_daily_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var act = () => new SetDayUseCurrentTimeHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetDayUseCurrentTimeCommand(trip.Id, day.Id, false), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>();

        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue();
    }

    [Fact]
    public async Task SetDayUseCurrentTime_true_still_works_on_a_daily_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        await new SetDayUseCurrentTimeHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetDayUseCurrentTimeCommand(trip.Id, day.Id, true), CancellationToken.None);

        (await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id)).UseCurrentTimeAsStart.Should().BeTrue();
    }
}
```

(RetimeStopToHour has its own relational tests; its guard is a one-line condition verified by build + the existing retime tests staying green. A dedicated retime-on-daily test needs the full `RetimeStopToHour` fixture — add it to the existing retime test file if one exists, asserting `day.UseCurrentTimeAsStart` remains `true` after a same-day retime on a daily trip.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~DailyEvergreenGuardTests`
Expected: FAIL — `SetDayUseCurrentTime(false)` currently succeeds on a daily trip.

- [ ] **Step 3: Guard `SetDayUseCurrentTimeHandler`**

Rewrite the handler body to load the trip and refuse turning the flag off on a daily trip:

```csharp
    public async ValueTask<Unit> Handle(SetDayUseCurrentTimeCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(
            t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        if (trip.IsDaily && !c.UseCurrentTime)
            throw new DomainException("A daily trip always starts from the current time. Turn off daily mode first.");

        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId && d.TripId == trip.Id, ct)
            ?? throw new DomainException("Itinerary day not found.");

        day.SetUseCurrentTimeAsStart(c.UseCurrentTime);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
```

- [ ] **Step 4: Guard `RetimeStopToHourHandler`**

At lines 57-58, guard the un-pin (the `trip` is already loaded at line 25):

```csharp
        day.SetStartTime(c.NewDayStartTime);
        if (!trip.IsDaily)
            day.SetUseCurrentTimeAsStart(false);            // pin (ADR-115); never un-pin a daily trip (ADR-134)
```

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS (new guard tests pass; existing retime/set-current-time tests use non-daily trips and stay green).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourHandler.cs \
        backend/src/MenuNest.Application/UseCases/Trips/SetDayUseCurrentTime/SetDayUseCurrentTimeHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/DailyEvergreenGuardTests.cs
git commit -m "feat(trips): backend-enforce evergreen on daily trips (retime + set-current-time) (#49)"
```

---

### Task 7: Frontend api slice — `isDaily` type + `setTripDaily` mutation

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Produces: `TripDto.isDaily: boolean`; `createTrip` arg gains `isDaily?: boolean`; `useSetTripDailyMutation()`.

- [ ] **Step 1: Add `isDaily` to the client `TripDto`**

`api.ts:498`:
```ts
export interface TripDto { id: string; name: string; destination: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode; isDaily: boolean }
```

- [ ] **Step 2: Add `isDaily` to `createTrip` and add the `setTripDaily` mutation**

`api.ts` — `createTrip` arg (line 1359):
```ts
        createTrip: build.mutation<TripDto, {name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode; isDaily?: boolean}>({
            query: (b) => ({url: '/api/trips', method: 'POST', body: b}),
            invalidatesTags: ['Trips'],
        }),
```
Add immediately after the `updateTrip` mutation (after line 1366):
```ts
        setTripDaily: build.mutation<TripDto, {id: string; isDaily: boolean}>({
            query: ({id, isDaily}) => ({url: `/api/trips/${id}/daily`, method: 'PATCH', body: {isDaily}}),
            invalidatesTags: (_r, _e, a) => ['Trips', {type: 'TripDetail', id: a.id}, {type: 'TripItinerary', id: a.id}],
        }),
```

- [ ] **Step 3: Export the hook**

Find the `export const { ... } = api` block at the bottom of `api.ts` and add `useSetTripDailyMutation` to the destructured list (next to `useUpdateTripMutation`).

- [ ] **Step 4: Verify types + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(trips): api types for IsDaily + setTripDaily mutation (#49)"
```

---

### Task 8: `/trips` — "ประจำวัน" section, daily card, badge icon, emoji fix

**Files:**
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (add `RepeatIcon`)
- Modify: `frontend/src/pages/trips/TripsPage.tsx`
- Modify: `frontend/src/pages/trips/TripsPage.css`

**Interfaces:**
- Consumes: `TripDto.isDaily` (Task 7).

- [ ] **Step 1: Add the `RepeatIcon`**

Append to `TripFormIcons.tsx` (before the final closing — after `GripIcon`):
```tsx
/** Repeat / recurring — daily-trip badge + section (issue #49). */
export function RepeatIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M17 2l4 4-4 4" />
      <path d="M3 11V9a4 4 0 0 1 4-4h14" />
      <path d="M7 22l-4-4 4-4" />
      <path d="M21 13v2a4 4 0 0 1-4 4H3" />
    </svg>
  )
}
```

- [ ] **Step 2: Rewrite `TripsPage.tsx` to split sections + daily card + drop the emoji**

Replace the whole component body's imports and JSX:
```tsx
// frontend/src/pages/trips/TripsPage.tsx
import {useNavigate} from 'react-router-dom'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useListTripsQuery, type TripDto} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/index'
import {setCreateTripOpen} from './tripsSlice'
import {CreateTripDialog} from './components/CreateTripDialog'
import {SuitcaseIcon, RepeatIcon} from './components/TripFormIcons'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import './trips-tokens.css'
import './TripsPage.css'

export function TripsPage() {
  const nav = useNavigate()
  const dispatch = useAppDispatch()
  const open = useAppSelector(s => s.trips.createTripOpen)
  const {data: trips, isLoading, error} = useListTripsQuery()

  const daily = trips?.filter(t => t.isDaily) ?? []
  const regular = trips?.filter(t => !t.isDaily) ?? []

  const dailyCard = (t: TripDto) => (
    <button
      key={t.id}
      className="trip-card trip-card--daily"
      data-testid="trip-card"
      onClick={() => nav(`/trips/${t.id}`)}
    >
      <div className="trip-card-name">{t.name}</div>
      <span className="trip-badge-daily"><RepeatIcon /> ประจำวัน</span>
      <div className="trip-card-today"><span className="dot" /> วันนี้</div>
    </button>
  )

  const regularCard = (t: TripDto) => (
    <button
      key={t.id}
      className="trip-card"
      data-testid="trip-card"
      onClick={() => nav(`/trips/${t.id}`)}
    >
      <div className="trip-card-name">{t.name}</div>
      <div className="trip-card-meta">
        {t.destination ?? ''}{t.destination ? ' · ' : ''}{t.dayCount} วัน
      </div>
      <div className="trip-card-dates">{t.startDate}</div>
    </button>
  )

  return (
    <section className="trips-page">
      <header className="trips-header">
        <h1><SuitcaseIcon className="trips-title-ic" /> ทริปของฉัน</h1>
        <Button
          color={Color.Primary}
          variant={Variant.Filled}
          onClick={() => dispatch(setCreateTripOpen(true))}
        >
          + ทริปใหม่
        </Button>
      </header>

      {isLoading && <p className="trips-muted">กำลังโหลด…</p>}
      {error && <p className="trips-field-error">{getErrorMessage(error)}</p>}
      {!isLoading && !error && trips?.length === 0 && (
        <p className="trips-empty">ยังไม่มีทริป — สร้างทริปแรกของคุณ</p>
      )}

      {daily.length > 0 && (
        <section className="trips-section">
          <div className="trips-section-lab"><RepeatIcon /> ประจำวัน</div>
          <div className="trips-grid">{daily.map(dailyCard)}</div>
        </section>
      )}

      {regular.length > 0 && (
        <section className="trips-section">
          {daily.length > 0 && <div className="trips-section-lab">ทริป</div>}
          <div className="trips-grid">{regular.map(regularCard)}</div>
        </section>
      )}

      {open && (
        <CreateTripDialog
          onClose={() => dispatch(setCreateTripOpen(false))}
          onCreated={(id) => {
            dispatch(setCreateTripOpen(false))
            nav(`/trips/${id}`)
          }}
        />
      )}
    </section>
  )
}
```

- [ ] **Step 3: Add the section + daily-card styles**

Append to `TripsPage.css` (tokens `--trp-teal`/`--trp-teal-soft` already exist on `.trips-page`):
```css
/* -------- Daily-trip section + card (issue #49) -------- */
.trips-title-ic { font-size: 1.3rem; vertical-align: -3px; color: var(--trp-teal-dark); margin-right: 4px; }
.trips-section { display: flex; flex-direction: column; gap: 10px; }
.trips-section-lab {
  display: flex; align-items: center; gap: 7px;
  font-size: 0.8rem; font-weight: 700; color: var(--trp-text-muted); letter-spacing: 0.02em;
}
.trips-section-lab svg { width: 14px; height: 14px; color: var(--trp-teal-dark); }
.trip-card--daily { border-color: #d6ebee; background: linear-gradient(180deg, #fbfeff, #fff); }
.trip-badge-daily {
  align-self: flex-start; display: inline-flex; align-items: center; gap: 4px;
  background: var(--trp-teal-soft); color: var(--trp-teal-dark);
  border-radius: 999px; padding: 2px 9px 2px 7px; font-size: 0.66rem; font-weight: 700;
}
.trip-badge-daily svg { width: 12px; height: 12px; }
.trip-card-today {
  display: inline-flex; align-items: center; gap: 5px;
  font-size: 0.78rem; font-weight: 700; color: var(--trp-teal);
}
.trip-card-today .dot { width: 6px; height: 6px; border-radius: 50%; background: var(--trp-teal); }
```

- [ ] **Step 4: Verify types + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 5: Interactive verify**

Run the app (see `/run` or `npm run dev`), open `/trips`. Confirm: a daily trip appears in a top "ประจำวัน" section with the repeat badge + "วันนี้" and NO fixed date; regular trips appear below; the header shows the suitcase SVG (no 🧳). Diff against the mock (Screens → "Issue #49 — ทริปประจำวัน"). If no daily trip exists yet, create one via Task 9 first, or temporarily toggle one over MCP `set_trip_daily`.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/TripFormIcons.tsx \
        frontend/src/pages/trips/TripsPage.tsx \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): ประจำวัน section + daily card on /trips; drop emoji (#49)"
```

---

### Task 9: "โหมดประจำวัน" switch — detail header + create dialog

**Files:**
- Create: `frontend/src/pages/trips/components/DailyToggle.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`
- Modify: `frontend/src/pages/trips/components/CreateTripDialog.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (toggle styles)

**Interfaces:**
- Consumes: `useSetTripDailyMutation`, `TripDto.isDaily` (Task 7); `RepeatIcon`, `MapRouteIcon` (Task 8 / existing).
- Produces: `<DailyToggle trip={trip} onError={...} />`.

- [ ] **Step 1: Create the `DailyToggle` component**

`frontend/src/pages/trips/components/DailyToggle.tsx`:
```tsx
// frontend/src/pages/trips/components/DailyToggle.tsx
import {useSetTripDailyMutation, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {RepeatIcon} from './TripFormIcons'

/**
 * The trip-level "โหมดประจำวัน" switch (issue #49). Commits immediately via
 * setTripDaily; enabling a multi-day trip is rejected by the backend and the
 * message is surfaced through onError. Disabled (with a hint) when the trip has
 * more than one day, since a daily trip must be single-day.
 */
export function DailyToggle({trip, onError}: {trip: TripDto; onError: (msg: string | null) => void}) {
  const [setDaily, {isLoading}] = useSetTripDailyMutation()
  const canEnable = trip.dayCount === 1
  const disabled = isLoading || (!trip.isDaily && !canEnable)

  const toggle = async () => {
    onError(null)
    try {
      await setDaily({id: trip.id, isDaily: !trip.isDaily}).unwrap()
    } catch (e) {
      onError(getErrorMessage(e))
    }
  }

  return (
    <button
      type="button"
      className={`daily-toggle${trip.isDaily ? ' on' : ''}`}
      role="switch"
      aria-checked={trip.isDaily}
      aria-label="โหมดประจำวัน"
      disabled={disabled}
      title={!trip.isDaily && !canEnable ? 'ทริปประจำวันต้องเป็นวันเดียว' : undefined}
      onClick={toggle}
    >
      <RepeatIcon className="daily-toggle-ic" />
      <span>ประจำวัน</span>
      <span className="daily-toggle-track"><span className="daily-toggle-knob" /></span>
    </button>
  )
}
```

- [ ] **Step 2: Place the switch in both detail headers + drop the map emoji**

In `TripDetailPage.tsx`, add the import:
```tsx
import { DailyToggle } from './components/DailyToggle'
import { MapRouteIcon } from './components/TripFormIcons'
```
Desktop header (line 105) — replace the emoji name span:
```tsx
          <span className="trip-topbar-name"><MapRouteIcon className="trip-topbar-ic" /> {trip?.name ?? '…'}</span>
```
Desktop meta block — add the toggle after `<TripDateEditor .../>` (inside the `{trip && (...)}` at ~109):
```tsx
              <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
              {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
              <DailyToggle trip={trip} onError={setDateError} />
```
Mobile meta block — same addition after the mobile `<TripDateEditor .../>` (~192):
```tsx
              <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
              {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
              <DailyToggle trip={trip} onError={setDateError} />
```
(The mobile name at line 188 has no emoji — leave it.)

- [ ] **Step 3: Add toggle styles**

Append to `TripDetailPage.css`:
```css
/* Daily-mode switch in the trip header (issue #49) */
.daily-toggle {
  display: inline-flex; align-items: center; gap: 6px;
  border: 1px solid rgba(255,255,255,0.18); background: rgba(255,255,255,0.04);
  color: #9fb0c4; border-radius: 999px; padding: 3px 10px; margin-left: 8px;
  font: inherit; font-size: 11px; font-weight: 700; cursor: pointer; vertical-align: middle;
}
.daily-toggle:disabled { opacity: 0.5; cursor: not-allowed; }
.daily-toggle-ic { width: 13px; height: 13px; }
.daily-toggle-track { width: 26px; height: 15px; border-radius: 999px; background: #4a5568; position: relative; transition: background .15s; }
.daily-toggle-knob { position: absolute; top: 2px; left: 2px; width: 11px; height: 11px; border-radius: 50%; background: #fff; transition: left .15s; }
.daily-toggle.on { border-color: var(--teal); background: rgba(14,143,158,0.18); color: #fff; }
.daily-toggle.on .daily-toggle-track { background: var(--teal); }
.daily-toggle.on .daily-toggle-knob { left: 13px; }
.trip-topbar-ic { width: 18px; height: 18px; vertical-align: -3px; color: #9fb0c4; }
/* Mobile header uses light tokens — override the dark chrome there */
.trip-detail-meta .daily-toggle { border-color: var(--trp-border); background: transparent; color: var(--trp-text-muted); }
.trip-detail-meta .daily-toggle.on { border-color: var(--teal); background: rgba(14,143,158,0.08); color: var(--teal-deep); }
.trip-detail-meta .daily-toggle .daily-toggle-track { background: #cbd5e1; }
.trip-detail-meta .daily-toggle.on .daily-toggle-track { background: var(--teal); }
```

- [ ] **Step 4: Add the daily switch to the create dialog (pin day-count to 1)**

In `CreateTripDialog.tsx`: add `isDaily` to `FormValues` (line 21-27) and `defaultValues` (line 70-76):
```tsx
interface FormValues {
  name: string
  destination: string
  startDate: string
  dayCount: number
  defaultTravelMode: TravelMode
  isDaily: boolean
}
```
```tsx
    defaultValues: {
      name: '',
      destination: '',
      startDate: today,
      dayCount: 3,
      defaultTravelMode: 'Drive',
      isDaily: false,
    },
```
Import `RepeatIcon` (add to the `TripFormIcons` import list). Watch `isDaily` alongside the others (line 82):
```tsx
  const [startDate, dayCount, isDaily] = useWatch({control, name: ['startDate', 'dayCount', 'isDaily']})
```
Pass `isDaily` in the submit body (line 94-100):
```tsx
      const t = await createTrip({
        name: v.name.trim(),
        destination: v.destination.trim() || null,
        startDate: v.startDate,
        dayCount: v.isDaily ? 1 : v.dayCount,
        defaultTravelMode: v.defaultTravelMode,
        isDaily: v.isDaily,
      }).unwrap()
```
Add a daily field with a `Controller` that also pins day-count when turned on — insert before the `{/* Start date + day count */}` row (before line 178):
```tsx
        {/* Daily mode */}
        <div className="ctd-field">
          <Controller
            control={control}
            name="isDaily"
            render={({field}) => (
              <button
                type="button"
                className={`ctd-daily${field.value ? ' on' : ''}`}
                role="switch"
                aria-checked={field.value}
                onClick={() => field.onChange(!field.value)}
              >
                <span className="ctd-daily-ic"><RepeatIcon /></span>
                <span className="ctd-daily-txt">
                  <b>ทริปประจำวัน</b>
                  <small>เดินทางเส้นเดิมซ้ำทุกวัน — บังคับ 1 วัน, เริ่มเป็น "วันนี้" อัตโนมัติ</small>
                </span>
                <span className="ctd-daily-track"><span className="ctd-daily-knob" /></span>
              </button>
            )}
          />
        </div>
```
Disable the day-count stepper when `isDaily` — in the stepper `Controller` render (lines 213-235), gate both buttons:
```tsx
                  <button type="button" className="ctd-step" aria-label="ลดจำนวนวัน"
                    disabled={isDaily || field.value <= MIN_DAYS}
                    onClick={() => field.onChange(Math.max(MIN_DAYS, field.value - 1))}>
                    <MinusIcon />
                  </button>
                  <span className="ctd-step-val" aria-live="polite">{isDaily ? 1 : field.value}</span>
                  <button type="button" className="ctd-step" aria-label="เพิ่มจำนวนวัน"
                    disabled={isDaily || field.value >= MAX_DAYS}
                    onClick={() => field.onChange(Math.min(MAX_DAYS, field.value + 1))}>
                    <PlusIcon />
                  </button>
```
Add minimal styles to `TripsPage.css` (the `.create-trip-dialog` scope lives there):
```css
.create-trip-dialog .ctd-daily {
  display: flex; align-items: center; gap: 11px; width: 100%; text-align: left;
  border: 1.5px solid var(--border); border-radius: 14px; background: var(--surface);
  padding: 12px 14px; font: inherit; cursor: pointer;
}
.create-trip-dialog .ctd-daily.on { border-color: var(--teal); background: var(--teal-soft); }
.create-trip-dialog .ctd-daily-ic { display: flex; font-size: 18px; color: var(--teal); }
.create-trip-dialog .ctd-daily-txt { flex: 1; display: flex; flex-direction: column; gap: 2px; }
.create-trip-dialog .ctd-daily-txt b { font-size: 14px; color: var(--ink); }
.create-trip-dialog .ctd-daily-txt small { font-size: 11.5px; color: var(--muted); }
.create-trip-dialog .ctd-daily-track { flex: none; width: 40px; height: 24px; border-radius: 999px; background: #cbd5e1; position: relative; transition: background .15s; }
.create-trip-dialog .ctd-daily-knob { position: absolute; top: 3px; left: 3px; width: 18px; height: 18px; border-radius: 50%; background: #fff; box-shadow: 0 2px 5px rgba(0,0,0,.25); transition: left .15s; }
.create-trip-dialog .ctd-daily.on .ctd-daily-track { background: var(--teal); }
.create-trip-dialog .ctd-daily.on .ctd-daily-knob { left: 19px; }
```

- [ ] **Step 5: Verify types + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 6: Interactive verify**

- Create dialog: toggle "ทริปประจำวัน" → day-count pins to 1 and the stepper disables; create → the new trip lands in the "ประจำวัน" section.
- Detail header (desktop + mobile): the switch reflects state; turning it on for a single-day trip works; for a multi-day trip the switch is disabled with the "ต้องเป็นวันเดียว" tooltip; turning on then attempting via a multi-day trip surfaces the backend error via `dateError`.
- Confirm the desktop top-bar shows the map SVG (no 🗺️). Diff against the mock (Panel B).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/components/DailyToggle.tsx \
        frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/TripDetailPage.css \
        frontend/src/pages/trips/components/CreateTripDialog.tsx \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): โหมดประจำวัน switch on detail + create dialog; drop map emoji (#49)"
```

---

### Task 10: Itinerary UI locks (D5) — lock current-time toggle, hide retiming apply

**Files:**
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx` (pass `isDaily` to `ItineraryTab`)
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/components/DayStartEditor.tsx`
- Modify: `frontend/src/pages/trips/components/StopDetailSheet.tsx`
- Modify: `frontend/src/pages/trips/components/HourlyPlanner.tsx`

**Interfaces:**
- Consumes: `TripDto.isDaily`.
- Produces: `ItineraryTab` prop `isDaily?: boolean`; `DayStartEditor` prop `locked?: boolean`; `HourlyPlanner` prop `isDaily?: boolean`; the `planner` object gains `isDaily`.

- [ ] **Step 1: Pass `isDaily` from `TripDetailPage` into `ItineraryTab`**

Both render sites of `<ItineraryTab tripId={tripId} />` (desktop line 154, mobile further down):
```tsx
{tab === 'itinerary' && <ItineraryTab tripId={tripId} isDaily={trip?.isDaily ?? false} />}
```

- [ ] **Step 2: Thread `isDaily` through `ItineraryTab`**

Add `isDaily` to the component's props type and signature (default `false`). Pass it to `DayStartEditor` (line 290-297):
```tsx
          <DayStartEditor
            key={resolvedDayId}
            tripId={tripId}
            dayId={resolvedDayId}
            dayStartTime={resolvedDay.dayStartTime}
            useCurrentTimeAsStart={resolvedDay.useCurrentTimeAsStart}
            locked={isDaily}
            onError={setActionError}
          />
```
And into the `planner` prop object (line 502):
```tsx
          planner={{tripId, day: resolvedDay, tripDayCount: dayList.length, isDaily}}
```

- [ ] **Step 3: Add the `locked` behaviour to `DayStartEditor`**

Add `locked` to the props (line 25-37):
```tsx
export function DayStartEditor({
  tripId,
  dayId,
  dayStartTime,
  useCurrentTimeAsStart,
  locked = false,
  onError,
}: {
  tripId: string
  dayId: string
  dayStartTime: string
  useCurrentTimeAsStart: boolean
  locked?: boolean
  onError: (msg: string | null) => void
}) {
```
Make the checkbox forced-on + disabled and suppress its handler when locked (line 112-119):
```tsx
      <label className="day-start-live-toggle">
        <input
          type="checkbox"
          checked={locked ? true : useCurrentTimeAsStart}
          disabled={locked}
          onChange={locked ? undefined : handleToggleUseCurrentTime}
        />
        ใช้เวลาปัจจุบันเสมอ{locked && ' (โหมดประจำวัน)'}
      </label>
```
(The TimePicker is already `disabled={useCurrentTimeAsStart}` and the "ตอนนี้" button already hidden while the flag is on; a daily trip always has the flag on, so no further change is needed there.)

- [ ] **Step 4: Pass `isDaily` from `StopDetailSheet` into `HourlyPlanner`**

In `StopDetailSheet.tsx`, extend the `planner` prop's type to include `isDaily: boolean` (find the `planner?: {...}` prop type near the top of the file and add `isDaily: boolean`). Then pass it through (line 122-130):
```tsx
              <HourlyPlanner
                day={planner.day}
                stopId={stopId}
                place={place}
                tripId={planner.tripId}
                tripDayCount={planner.tripDayCount}
                isDaily={planner.isDaily}
                arrival={arrival}
                onClose={() => setShowHourly(false)}
              />
```

- [ ] **Step 5: Gate retiming in `HourlyPlanner`**

Add `isDaily` to the props (line 18-28):
```tsx
export function HourlyPlanner({
  day, stopId, place, tripId, tripDayCount, isDaily, arrival, onClose,
}: {
  day: ItineraryDayDto
  stopId: string
  place: TripPlaceDto
  tripId: string
  tripDayCount: number
  isDaily: boolean
  arrival: string
  onClose: () => void
}) {
```
Make the hour cells non-interactive on a daily trip (line 153):
```tsx
              <button type="button" className={cls.join(' ')} disabled={isDaily} onClick={isDaily ? undefined : () => setPicked(h)}>
```
Gate the suggestion/apply card so it never shows on a daily trip, replacing it with a short read-only note (line 165 — change the opening of the `{preview && (` block):
```tsx
      {isDaily ? (
        <div className="sd-sugg"><p className="sd-sugg-line">โหมดประจำวันเริ่มจากเวลาปัจจุบันเสมอ — ปรับเวลาตามอากาศไม่ได้</p></div>
      ) : preview && (
        <div className="sd-sugg">
          {/* …existing suggestion/apply card unchanged… */}
        </div>
      )}
```
(Leave the hours grid above intact so the display-only hourly temperatures still render.)

- [ ] **Step 6: Verify types + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 7: Interactive verify**

On a **daily** trip's itinerary: the "ใช้เวลาปัจจุบันเสมอ" checkbox is checked + disabled (labelled "โหมดประจำวัน"); opening a stop's "ดูอุณหภูมิรายชั่วโมง" shows the hourly strip but the hour cells are not selectable and the apply card is replaced by the read-only note. On an **ordinary single-day** (non-daily) trip: retiming still works normally (regression check — this is why the gate is `isDaily`, not `tripDayCount`).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/components/ItineraryTab.tsx \
        frontend/src/pages/trips/components/DayStartEditor.tsx \
        frontend/src/pages/trips/components/StopDetailSheet.tsx \
        frontend/src/pages/trips/components/HourlyPlanner.tsx
git commit -m "feat(trips): lock current-time toggle + hide retiming apply on daily trips (#49)"
```

---

### Task 11: Full verification, prod migration, and push

**Files:** none (verification + ops).

- [ ] **Step 1: Full local suite**

Run: `dotnet test backend/MenuNest.sln` then `cd frontend && npx tsc --noEmit && npm run build`
Expected: all green.

- [ ] **Step 2: Preview the migration SQL**

Run (from `backend`):
```bash
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Confirm it contains exactly the `ALTER TABLE [Trips] ADD [IsDaily] bit NOT NULL DEFAULT CAST(0 AS bit)` (idempotent-guarded) and nothing else unexpected.

- [ ] **Step 3: Apply the migration to prod BY HAND (before/with deploy)**

Per CLAUDE.md — ensure the terminal `az` session is `thodsaphonSP@hotmail.co.th`, add a temp firewall rule for your public IP if needed, then:
```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```
Remove the temp firewall rule afterward. **This must happen before the new code serves traffic** — Get/List project `IsDaily` server-side, so an unapplied column 500s.

- [ ] **Step 4: Interactive smoke test against the mock**

Create a daily trip (create dialog toggle), confirm it lands in the "ประจำวัน" section as "วันนี้", open it (itinerary shows today, toggle locked, retiming hidden), toggle daily off from the header (returns to normal list), and verify a multi-day trip's switch is disabled. Compare screens to Claude Design → Screens → "Issue #49 — ทริปประจำวัน (Daily trips)".

- [ ] **Step 5: Push**

```bash
git push main HEAD:main
```
The final feature commit (or a merge commit) should close the issue — ensure one commit subject/body carries `(closes #49)`.

---

## Self-Review

**1. Spec coverage:**
- D1 (no history, run-as-today) — reuses existing read-time reseed; no new storage. ✓ (Tasks 1-3 add no history table.)
- D2 (Trip + bool, many) — Task 1 (scalar), no uniqueness constraint. ✓
- D3 (enable = handler op: guard + force day flag) — Task 3 handler + Task 5 create path. ✓
- D4 (single-day guard, non-destructive) — Task 1 `SetDaily` + `Reschedule` guards. ✓
- D5 (backend-enforced evergreen, gated on IsDaily; UI lock) — Task 6 (backend) + Task 10 (UI). ✓
- D6 (tap opens itinerary, no auto-nav) — unchanged card `onClick` (Task 8). ✓
- D7 (section + card badge/"วันนี้", no HH:MM/stop count, emoji fix, section hidden when empty) — Task 8. ✓
- Command surface (Create + SetTripDaily, never Update) — Tasks 3-5; `UpdateTrip` untouched. ✓
- TripDto positional append + 4 sites — Task 2. ✓
- MCP (`set_trip_daily`, `create_trip` isDaily) — Tasks 4-5. ✓
- Manual prod migration ordering — Task 11. ✓

**2. Placeholder scan:** The one intentional "…existing card unchanged…" in Task 10 Step 5 refers to code already present in the file (the suggestion card body is not being changed — only its guard wrapper), so no code is omitted from a *new* write. All other steps carry full code.

**3. Type consistency:** `TripDto` gains `IsDaily`/`isDaily` (backend last positional / frontend interface) and is constructed with 7 args at all 4 sites (Task 2) and in `SetTripDailyHandler`/`CreateTripHandler` (Tasks 3, 5). `SetTripDailyCommand(Guid, bool)` is consistent across command/handler/controller/MCP. `ItineraryTab.isDaily` → `DayStartEditor.locked` + `planner.isDaily` → `HourlyPlanner.isDaily` chain is consistent (Task 10).
