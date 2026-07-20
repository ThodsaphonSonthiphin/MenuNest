# Discover Review-links + Place-note Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a place's Review links (TikTok/relate links) and a free-text Place note — read-only — on the Discover ("ไปไหนดี") detail sheet, sourced from the User's master `PlaceProfile`.

**Architecture:** `Notes` becomes a field on the master `PlaceProfile`. `update_trip_place` write-throughs `Notes` + `ReviewLinks` to that master on every save so it is never stale. The Discover read (`GET /api/places`) joins the master and surfaces both on a widened `DiscoverPlaceDto`; the Discover `PlaceSheet` and the trip `StopDetailSheet` render them.

**Tech Stack:** .NET 10 / EF Core (Clean-arch: Domain / Application / Infrastructure / WebApi + McpServer), xUnit + Moq + FluentAssertions (relational tests via `SqliteAppDbContext`); React + TypeScript SPA, RTK Query, vitest (`node` env — no jsdom).

**Spec:** [docs/superpowers/specs/2026-07-20-discover-review-note-design.md](../specs/2026-07-20-discover-review-note-design.md) · **ADRs:** 101–104 · **Confirmed mock:** MenuNest design system → Screens → `issue-44-discover-review-note`.

## Global Constraints

- **Pre-commit hook runs the FULL suite** (`frontend/.husky/pre-commit`, `set -e`): backend `dotnet build`+`dotnet test` (Release) and frontend `tsc --noEmit`+`npm run build` (~40s). **Every commit must leave the entire suite green.** Never `--no-verify`.
- **Stage narrowly:** always `git add <explicit paths>`. Never `git add -A`/`.`. Never sweep `daily-state.md` or `AGENTS.md` into a commit.
- **Every commit references the ticket:** use `(#44)` in the subject (this issue is multi-commit; do **not** auto-close — leave a `Closes #44` for a final human decision).
- **Testing libraries:** xUnit + **Moq** (NOT NSubstitute) + FluentAssertions. Relational handler tests use `SqliteAppDbContext` (applies real EF configs).
- **Three `IApplicationDbContext` implementers** (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`): adding a **scalar** property to an existing entity needs **no** `DbSet` change (auto-mapped) — but a **new** `DbSet<>` would need all three. This plan adds no new `DbSet`.
- **Migrations are applied to prod BY HAND** (neither app nor CD runs `Database.Migrate()`). The entity change + its EF configuration/mapping + the migration must all land in the **same commit** (EF model validation).
- **Icons are inline SVG components, never emoji** (`ReviewIcon`).
- **No frontend component/DOM test harness** — UI rendering is not unit-testable; interactive verification is REQUIRED before considering UI done (map/overlay black-screen lesson, #36).
- **Git remote is `main`** (not `origin`); push only when the user asks, with `git push main HEAD:main`.
- **Note length cap:** 2000 chars. **Review-link caps** (unchanged): URL http/https ≤500, label ≤80, ≤10 per place.

---

### Task 0: Commit the design docs (prevent orphaned ADRs/spec)

Grill-then-plan wrote ADRs 101–104, the spec, this plan, and CONTEXT.md glossary edits — all uncommitted. Commit them first so they don't orphan (SDD implementers stage only code+tests).

**Files:**
- Add: `docs/adr/101-place-note-is-master-attribute.md`, `docs/adr/102-discover-surfaces-reviewlinks-and-notes-from-master.md`, `docs/adr/103-notes-and-reviewlinks-write-through-to-master.md`, `docs/adr/104-discover-and-stop-detail-display-of-reviews-and-note.md`
- Add: `docs/superpowers/specs/2026-07-20-discover-review-note-design.md`, `docs/superpowers/plans/2026-07-20-discover-review-note.md`
- Modify: `CONTEXT.md`

- [ ] **Step 1: Stage exactly these docs**

```bash
git add docs/adr/101-place-note-is-master-attribute.md \
        docs/adr/102-discover-surfaces-reviewlinks-and-notes-from-master.md \
        docs/adr/103-notes-and-reviewlinks-write-through-to-master.md \
        docs/adr/104-discover-and-stop-detail-display-of-reviews-and-note.md \
        docs/superpowers/specs/2026-07-20-discover-review-note-design.md \
        docs/superpowers/plans/2026-07-20-discover-review-note.md \
        CONTEXT.md
```

- [ ] **Step 2: Commit** (pre-commit runs the full suite even for docs — expect ~40s, it should pass unchanged)

```bash
git commit -m "docs(discover): ADRs 101-104 + design spec/plan + glossary for review-links & note (#44)"
```

---

### Task 1: `PlaceProfile.Notes` + `TripPlace.SetNotes` + EF mapping + migration

Add the master `Notes` field and a granular `TripPlace.SetNotes` (for seeding), map the column, and generate the migration — all in one commit (EF model validation).

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/TripPlace.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/PlaceProfileNotesTests.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceNotesTests.cs`
- Create (generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/*_AddPlaceProfileNotes.cs`

**Interfaces:**
- Produces: `PlaceProfile.Notes` (`string?`), `PlaceProfile.SetNotes(string?)`, `TripPlace.SetNotes(string?)`.

- [ ] **Step 1: Write the failing domain tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/PlaceProfileNotesTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public sealed class PlaceProfileNotesTests
{
    private static PlaceProfile New() => PlaceProfile.Create(Guid.NewGuid(), "places/x");

    [Fact]
    public void SetNotes_trims_and_stores()
    {
        var p = New();
        p.SetNotes("  จอดรถลานล่าง  ");
        p.Notes.Should().Be("จอดรถลานล่าง");
    }

    [Fact]
    public void SetNotes_blank_becomes_null()
    {
        var p = New();
        p.SetNotes("   ");
        p.Notes.Should().BeNull();
    }

    [Fact]
    public void SetNotes_rejects_over_2000_chars()
    {
        var p = New();
        var act = () => p.SetNotes(new string('a', 2001));
        act.Should().Throw<DomainException>();
    }
}
```

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceNotesTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public sealed class TripPlaceNotesTests
{
    private static TripPlace New() => TripPlace.Create(Guid.NewGuid(), "P", 1, 2, PlaceCategory.See, "gp");

    [Fact]
    public void SetNotes_trims_stores_and_nulls_blank()
    {
        var p = New();
        p.SetNotes("  hi  ");
        p.Notes.Should().Be("hi");
        p.SetNotes("   ");
        p.Notes.Should().BeNull();
    }

    [Fact]
    public void SetNotes_rejects_over_2000_chars()
    {
        var p = New();
        var act = () => p.SetNotes(new string('a', 2001));
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Domain.PlaceProfileNotesTests|FullyQualifiedName~Domain.TripPlaceNotesTests"`
Expected: FAIL — `PlaceProfile` / `TripPlace` do not contain a definition for `SetNotes` (compile error).

- [ ] **Step 3: Add `Notes` + `SetNotes` to `PlaceProfile`**

In `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`, add the property after `BestTimeEnd` (line ~18) and the setter alongside `SetBestTime`:

```csharp
public string? Notes { get; private set; }
```

```csharp
public void SetNotes(string? notes)
{
    var n = notes?.Trim();
    if (n is { Length: > 2000 }) throw new DomainException("Place note is too long (max 2000).");
    Notes = string.IsNullOrEmpty(n) ? null : n;
    UpdatedAt = DateTime.UtcNow;
}
```

- [ ] **Step 4: Add `SetNotes` to `TripPlace`**

In `backend/src/MenuNest.Domain/Entities/TripPlace.cs`, add alongside the existing setters (near `SetReviewLinks`):

```csharp
public void SetNotes(string? notes)
{
    var n = notes?.Trim();
    if (n is { Length: > 2000 }) throw new DomainException("Place note is too long (max 2000).");
    Notes = string.IsNullOrEmpty(n) ? null : n;
    UpdatedAt = DateTime.UtcNow;
}
```

- [ ] **Step 5: Run the domain tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Domain.PlaceProfileNotesTests|FullyQualifiedName~Domain.TripPlaceNotesTests"`
Expected: PASS.

- [ ] **Step 6: Map the `Notes` column**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`, inside `Configure(EntityTypeBuilder<PlaceProfile> b)` (after the `GooglePlaceId` property, before the JSON converters):

```csharp
b.Property(p => p.Notes).HasColumnName("Notes").HasMaxLength(2000);
```

- [ ] **Step 7: Generate the migration**

Run:
```bash
cd backend
dotnet ef migrations add AddPlaceProfileNotes \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: a new `..._AddPlaceProfileNotes.cs` under `src/MenuNest.Infrastructure/Persistence/Migrations/` whose `Up` calls `migrationBuilder.AddColumn<string>(name: "Notes", table: "PlaceProfiles", type: "nvarchar(2000)", nullable: true)`. Open it and confirm it contains **only** that column addition (no unexpected drops).

- [ ] **Step 8: Run the full backend suite**

Run: `cd backend && dotnet test`
Expected: PASS (all existing tests still green; the new column auto-maps on `SqliteAppDbContext`).

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/PlaceProfile.cs \
        backend/src/MenuNest.Domain/Entities/TripPlace.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations \
        backend/tests/MenuNest.Application.UnitTests/Trips/Domain/PlaceProfileNotesTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceNotesTests.cs
git commit -m "feat(places): add Notes to PlaceProfile master + TripPlace.SetNotes + migration (#44)"
```

---

### Task 2: Write-through `Notes` + `ReviewLinks` to the master on `update_trip_place`

Make `notes`/`reviewLinks` propagate to the master on every save (ADR-103); seed the note on capture; keep best-time/season/checklist push-only.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (tool `[Description]` only)
- Create: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileWriteThroughRelationalTests.cs`

**Interfaces:**
- Consumes: `PlaceProfile.SetNotes`, `TripPlace.SetNotes` (Task 1).
- Produces: `PlaceProfileSync.WriteThroughNotesAndLinksAsync(IApplicationDbContext, Guid userId, TripPlace, CancellationToken)`; `SeedIntoAsync`/`UpsertFromAsync` now also carry `Notes`.

- [ ] **Step 1: Write the failing relational tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileWriteThroughRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileWriteThroughRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileWriteThroughRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(_trip);
        _db.SaveChanges();
    }

    private Mock<IUserProvisioner> Users()
    {
        var m = new Mock<IUserProvisioner>();
        m.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return m;
    }

    private Guid AddPlace(string? placeId)
    {
        var place = TripPlace.Create(_trip.Id, "P", 1, 2, PlaceCategory.See, placeId);
        _db.TripPlaces.Add(place);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return place.Id;
    }

    private UpdateTripPlaceCommand Cmd(Guid placeId, string? notes, ReviewLinkDto[] links, TimeOnly? bestStart = null, TimeOnly? bestEnd = null)
        => new(_trip.Id, placeId, "P", PlaceCategory.See, null, null, notes, bestStart, bestEnd, links, Array.Empty<SeasonPeriodDto>());

    [Fact]
    public async Task First_save_snapshots_note_into_the_new_master()
    {
        var placeId = AddPlace("places/W1");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "hello", Array.Empty<ReviewLinkDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/W1");
        profile.Notes.Should().Be("hello");
    }

    [Fact]
    public async Task Second_save_write_throughs_notes_and_reviewlinks_but_not_best_time()
    {
        var placeId = AddPlace("places/W2");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "A",
            new[] { new ReviewLinkDto("https://tiktok.com/a", null) }, new TimeOnly(9, 0), new TimeOnly(11, 0)), default);
        await handler.Handle(Cmd(placeId, "B",
            new[] { new ReviewLinkDto("https://youtu.be/b", "clip") }, new TimeOnly(14, 0), new TimeOnly(15, 0)), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/W2");
        profile.Notes.Should().Be("B");                                   // write-through
        profile.ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://youtu.be/b"); // write-through
        profile.BestTimeStart.Should().Be(new TimeOnly(9, 0));            // push-only: unchanged from first save
    }

    [Fact]
    public async Task Save_with_no_place_id_creates_no_master()
    {
        var placeId = AddPlace(null);
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "note", Array.Empty<ReviewLinkDto>()), default);
        (await _db.Set<PlaceProfile>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capture_seeds_note_from_an_existing_master()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/W3");
        profile.SetNotes("seeded note");
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var add = new AddTripPlaceHandler(_db, Users().Object, new AddTripPlaceValidator());
        // AddTripPlaceCommand order (verified): (TripId, Name, Lat, Lng, Category, GooglePlaceId, Address, PriceLevel, PhotoUrl, OpeningHoursJson)
        var dto = await add.Handle(new AddTripPlaceCommand(_trip.Id, "P", 1, 2, PlaceCategory.See, "places/W3", null, null, null, null), default);

        dto.Notes.Should().Be("seeded note");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~PlaceProfileWriteThroughRelationalTests"`
Expected: FAIL — `Second_save...` sees `profile.Notes` null / old links (no write-through yet); `Capture_seeds...` sees `dto.Notes` null (seed doesn't copy notes yet). `WriteThroughNotesAndLinksAsync` may not exist (compile error is fine — implement next).

- [ ] **Step 3: Extend `PlaceProfileSync`**

In `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`:

In `SeedIntoAsync`, after `place.SetSeasonPeriods(profile.SeasonPeriods);`, add:
```csharp
place.SetNotes(profile.Notes);
```

In `UpsertFromAsync`, after `profile.SetSeasonPeriods(...)`, add:
```csharp
profile.SetNotes(place.Notes);
```

Add a new method (near `EnsureCreatedAsync`):
```csharp
/// <summary>Overwrite ONLY the master's Notes + ReviewLinks from the place (write-through, ADR-103).
/// No-op when the place has no GooglePlaceId or no master exists yet. Caller owns SaveChanges.</summary>
public static async Task WriteThroughNotesAndLinksAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
{
    if (string.IsNullOrEmpty(place.GooglePlaceId)) return;
    var profile = await db.PlaceProfiles
        .FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
    if (profile is null) return;
    profile.SetNotes(place.Notes);
    profile.SetReviewLinks(place.ReviewLinks);
}
```

- [ ] **Step 4: Wire the handler**

In `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`, replace the single line
`await PlaceProfileSync.EnsureCreatedAsync(_db, user.Id, place, ct);`
with:
```csharp
var createdMaster = await PlaceProfileSync.EnsureCreatedAsync(_db, user.Id, place, ct);
if (!createdMaster)
    await PlaceProfileSync.WriteThroughNotesAndLinksAsync(_db, user.Id, place, ct);
```

- [ ] **Step 5: Update the MCP tool description**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, in the `update_trip_place` `[McpServerTool, Description(...)]`, append to the description sentence: ` The notes and reviewLinks also propagate to the user's saved master profile for this place and appear on Discover immediately (no push needed).` (Signature and parameters are unchanged.)

- [ ] **Step 6: Run the full backend suite**

Run: `cd backend && dotnet test`
Expected: PASS. In particular `PlaceProfileAutoCreateRelationalTests.Second_save_does_not_overwrite_an_existing_master` must still pass (write-through touches only Notes/ReviewLinks, not best-time).

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs \
        backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileWriteThroughRelationalTests.cs
git commit -m "feat(places): write-through Notes + ReviewLinks to master; seed note on capture (#44)"
```

---

### Task 3: Widen `DiscoverPlaceDto` + join the master in `ListMyPlacesHandler`

Surface `reviewLinks` + `notes` on the Discover read model, from the master with an empty-aware fallback to the representative `TripPlace`.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs`

**Interfaces:**
- Consumes: `PlaceProfile.Notes`/`ReviewLinks` (Task 1), write-through freshness (Task 2).
- Produces: `DiscoverPlaceDto.ReviewLinks` (`IReadOnlyList<ReviewLinkDto>`) + `DiscoverPlaceDto.Notes` (`string?`), trailing.

- [ ] **Step 1: Write the failing tests**

In `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs`, add `using MenuNest.Domain.ValueObjects;` to the usings, then add:

```csharp
[Fact]
public async Task Surfaces_review_links_and_note_from_the_master()
{
    var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
    _db.Trips.Add(t);
    _db.TripPlaces.Add(TripPlace.Create(t.Id, "Wat", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-m"));
    var profile = PlaceProfile.Create(_user.Id, "gp-m");
    profile.SetNotes("master note");
    profile.SetReviewLinks(new[] { ReviewLink.Create("https://tiktok.com/x", "clip") });
    _db.Set<PlaceProfile>().Add(profile);
    await _db.SaveChangesAsync();

    var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

    result.Should().ContainSingle();
    result[0].Notes.Should().Be("master note");
    result[0].ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://tiktok.com/x");
}

[Fact]
public async Task Falls_back_to_trip_place_when_no_master_or_empty_master_links()
{
    var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
    _db.Trips.Add(t);
    var p = TripPlace.Create(t.Id, "NoMaster", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-n");
    p.SetNotes("trip note");
    p.SetReviewLinks(new[] { ReviewLink.Create("https://youtu.be/y", null) });
    _db.TripPlaces.Add(p);
    await _db.SaveChangesAsync(); // no PlaceProfile row for gp-n

    var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

    result.Should().ContainSingle();
    result[0].Notes.Should().Be("trip note");
    result[0].ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://youtu.be/y");
}

[Fact]
public async Task Place_with_no_reviews_or_note_surfaces_empty_and_null()
{
    var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
    _db.Trips.Add(t);
    _db.TripPlaces.Add(TripPlace.Create(t.Id, "Bare", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-z"));
    await _db.SaveChangesAsync();

    var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

    result[0].ReviewLinks.Should().BeEmpty();
    result[0].Notes.Should().BeNull();
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListMyPlacesHandlerTests"`
Expected: FAIL — `DiscoverPlaceDto` has no `Notes`/`ReviewLinks` (compile error).

- [ ] **Step 3: Widen the DTO**

In `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs`, append two trailing params to `DiscoverPlaceDto`:

```csharp
    bool Visited,
    IReadOnlyList<PlaceTripRefDto> Trips,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    string? Notes);
```

(`ReviewLinkDto` is in `MenuNest.Application.UseCases.Trips`, already imported by this file.)

- [ ] **Step 4: Join the master + map in the handler**

In `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs`, materialize the groups, load the relevant master profiles once, and source the two fields per group. Replace the `var groups = …;` through the `foreach` with:

```csharp
var groups = rows.GroupBy(r => r.Place.GooglePlaceId ?? $"tp:{r.Place.Id}").ToList();

var repGpids = groups
    .Select(g => g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place.GooglePlaceId)
    .Where(id => id != null).Select(id => id!).Distinct().ToList();
var profileByGpid = (await _db.PlaceProfiles
        .Where(p => p.UserId == user.Id && repGpids.Contains(p.GooglePlaceId))
        .ToListAsync(ct))
    .ToDictionary(p => p.GooglePlaceId);

var result = new List<DiscoverPlaceDto>();
foreach (var g in groups)
{
    var rep = g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place;
    var trips = g.Select(r => new PlaceTripRefDto(r.TripId, r.TripName))
                 .GroupBy(x => x.TripId).Select(x => x.First()).ToList();
    var visited = g.Any(r => visitedPlaceIds.Contains(r.Place.Id));

    var master = rep.GooglePlaceId != null && profileByGpid.TryGetValue(rep.GooglePlaceId, out var pf) ? pf : null;
    // Empty-aware: a null OR empty master list falls back to the rep TripPlace (heals #33 pre-write-through data).
    var reviewSrc = master?.ReviewLinks is { Count: > 0 } ml ? ml : rep.ReviewLinks;
    var reviewLinks = reviewSrc.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList();
    var notes = master?.Notes ?? rep.Notes;

    result.Add(new DiscoverPlaceDto(
        g.Key, rep.GooglePlaceId, rep.Name, rep.Lat, rep.Lng, rep.Address, rep.Category,
        rep.PriceLevel, rep.PhotoUrl, rep.OpeningHoursJson, rep.BestTimeStart, rep.BestTimeEnd,
        rep.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
        visited, trips, reviewLinks, notes));
}

return result.OrderBy(r => r.Name).ToList();
```

- [ ] **Step 5: Run the full backend suite**

Run: `cd backend && dotnet test`
Expected: PASS (new tests + all existing).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs \
        backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs
git commit -m "feat(discover): surface reviewLinks + note from master (fallback rep) on GET /api/places (#44)"
```

---

### Task 4: Frontend — Discover `PlaceSheet` shows รีวิว + โน้ต

Widen the SPA `DiscoverPlaceDto`, fix the one fixture, and render the two read-only sections.

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (`DiscoverPlaceDto` interface, ~:533)
- Modify: `frontend/src/pages/discover/lib/discoverFilter.test.ts` (fixture default)
- Modify: `frontend/src/pages/discover/components/PlaceSheet.tsx`
- Modify: `frontend/src/pages/discover/DiscoverPage.css`

**Interfaces:**
- Consumes: backend `DiscoverPlaceDto.reviewLinks`/`notes` (Task 3); existing `ReviewLink` interface, `ReviewIcon`, `reviewLabel`/`reviewHost`.

- [ ] **Step 1: Widen the SPA interface + fix the fixture (keeps `tsc` green)**

In `frontend/src/shared/api/api.ts`, in `interface DiscoverPlaceDto` add two fields after `trips: PlaceTripRefDto[]`:
```ts
    reviewLinks: ReviewLink[]
    notes: string | null
```

In `frontend/src/pages/discover/lib/discoverFilter.test.ts`, add the two fields to the `place()` fixture default (after `visited: false, trips: [],`):
```ts
  reviewLinks: [], notes: null,
```

- [ ] **Step 2: Run the discover slice/filter tests + typecheck**

Run: `cd frontend && npx tsc -b && npx vitest run src/pages/discover`
Expected: PASS (fixture now satisfies the widened interface; behaviour tests unaffected).

- [ ] **Step 3: Render the sections in `PlaceSheet.tsx`**

In `frontend/src/pages/discover/components/PlaceSheet.tsx`:

Add imports at the top:
```tsx
import {ReviewIcon} from '../../trips/components/ReviewIcon'
import {reviewLabel, reviewHost} from '../../trips/lib/reviewLinks'
```

Between the `<div className="disc-detail-badges">…</div>` block and `<div className="disc-actions">`, insert:
```tsx
      {place.reviewLinks.length > 0 && (
        <div className="disc-reviews">
          <div className="disc-sec-lab">รีวิว</div>
          {place.reviewLinks.map((l, i) => (
            <a key={l.url + i} className="disc-review" href={l.url} target="_blank" rel="noopener noreferrer">
              <ReviewIcon />
              <span className="disc-review-label">{reviewLabel(l, i)}</span>
              <span className="disc-review-host">{reviewHost(l.url)}</span>
            </a>
          ))}
        </div>
      )}
      {place.notes && (
        <div className="disc-note">
          <div className="disc-sec-lab">โน้ต</div>
          <p className="disc-note-body">{place.notes}</p>
        </div>
      )}
```

- [ ] **Step 4: Add the CSS (match the confirmed mock)**

In `frontend/src/pages/discover/DiscoverPage.css`, after the `.disc-detail-badges` rule, add:
```css
.disc-sec-lab { font-size: 12px; font-weight: 700; color: var(--muted); margin: 0 0 6px; }

.disc-reviews { margin: 2px 0 14px; }
.disc-review {
  display: flex; align-items: center; gap: 9px; text-decoration: none;
  border: 1px solid #f6cede; background: var(--review-bg); color: var(--review);
  border-radius: 10px; padding: 9px 11px; margin-bottom: 6px; font-weight: 700; font-size: 12.5px;
}
.disc-review:last-child { margin-bottom: 0; }
.disc-review svg { width: 16px; height: 16px; flex: none; }
.disc-review-label { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.disc-review-host { font-size: 11px; font-weight: 500; opacity: 0.72; }

.disc-note { margin: 2px 0 14px; }
.disc-note-body {
  margin: 0; background: var(--page); border: 1px solid var(--border); border-radius: 12px;
  padding: 11px 12px; font-size: 12.5px; color: #334155; line-height: 1.55; white-space: pre-wrap;
}
```

(`--review`/`--review-bg` resolve because `DiscoverPage.tsx` already imports `../trips/trips-tokens.css`.)

- [ ] **Step 5: Run the full frontend gate**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/discover/lib/discoverFilter.test.ts \
        frontend/src/pages/discover/components/PlaceSheet.tsx \
        frontend/src/pages/discover/DiscoverPage.css
git commit -m "feat(discover): show รีวิว + โน้ต sections on the place sheet (#44)"
```

---

### Task 5: Frontend — trip `StopDetailSheet` shows a โน้ต section

Add the note under the existing "รีวิว" section on the trip detail sheet (uses the existing `TripPlaceDto.notes`).

**Files:**
- Modify: `frontend/src/pages/trips/components/StopDetailSheet.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`

- [ ] **Step 1: Add the note block in `StopDetailSheet.tsx`**

In `frontend/src/pages/trips/components/StopDetailSheet.tsx`, immediately after the `{links.length > 0 && (<div className="sd-reviews">…</div>)}` block, add:
```tsx
        {place.notes && (
          <div className="sd-note">
            <div className="sd-sec-lab">โน้ต</div>
            <p className="sd-note-body">{place.notes}</p>
          </div>
        )}
```

- [ ] **Step 2: Add the CSS**

In `frontend/src/pages/trips/TripDetailPage.css`, near the `.stop-detail-sheet .sd-reviews`/`.sd-review` rules, add:
```css
.stop-detail-sheet .sd-note { margin-bottom: 4px; }
.stop-detail-sheet .sd-note-body {
  margin: 0; background: var(--page, #f8fafc); border: 1px solid var(--border);
  border-radius: 12px; padding: 10px 12px; font-size: 12.5px; line-height: 1.55; white-space: pre-wrap;
}
```

- [ ] **Step 3: Run the full frontend gate**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/trips/components/StopDetailSheet.tsx \
        frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): show note on the stop detail sheet (#44)"
```

---

### Task 6: Apply the migration to prod + interactive verification (rollout)

No code commit — this is the CLAUDE.md manual-migration step plus the required interactive smoke test. Do it **before/with** the deploy so the API never queries a missing column.

- [ ] **Step 1: Preview the migration SQL**

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: an idempotent script whose only effective change adds the `Notes` column to `PlaceProfiles`.

- [ ] **Step 2: Apply to prod (temporary firewall rule if needed)**

Confirm `az account show` is `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th`. If blocked by IP, add a temp firewall rule (CLAUDE.md), then:
```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```
Remove the temp firewall rule afterward.

- [ ] **Step 2b: Push (only if the user asks) so CD deploys**

```bash
git push main HEAD:main
```

- [ ] **Step 3: Interactive verification (REQUIRED — no component test harness)**

On the deployed app (seeded/authed), confirm:
1. A place with review links on Discover → the "รีวิว" section lists them; each opens in a **new browser tab**.
2. A place with a note → the "โน้ต" section shows it; a place with neither shows **neither section** and the map is **not** covered (no black-screen overlay regression, #36).
3. Set a note via MCP `update_trip_place` on a place → it appears on Discover **without** a `push_place_profile` call (write-through).
4. The trip `StopDetailSheet` shows the note under "รีวิว".

- [ ] **Step 4 (optional): Update `daily-state.md` / memory** with the push + prod-migration status. Do not sweep `daily-state.md` into any feature commit.

---

## Notes for the executor

- **Do not re-open design decisions** — the spec + ADRs 101–104 are approved. If a code reality contradicts the plan (e.g. a constructor signature differs), fix the plan's code to match reality; do not change the design.
- **Verify positional-record/constructor signatures against the real files** before running the first test of Tasks 2–3 (`AddTripPlaceCommand`, `UpdateTripPlaceCommand`, `DiscoverPlaceDto`) — they are the highest-risk compile points.
- **Pre-commit will run the whole suite** on every commit; budget ~40s each and never bypass it.
