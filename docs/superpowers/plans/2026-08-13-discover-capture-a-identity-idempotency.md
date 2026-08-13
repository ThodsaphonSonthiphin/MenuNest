# Discover Capture — Plan A: place identity & idempotency

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One physical place captured into two Trips shows as **one** card in Discover, and re-capturing a place already in a Trip returns the existing row instead of failing.

**Architecture:** `TripPlace` gains a nullable, opaque `Guid? OriginTripPlaceId` recording which existing row a copy was made from. It is never a foreign key and never indexed, because nothing queries by it — Discover's grouping runs in memory after `ToListAsync`. The value stored is always the **root**, computed once at read time and passed back verbatim by the client, so a chain of copies is structurally impossible. Alongside it, `AddTripPlaceHandler` becomes idempotent for an exact `place_id` already present in the target Trip, and copies the origin row's enrichment only when no `PlaceProfile` master supplied it.

**Tech Stack:** .NET 10, EF Core (SQL Server prod / SQLite + InMemory tests), Mediator, FluentValidation, xUnit + Moq + FluentAssertions, React 19 + Redux Toolkit Query (frontend passthrough).

**Spec:** `docs/superpowers/specs/2026-08-13-discover-capture-design.md` — this plan implements §2 (R2.1–R2.5) and §3 (R3.1–R3.6). Read the spec's Global Constraints before Task 1.

## Global Constraints

- UI copy is Thai. Backend error messages stay English and are not translated (ADR-145).
- Icons are inline-SVG components, never emoji. `@syncfusion/react-icons` is not installed.
- Three classes implement `IApplicationDbContext`: `AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`. **This plan adds no `DbSet<>`,** so none of them changes — a new scalar column is picked up automatically (`SqliteAppDbContext` applies the real Infrastructure configurations; `InMemoryAppDbContext` only mirrors value conversions).
- Backend tests use **Moq**, not NSubstitute. `Substitute.For<>` will not compile.
- An entity property and its EF configuration must land in the **same commit** — an invalid model fails EF validation for every test touching the `DbContext`, and the pre-commit hook runs the whole suite.
- **Migrations are applied to prod BY HAND** (Task 6). Nothing in the app or CD pipeline calls `Migrate()`.
- `git add <explicit paths>` only. Never `-A` or `.`. `daily-state.md` and `AGENTS.md` must never enter a commit.
- Every commit references the issue: `(#48)`, and the final one `(closes #48)` only when the whole feature ships — this plan is Plan A of four, so use `(#48)`.
- Pre-commit runs backend `dotnet build` + `dotnet test` (Release) **and** frontend `tsc --noEmit` + `npm run build`, ~40s+. The whole suite must be green at every commit.
- `AddTripPlaceCommand` is a **positional record with 10 construction sites** (listed in Task 2). Every new member takes a default value so existing positional calls keep compiling.

---

## File Structure

**Backend — modified:**
- `backend/src/MenuNest.Domain/Entities/TripPlace.cs` — the new `OriginTripPlaceId` property and a `Create` parameter for it.
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs` — one explicit `Property` line whose job is to document that the column is deliberately bare (no `HasOne`, no `HasIndex`).
- `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceCommand.cs` — five new members, all defaulted.
- `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs` — stores the origin key verbatim, applies copied enrichment when the master did not, and pre-checks for an exact `place_id` duplicate.
- `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs` — `DiscoverPlaceDto` gains `Guid OriginTripPlaceId`.
- `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs` — the group key and the flattened root on the DTO.

**Backend — created:**
- `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddTripPlaceOriginTripPlaceId.cs` — generated, one `AddColumn`.
- `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceOriginRelationalTests.cs` — the column round-trips through a real relational provider, which is the only test that proves the entity **and** its mapping together.
- `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceIdempotencyRelationalTests.cs` — idempotency against the real filtered unique index, which the InMemory provider silently ignores.
- `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesOriginGroupingTests.cs` — two rows with one root collapse to one card.

**Frontend — modified:**
- `frontend/src/shared/api/api.ts` — `DiscoverPlaceDto` gains `originTripPlaceId`; `AddTripPlaceArgs` gains the optional passthrough members.
- `frontend/src/pages/discover/components/AddToTripDialog.tsx` — passes `originTripPlaceId` through.
- `frontend/src/pages/discover/DiscoverPage.tsx` — `handleCreateTrip`'s `addTripPlace` payload passes it too, so both add-from-Discover paths stay in sync.

Files that change together live together: the entity and its configuration are one task because the build demands it, and the two Discover write paths are one task because they must not diverge.

---

### Task 1: The opaque origin column

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/TripPlace.cs:15-61`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs:75-80`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddTripPlaceOriginTripPlaceId.cs` (generated)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceOriginRelationalTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TripPlace.OriginTripPlaceId` (`Guid?`, private setter) and
  `TripPlace.Create(Guid tripId, string name, double lat, double lng, PlaceCategory category, string? googlePlaceId = null, string? address = null, int? priceLevel = null, string? photoUrl = null, string? openingHoursJson = null, Guid? originTripPlaceId = null)` — the new parameter is **last and defaulted**, so all existing `Create` calls compile unchanged.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceOriginRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-156: OriginTripPlaceId is a nullable, opaque Guid — no FK, no index. A relational
/// round-trip is the only test that proves the property AND its EF mapping together.
/// </summary>
public sealed class TripPlaceOriginRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;

    public TripPlaceOriginRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    private Trip SeedTrip()
    {
        var user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(user);
        var trip = Trip.Create(user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        _db.SaveChanges();
        return trip;
    }

    [Fact]
    public async Task Origin_defaults_to_null_and_round_trips_a_value()
    {
        var trip = SeedTrip();
        var root = TripPlace.Create(trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See);
        var copy = TripPlace.Create(trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
            originTripPlaceId: root.Id);
        _db.TripPlaces.AddRange(root, copy);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var reloadedRoot = await _db.TripPlaces.SingleAsync(p => p.Id == root.Id);
        var reloadedCopy = await _db.TripPlaces.SingleAsync(p => p.Id == copy.Id);

        reloadedRoot.OriginTripPlaceId.Should().BeNull("a fresh capture has no origin");
        reloadedCopy.OriginTripPlaceId.Should().Be(root.Id);
    }

    [Fact]
    public async Task Origin_may_reference_a_row_that_no_longer_exists()
    {
        // Opaque, not a foreign key: deletes are HARD, so a dangling value must persist and read back.
        var trip = SeedTrip();
        var vanished = Guid.NewGuid();
        var place = TripPlace.Create(trip.Id, "Orphan copy", 1, 2, PlaceCategory.Other,
            originTripPlaceId: vanished);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var reloaded = await _db.TripPlaces.SingleAsync(p => p.Id == place.Id);
        reloaded.OriginTripPlaceId.Should().Be(vanished);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceOriginRelationalTests`
Expected: FAIL — compile error `CS1739: The best overload for 'Create' does not have a parameter named 'originTripPlaceId'`.

- [ ] **Step 3: Add the property and the Create parameter**

In `TripPlace.cs`, after line 26 (`public string? Notes { get; private set; }`) add:

```csharp
    /// <summary>
    /// The existing TripPlace this row was copied FROM, when it was created by adding an
    /// existing Discover place to a Trip. Opaque by design (ADR-156): no FK — deletes are
    /// hard, so a dangling value is legal — and no index, because the only consumer groups
    /// in memory. Always holds the ROOT, never an intermediate parent, so no chain can form.
    /// </summary>
    public Guid? OriginTripPlaceId { get; private set; }
```

Change the `Create` signature (line 39-42) to add the defaulted parameter last:

```csharp
    public static TripPlace Create(
        Guid tripId, string name, double lat, double lng, PlaceCategory category,
        string? googlePlaceId = null, string? address = null, int? priceLevel = null,
        string? photoUrl = null, string? openingHoursJson = null, Guid? originTripPlaceId = null)
```

and add one line to the object initialiser, after `OpeningHoursJson = openingHoursJson,`:

```csharp
            OriginTripPlaceId = originTripPlaceId,
```

- [ ] **Step 4: Make the bare mapping explicit**

In `TripPlaceConfiguration.cs`, immediately before `b.HasIndex(p => p.TripId);` (line 75) add:

```csharp
        // ADR-156: opaque origin key. EF maps it automatically; this line exists to state
        // that the bareness is deliberate — do NOT add HasOne() or HasIndex() here. Nothing
        // queries by it (ListMyPlacesHandler groups in memory), and a FK would break the
        // hard-delete path it must survive.
        b.Property(p => p.OriginTripPlaceId);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceOriginRelationalTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Generate the migration**

Run:

```bash
cd backend
dotnet ef migrations add AddTripPlaceOriginTripPlaceId \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --output-dir Persistence/Migrations
```

Open the generated `Up` and confirm it is exactly one `AddColumn<Guid>` on `TripPlaces`, `nullable: true`, with **no** `CreateIndex` and **no** `AddForeignKey`. If EF emitted either, the configuration in Step 4 was changed beyond one `Property` line — fix that rather than hand-editing the migration.

- [ ] **Step 7: Verify the whole suite is green**

Run: `cd backend && dotnet test`
Expected: PASS. `Create`'s new parameter is defaulted and last, so none of the existing `TripPlace.Create` call sites needed touching.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/TripPlace.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations \
        backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceOriginRelationalTests.cs
git commit -m "feat(trips): TripPlace gains an opaque OriginTripPlaceId (#48)"
```

---

### Task 2: The command carries the origin key and the enrichment copy

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs:25-32`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceOriginRelationalTests.cs` (new file)

**Interfaces:**
- Consumes: `TripPlace.Create(..., Guid? originTripPlaceId = null)` from Task 1.
- Produces: `AddTripPlaceCommand` with five new **defaulted** members in this exact order after `OpeningHoursJson` — `Guid? OriginTripPlaceId = null`, `string? Notes = null`, `IReadOnlyList<ReviewLinkDto>? ReviewLinks = null`, `IReadOnlyList<BestTimeWindowDto>? BestTimeWindows = null`, `IReadOnlyList<SeasonPeriodDto>? SeasonPeriods = null`. Task 5 and Plans B–D construct it by **named** argument.

**Why defaults matter here:** `AddTripPlaceCommand` is a positional record with **10 construction sites**, and none of them may break. Two are production — `src/MenuNest.McpServer/Tools/TripTools.cs:106` and `src/MenuNest.WebApi/Controllers/TripsController.cs:75` — and eight are tests: `tests/.../Trips/AddTripPlaceHandlerTests.cs:23`, `:40`; `tests/.../Trips/PlaceProfileSeedRelationalTests.cs:64`, `:76`, `:87`, `:96`, `:97`; `tests/.../Trips/PlaceProfileWriteThroughRelationalTests.cs:105`. Appending defaulted members leaves all ten compiling untouched.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceOriginRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>ADR-156 §2/§4: the handler stores the origin key VERBATIM (no lookup, no
/// flattening) and applies the copied enrichment only when no PlaceProfile master did.</summary>
public sealed class AddTripPlaceOriginRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users = new();
    private readonly IValidator<AddTripPlaceCommand> _validator = new AddTripPlaceValidator();
    private readonly Trip _trip;

    public AddTripPlaceOriginRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(_trip);
        _db.SaveChanges();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private AddTripPlaceHandler NewAdd() => new(_db, _users.Object, _validator);

    [Fact]
    public async Task Stores_the_origin_key_verbatim()
    {
        var root = Guid.NewGuid();
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                null, null, null, null, null, OriginTripPlaceId: root),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.OriginTripPlaceId.Should().Be(root, "the client already sent the ROOT; the handler must not resolve it");
    }

    [Fact]
    public async Task Copies_enrichment_when_there_is_no_master()
    {
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                null, null, null, null, null,
                Notes: "shady 06:30-09:00",
                ReviewLinks: new[] { new ReviewLinkDto("https://www.tiktok.com/@a/video/1", "clip") }),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("shady 06:30-09:00");
        saved.ReviewLinks.Should().HaveCount(1);
        saved.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@a/video/1");
    }

    [Fact]
    public async Task Master_wins_over_the_copied_enrichment()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/MASTER", "Viewpoint");
        profile.SetNotes("from the master");
        _db.PlaceProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                "places/MASTER", null, null, null, null,
                Notes: "from the copy"),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("from the master", "SeedIntoAsync returned true, so the copy is not applied");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
```

> **Before running:** confirm `PlaceProfile.Create`'s signature and its notes setter with
> `grep -n "public static PlaceProfile Create\|public void SetNotes" backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`
> and adjust the third test's two lines to match. Everything else in this file is independent of it.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AddTripPlaceOriginRelationalTests`
Expected: FAIL — `CS1739: ... does not have a parameter named 'OriginTripPlaceId'`.

- [ ] **Step 3: Add the five defaulted members to the command**

Replace the body of `AddTripPlaceCommand.cs` with:

```csharp
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;

/// <summary>
/// The five trailing members are ADR-156's copy-at-add-time payload and are ALL defaulted:
/// AddTripPlaceCommand has 10 positional construction sites (2 production, 8 test) and none
/// of them may break. Construct the new members by name.
/// </summary>
public sealed record AddTripPlaceCommand(
    Guid TripId, string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson,
    Guid? OriginTripPlaceId = null,
    string? Notes = null,
    IReadOnlyList<ReviewLinkDto>? ReviewLinks = null,
    IReadOnlyList<BestTimeWindowDto>? BestTimeWindows = null,
    IReadOnlyList<SeasonPeriodDto>? SeasonPeriods = null)
    : ICommand<TripPlaceDto>;
```

- [ ] **Step 4: Store the key and apply the fallback enrichment**

In `AddTripPlaceHandler.Handle`, replace lines 25-29 with:

```csharp
        var place = TripPlace.Create(c.TripId, c.Name, c.Lat, c.Lng, c.Category,
            c.GooglePlaceId, c.Address, c.PriceLevel, c.PhotoUrl, c.OpeningHoursJson,
            c.OriginTripPlaceId);
        _db.TripPlaces.Add(place);
        var seeded = await PlaceProfileSync.SeedIntoAsync(_db, user.Id, place, ct);
        if (!seeded) ApplyCopiedEnrichment(place, c);
        await _db.SaveChangesAsync(ct);
```

and add this private static method to the class, above `ToDto`:

```csharp
    /// <summary>
    /// ADR-156 §4: the origin row's enrichment, copied at add-time — applied ONLY when no
    /// PlaceProfile master supplied it, so a master stays canonical. Mirrors what
    /// PlaceProfileSync.SeedIntoAsync already does for the master case.
    /// </summary>
    private static void ApplyCopiedEnrichment(TripPlace place, AddTripPlaceCommand c)
    {
        if (c.Notes is not null) place.SetNotes(c.Notes);
        if (c.ReviewLinks is { Count: > 0 })
            place.SetReviewLinks(c.ReviewLinks.Select(r => new ReviewLink(r.Url, r.Label)));
        if (c.BestTimeWindows is { Count: > 0 })
            place.SetBestTimeWindows(c.BestTimeWindows.Select(w => new BestTimeWindow(w.Start, w.End, w.Note)));
        if (c.SeasonPeriods is { Count: > 0 })
            place.SetSeasonPeriods(c.SeasonPeriods.Select(s => new SeasonPeriod(s.Kind, s.Months, s.Note)));
    }
```

> **Value-object constructors:** confirm each signature before running —
> `grep -rn "public .*ReviewLink(\|public .*BestTimeWindow(\|public .*SeasonPeriod(" backend/src/MenuNest.Domain/ValueObjects/`.
> Several in this codebase are static factories rather than constructors; if so, swap `new X(...)` for `X.From(...)` / `X.Create(...)` as that file dictates. The four `Set*` methods on `TripPlace` already enforce their caps (2000 chars, 10 links, 6 windows, 12 periods), so no extra validation belongs here.

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AddTripPlaceOriginRelationalTests`
Expected: PASS, 3 tests.

- [ ] **Step 6: Verify all ten construction sites still compile**

Run: `cd backend && dotnet build && dotnet test`
Expected: PASS, and **zero** edits needed in `TripTools.cs`, `TripsController.cs` or the eight test call sites. If any of them failed to compile, a new member was not defaulted or was inserted before `OpeningHoursJson` — fix the record, do not edit the call sites.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceCommand.cs \
        backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceOriginRelationalTests.cs
git commit -m "feat(trips): AddTripPlace stores the origin key and copies enrichment when no master (#48)"
```

---

### Task 3: The handler is idempotent for an exact place_id

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceIdempotencyRelationalTests.cs`

**Interfaces:**
- Consumes: everything from Task 2.
- Produces: no signature change. The behaviour later plans rely on: `AddTripPlace` with a `GooglePlaceId` already present on the target Trip **returns that existing row's `TripPlaceDto`** and inserts nothing. Plan B's `alreadySaved` field is the resolve-time twin of this; Plan D's picker relies on it so a double-tap cannot 500.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceIdempotencyRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-149 §1: an exact place_id match is idempotent — the existing row is returned, nothing
/// is inserted and nothing is merged. Relational, because the filtered unique index
/// (TripId, GooglePlaceId) WHERE GooglePlaceId IS NOT NULL is the backstop under this policy
/// and the InMemory provider ignores it.
/// </summary>
public sealed class AddTripPlaceIdempotencyRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users = new();
    private readonly IValidator<AddTripPlaceCommand> _validator = new AddTripPlaceValidator();
    private readonly Trip _trip;

    public AddTripPlaceIdempotencyRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(_trip);
        _db.SaveChanges();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private AddTripPlaceHandler NewAdd() => new(_db, _users.Object, _validator);

    private AddTripPlaceCommand Cmd(string? gpid, string name = "Cafe") =>
        new(_trip.Id, name, 18.79, 98.96, PlaceCategory.Cafe, gpid, null, null, null, null);

    [Fact]
    public async Task Same_place_id_twice_returns_the_existing_row_and_inserts_nothing()
    {
        var first = await NewAdd().Handle(Cmd("places/ChIJabc"), default);
        var second = await NewAdd().Handle(Cmd("places/ChIJabc", "Cafe renamed"), default);

        second.Id.Should().Be(first.Id, "the existing row is returned, not a second one");
        (await _db.TripPlaces.CountAsync(p => p.TripId == _trip.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Nothing_is_merged_onto_the_existing_row()
    {
        var first = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Cafe", 18.79, 98.96, PlaceCategory.Cafe,
                "places/ChIJabc", null, null, null, null, Notes: "original"),
            default);

        await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Cafe", 18.79, 98.96, PlaceCategory.Eat,
                "places/ChIJabc", null, null, null, null, Notes: "should be discarded"),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == first.Id);
        saved.Notes.Should().Be("original", "a capture is not an edit");
        saved.Category.Should().Be(PlaceCategory.Cafe);
    }

    [Fact]
    public async Task Two_place_id_less_captures_of_one_spot_remain_two_rows()
    {
        // ADR-149 §2: the filtered unique index excludes NULL place ids, deliberately — the
        // 100 m proximity NOTICE is the only signal here, and it only warns.
        await NewAdd().Handle(Cmd(null, "Stall A"), default);
        await NewAdd().Handle(Cmd(null, "Stall B"), default);

        (await _db.TripPlaces.CountAsync(p => p.TripId == _trip.Id)).Should().Be(2);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AddTripPlaceIdempotencyRelationalTests`
Expected: FAIL — `Same_place_id_twice...` throws `DbUpdateException` from the unique index (today's 500), and `Nothing_is_merged...` fails the same way. `Two_place_id_less...` already passes.

- [ ] **Step 3: Add the pre-check**

In `AddTripPlaceHandler.Handle`, insert immediately after the ownership guard (`if (!owns) throw new DomainException("Trip not found.");`):

```csharp
        // ADR-149 §1: an exact place_id already on this Trip is idempotent — return the existing
        // row rather than inserting a second one or letting the filtered unique index 500. The
        // policy lives here so the SPA, MCP and a race all get the same answer. Nothing is
        // merged: a capture is not an edit, and the user did not ask for one.
        if (!string.IsNullOrEmpty(c.GooglePlaceId))
        {
            var existing = await _db.TripPlaces
                .FirstOrDefaultAsync(p => p.TripId == c.TripId && p.GooglePlaceId == c.GooglePlaceId, ct);
            if (existing is not null)
            {
                var hasMaster = await _db.PlaceProfiles
                    .AnyAsync(p => p.UserId == user.Id && p.GooglePlaceId == c.GooglePlaceId, ct);
                return ToDto(existing, hasMaster);
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AddTripPlaceIdempotencyRelationalTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Verify the whole suite is green**

Run: `cd backend && dotnet test`
Expected: PASS. Pay attention to `PlaceProfileSeedRelationalTests` — it adds several places with distinct `places/…` ids to one trip, so none of them hits the new pre-check.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceIdempotencyRelationalTests.cs
git commit -m "fix(trips): AddTripPlace is idempotent for an exact place_id, no longer a 500 (#48)"
```

---

### Task 4: Discover groups by the flattened root

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs:14-31`
- Modify: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs:41`, `:67-83`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesOriginGroupingTests.cs`

**Interfaces:**
- Consumes: `TripPlace.OriginTripPlaceId` from Task 1.
- Produces: `DiscoverPlaceDto` gains **`Guid OriginTripPlaceId`** as its **last** positional member, valued `rep.OriginTripPlaceId ?? rep.Id` — already flattened, so a client may pass it straight back into `AddTripPlaceCommand.OriginTripPlaceId`. Task 5 and Plan B's `list_my_places` both read it. `DiscoverPlaceDto` has exactly **one** construction site (`ListMyPlacesHandler.cs:67`).

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesOriginGroupingTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

/// <summary>ADR-156 §2/§3: two place_id-less rows sharing one root collapse to ONE Discover
/// card, and the DTO reports the already-flattened root so no chain can form.</summary>
public sealed class ListMyPlacesOriginGroupingTests
{
    private static (InMemoryAppDbContext db, User user) NewDb()
    {
        var db = new InMemoryAppDbContext(
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        db.Users.Add(user);
        db.SaveChanges();
        return (db, user);
    }

    private static ListMyPlacesHandler NewHandler(InMemoryAppDbContext db, User user)
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return new ListMyPlacesHandler(db, users.Object);
    }

    [Fact]
    public async Task Two_trips_one_root_is_one_card_carrying_the_root()
    {
        var (db, user) = NewDb();
        var octTrip = Trip.Create(user.Id, "Oct", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        var decTrip = Trip.Create(user.Id, "Dec", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        db.Trips.AddRange(octTrip, decTrip);

        var root = TripPlace.Create(octTrip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See);
        var copy = TripPlace.Create(decTrip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
            originTripPlaceId: root.Id);
        db.TripPlaces.AddRange(root, copy);
        await db.SaveChangesAsync();

        var result = await NewHandler(db, user).Handle(new ListMyPlacesQuery(), default);

        result.Should().HaveCount(1, "one physical place is one card");
        result[0].OriginTripPlaceId.Should().Be(root.Id);
        result[0].Trips.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_lone_capture_reports_its_own_id_as_the_root()
    {
        var (db, user) = NewDb();
        var trip = Trip.Create(user.Id, "Solo", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "Stall", 13.75, 100.50, PlaceCategory.Eat);
        db.TripPlaces.Add(place);
        await db.SaveChangesAsync();

        var result = await NewHandler(db, user).Handle(new ListMyPlacesQuery(), default);

        result.Should().HaveCount(1);
        result[0].OriginTripPlaceId.Should().Be(place.Id, "its own id IS the root");
    }

    [Fact]
    public async Task A_place_id_still_wins_over_the_origin_key()
    {
        // The column is inert for the common case: same place_id, two trips, still one card.
        var (db, user) = NewDb();
        var a = Trip.Create(user.Id, "A", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        var b = Trip.Create(user.Id, "B", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        db.Trips.AddRange(a, b);
        db.TripPlaces.Add(TripPlace.Create(a.Id, "Cafe", 1, 2, PlaceCategory.Cafe, "places/ChIJabc"));
        db.TripPlaces.Add(TripPlace.Create(b.Id, "Cafe", 1, 2, PlaceCategory.Cafe, "places/ChIJabc"));
        await db.SaveChangesAsync();

        var result = await NewHandler(db, user).Handle(new ListMyPlacesQuery(), default);
        result.Should().HaveCount(1);
        result[0].Trips.Should().HaveCount(2);
    }

    [Fact]
    public async Task Two_unrelated_place_id_less_rows_stay_two_cards()
    {
        var (db, user) = NewDb();
        var trip = Trip.Create(user.Id, "T", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        db.Trips.Add(trip);
        db.TripPlaces.Add(TripPlace.Create(trip.Id, "Stall A", 1, 2, PlaceCategory.Eat));
        db.TripPlaces.Add(TripPlace.Create(trip.Id, "Stall B", 1, 2, PlaceCategory.Eat));
        await db.SaveChangesAsync();

        var result = await NewHandler(db, user).Handle(new ListMyPlacesQuery(), default);
        result.Should().HaveCount(2, "no shared root, so no grouping");
    }
}
```

> **Before running:** confirm `ListMyPlacesQuery` is parameterless with
> `cat backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesQuery.cs`, and check how the existing Places tests build `InMemoryAppDbContext` (`ls backend/tests/MenuNest.Application.UnitTests/Places/`) — reuse their helper if one exists rather than the local `NewDb` above.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListMyPlacesOriginGroupingTests`
Expected: FAIL — `CS1061: 'DiscoverPlaceDto' has no member 'OriginTripPlaceId'`.

- [ ] **Step 3: Add the DTO member**

In `PlaceDtos.cs`, add as the **last** member of `DiscoverPlaceDto`, after `string? Notes`:

```csharp
    /// <summary>
    /// ADR-156: the group's ROOT TripPlace id, already flattened (rep.OriginTripPlaceId ?? rep.Id).
    /// A client passes this straight back as AddTripPlaceCommand.OriginTripPlaceId, which is what
    /// makes a chain of copies structurally impossible — the write path performs no lookup.
    /// </summary>
    Guid OriginTripPlaceId);
```

(replacing the existing closing `string? Notes);` with `string? Notes,` first).

- [ ] **Step 4: Change the group key and populate the member**

In `ListMyPlacesHandler.cs`, replace line 41:

```csharp
        // ADR-156 §3: GooglePlaceId still wins whenever present, so the origin key is inert for
        // the common case; it only groups place_id-less rows copied from one root.
        var groups = rows.GroupBy(r => r.Place.GooglePlaceId ?? $"tp:{r.Place.OriginTripPlaceId ?? r.Place.Id}").ToList();
```

and add the new argument as the **last** one in the `new DiscoverPlaceDto(...)` call, after `notes`:

```csharp
                notes,
                rep.OriginTripPlaceId ?? rep.Id));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListMyPlacesOriginGroupingTests`
Expected: PASS, 4 tests.

- [ ] **Step 6: Verify the whole suite is green**

Run: `cd backend && dotnet test`
Expected: PASS. `DiscoverPlaceDto` has one construction site, so nothing else needed touching.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs \
        backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesOriginGroupingTests.cs
git commit -m "feat(discover): group by the flattened origin root so one place is one card (#48)"
```

---

### Task 5: The SPA passes the root back

**Files:**
- Modify: `frontend/src/shared/api/api.ts` — the `DiscoverPlaceDto` interface and the `addTripPlace` mutation's argument type
- Modify: `frontend/src/pages/discover/components/AddToTripDialog.tsx`
- Modify: `frontend/src/pages/discover/DiscoverPage.tsx:65-77`

**Interfaces:**
- Consumes: `DiscoverPlaceDto.originTripPlaceId` (Task 4) and `AddTripPlaceCommand.OriginTripPlaceId` (Task 2).
- Produces: both Discover add paths send `originTripPlaceId`. Plan C's capture component uses the same `addTripPlace` argument shape.

**Why both files:** `AddToTripDialog` (add to an existing Trip) and `DiscoverPage.handleCreateTrip` (create-and-seed, ADR-098) are the two places that turn a Discover card into a `TripPlace`. `DiscoverPage.tsx:54-56` already carries a comment that they must stay in sync; if only one passes the key, half the copies split into a second card.

- [ ] **Step 1: Write the failing test**

The SPA's vitest runs in `environment: 'node'` with no jsdom, so a component test is impossible here — the type system is the gate. Add a compile-time assertion instead. Create `frontend/src/pages/discover/lib/originPassthrough.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import type {DiscoverPlaceDto} from '../../../shared/api/api'
import {addTripPlaceArgsFor} from './originPassthrough'

const card: DiscoverPlaceDto = {
  key: 'tp:11111111-1111-1111-1111-111111111111',
  googlePlaceId: null,
  name: 'จุดชมวิวก่อนถึงดอย',
  lat: 18.79641,
  lng: 98.96783,
  address: null,
  category: 'See',
  priceLevel: null,
  photoUrl: null,
  openingHoursJson: null,
  bestTimeWindows: [],
  seasonPeriods: [],
  visited: false,
  trips: [],
  reviewLinks: [],
  notes: null,
  originTripPlaceId: '11111111-1111-1111-1111-111111111111',
}

describe('addTripPlaceArgsFor', () => {
  it('carries the flattened root so the copy joins the same Discover card', () => {
    expect(addTripPlaceArgsFor('trip-1', card).originTripPlaceId)
      .toBe('11111111-1111-1111-1111-111111111111')
  })

  it('carries the enrichment the master may not supply', () => {
    const withNote: DiscoverPlaceDto = {...card, notes: 'ร่มเงาดี', reviewLinks: [{url: 'https://x', label: null}]}
    const args = addTripPlaceArgsFor('trip-1', withNote)
    expect(args.notes).toBe('ร่มเงาดี')
    expect(args.reviewLinks).toHaveLength(1)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/discover/lib/originPassthrough.test.ts`
Expected: FAIL — cannot resolve `./originPassthrough`, and `originTripPlaceId` is not a property of `DiscoverPlaceDto`.

- [ ] **Step 3: Add the two API types**

In `api.ts`, add to the `DiscoverPlaceDto` interface:

```ts
  /** ADR-156: the group's already-flattened root TripPlace id. Pass it straight back on add. */
  originTripPlaceId: string
```

and add these optional members to the `addTripPlace` mutation's argument type (the object type whose other members are `tripId`, `googlePlaceId`, `name`, `lat`, `lng`, `address`, `category`, `priceLevel`, `photoUrl`, `openingHoursJson`, `reviewLinks`):

```ts
  originTripPlaceId?: string
  notes?: string | null
  bestTimeWindows?: BestTimeWindowDto[]
  seasonPeriods?: SeasonPeriodDto[]
```

> Note the existing mutation `Omit`s the enrichment members (ADR-156 §4 names `api.ts:1395`); stop omitting the four above. Reuse the `BestTimeWindowDto` / `SeasonPeriodDto` / `ReviewLinkDto` types already exported from this file — do not declare new ones.

- [ ] **Step 4: Extract the shared argument builder**

Create `frontend/src/pages/discover/lib/originPassthrough.ts`:

```ts
import type {DiscoverPlaceDto} from '../../../shared/api/api'

/**
 * The single payload shape both Discover add paths use — AddToTripDialog (add to an
 * existing Trip) and DiscoverPage.handleCreateTrip (create-and-seed, ADR-098). Extracted
 * so they cannot drift: if only one passed originTripPlaceId, half the copies would split
 * into a second Discover card, which is the defect ADR-156 exists to prevent.
 *
 * Lives in lib/ because the SPA's vitest has no DOM harness — pure functions are the only
 * frontend logic that gets real unit coverage.
 */
export function addTripPlaceArgsFor(tripId: string, place: DiscoverPlaceDto) {
  return {
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
    originTripPlaceId: place.originTripPlaceId,
    notes: place.notes,
    reviewLinks: place.reviewLinks,
    bestTimeWindows: place.bestTimeWindows,
    seasonPeriods: place.seasonPeriods,
  }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/discover/lib/originPassthrough.test.ts`
Expected: PASS, 2 tests.

- [ ] **Step 6: Use the builder in both call sites**

In `DiscoverPage.tsx`, replace the `addTripPlace({...})` argument inside `handleCreateTrip` (lines 65-77) with:

```tsx
      await addTripPlace(addTripPlaceArgsFor(trip.id, place)).unwrap()
```

and add the import:

```tsx
import {addTripPlaceArgsFor} from './lib/originPassthrough'
```

Then open `AddToTripDialog.tsx`, find its `addTripPlace({...})` call, and replace its argument object with `addTripPlaceArgsFor(<its trip id variable>, place)` plus the same import (path `'../lib/originPassthrough'`). Delete the now-duplicated field list — do not leave both.

- [ ] **Step 7: Verify typecheck, build and the full suite**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`
Expected: all PASS. `tsc` is what catches a missed field, since `originTripPlaceId` is required on `DiscoverPlaceDto`.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/discover/lib/originPassthrough.ts \
        frontend/src/pages/discover/lib/originPassthrough.test.ts \
        frontend/src/pages/discover/components/AddToTripDialog.tsx \
        frontend/src/pages/discover/DiscoverPage.tsx
git commit -m "feat(discover): both add paths pass the flattened origin root and enrichment (#48)"
```

---

### Task 6: Apply the migration to production

**Files:** none — this is a runbook step, and it is **mandatory**. Nothing in `Program.cs` or `.github/workflows/main_menunest.yml` applies migrations. Deploying Task 4's code without this produces `Invalid column name 'OriginTripPlaceId'` on **every** Discover request and a trips-wide 500 — the exact #49 outage.

**Interfaces:**
- Consumes: the migration generated in Task 1.
- Produces: a prod schema that matches the model. Plans B, C and D all assume this has run.

- [ ] **Step 1: Confirm the terminal `az` session is the personal account**

Run: `az account show --query "{sub:name, user:user.name}" -o json`
Expected: `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th`. Any other account is the **work** subscription — stop and re-login. Never create or modify anything in `AzureSubscriptionInALSO`.

- [ ] **Step 2: Preview the SQL before touching prod**

```bash
cd backend
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --output /tmp/origin-column.sql
```

Read it. Expected: one `ALTER TABLE [TripPlaces] ADD [OriginTripPlaceId] uniqueidentifier NULL;` guarded by the migrations-history check. **No** `CREATE INDEX`, **no** `ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY`, no data movement. If you see any of those, stop — Task 1 Step 4 was over-configured.

- [ ] **Step 3: Open the firewall for your current IP, temporarily**

```bash
IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP
```

- [ ] **Step 4: Apply it**

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` is **required** — without it SqlClient's "Active Directory Default" picks the Visual Studio **work** account and the login fails against the personal-tenant server.

- [ ] **Step 5: Verify the column exists, then close the firewall**

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations list \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

Expected: `AddTripPlaceOriginTripPlaceId` listed **without** a `(Pending)` marker. Then remove the rule regardless of the outcome:

```bash
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

- [ ] **Step 6: Smoke-test Discover on prod after the deploy lands**

Open the deployed SPA, go to ไปไหนดี, and confirm the list renders and a card opens. A missing column would surface here as "An unexpected error occurred." Cross-check with:

```bash
az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 \
  --analytics-query "AppExceptions | where TimeGenerated > ago(30m) | where OuterMessage has 'OriginTripPlaceId' | project TimeGenerated, OuterMessage" -o json
```

Expected: `[]`. (Use `log-analytics query`, not `app-insights query` — the classic API returns `[]` here even when data exists.)

---

## Self-Review

**1. Spec coverage.** R2.1 → Task 1. R2.2 → Task 2 (verbatim store) + Task 5 (unconditional pass-through). R2.3 → Task 4 Step 4 (flatten at read) + Task 2 Step 4 (store, no lookup). R2.4 → Task 4 Step 4. R2.5 → Task 2 Steps 3-4. R3.1 is resolve-time and belongs to **Plan B** — noted, not a gap in A. R3.2 → Task 3. R3.3 → Task 3 Step 1 test 2. R3.4 is SPA copy and belongs to **Plan C**. R3.5 (100 m near match) is resolve-time — **Plan B**. R3.6 → Task 3 Step 1 test 3 (the index is unchanged and the test pins that in).

**2. Placeholder scan.** Three `>` notes ask the implementer to confirm a signature before running (`PlaceProfile.Create`, the three value-object constructors, `ListMyPlacesQuery`). Each names the exact `grep`/`cat` to run and what to do with the answer, and each is isolated to lines that do not affect the rest of its file. These are verification steps against real code, not deferred decisions.

**3. Type consistency.** `OriginTripPlaceId` is spelled identically in the entity, the EF configuration, the command, both DTOs and the migration name; the frontend uses the camelCase `originTripPlaceId` throughout, matching the SPA's existing JSON convention. `addTripPlaceArgsFor(tripId, place)` has one definition and two call sites. `TripPlace.Create`'s new parameter is `originTripPlaceId` (camelCase parameter, PascalCase property) — consistent with the other nine parameters on that method.
