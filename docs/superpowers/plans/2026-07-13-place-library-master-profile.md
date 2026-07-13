# Place Library — Cross-Trip Master Profile + Place Editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users edit a Place's own fields (best-time window, review links, checklist) directly from the คลังสถานที่ (Places) tab, and make that enrichment reusable across trips via a user-scoped master `PlaceProfile` that seeds every future capture.

**Architecture:** New user-scoped `PlaceProfile` (+ `PlaceProfileChecklistItem` junction) keyed by `(UserId, GooglePlaceId)`. Capture seeds a `TripPlace` from the profile; first enrichment auto-creates the profile; per-trip edits are overrides; an explicit push-to-master propagates a trip's values up. A new `PlaceEditorDialog` reuses shared section components with the existing `StopEditorDialog`.

**Tech Stack:** .NET (Clean Architecture, Mediator, FluentValidation, EF Core, xUnit + Moq + FluentAssertions, SQLite relational tests); React + Redux Toolkit + RTK Query + Syncfusion; EF migration applied to prod by hand.

**Spec:** `docs/superpowers/specs/2026-07-13-place-library-master-profile-design.md`
**ADRs:** 062-066. **Mock:** `docs/mocks/trip-place-library-editor-mock.html`. **Issue:** #37.

## Global Constraints

- **Pre-commit runs the FULL suite** (backend `dotnet build`+`dotnet test` Release, frontend `tsc --noEmit`+`npm run build`). Every commit must leave the whole suite green. Do NOT `--no-verify`.
- **New EF entity + all THREE `IApplicationDbContext` implementers (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`) + EF config + mapping must land in the SAME commit** — an unmapped entity fails EF model validation for every DbContext test (learned #33).
- **`TripPlaceDto` is a positional record**; adding `HasProfile` requires updating its single constructor site (`AddTripPlaceHandler.ToDto`) and every `ToDto` caller. Full construction-site set (verified): `AddTripPlaceHandler` (2 overloads + `Handle`), `UpdateTripPlaceHandler`, `ListTripPlacesHandler`, and `AddTripPlaceHandlerTests` (uses single-arg `ToDto(place)`). MCP (`TripTools`) and the controller only forward mediator results — no change.
- **Migrations are applied to prod BY HAND** (CLAUDE.md) — the app and CD do not run `db.Database.Migrate()`. Preview with `dotnet ef migrations script --idempotent`.
- **Backend mocks use Moq**, not NSubstitute. Relational handler tests use `SqliteAppDbContext`.
- **Frontend has NO component/visual test harness** (vitest runs in `node`). UI tasks are gated by `tsc`+`build`+**interactive** verification against the app or the mock; extract pure logic into `lib/` for vitest.
- **Icons are inline-SVG components, never emoji** (except the Budget user-data field).
- **Commits:** conventional-commit style, reference **#37** (`(#37)` for partial, `(closes #37)` on the last commit). `git add <explicit paths>` only — never `-A`/`.`. Remote is **`main`** (`git push main HEAD:main`), not `origin`.
- **Reuse existing value objects/patterns:** `ReviewLink.Create`, `ChecklistItem.NormalizeName`, the `TripPlace.ReviewLinks` JSON converter/comparer, the `PlaceChecklistEntry` cascade rules.

---

## File Structure

**Backend — create**
- `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs` — the master entity.
- `backend/src/MenuNest.Domain/Entities/PlaceProfileChecklistItem.cs` — profile↔ChecklistItem junction (no checked state).
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileChecklistItemConfiguration.cs`
- `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs` — shared seed / ensure-create / upsert logic.
- `backend/src/MenuNest.Application/UseCases/Trips/PushPlaceProfile/{PushPlaceProfileCommand,PushPlaceProfileHandler,PushPlaceProfileValidator}.cs`
- EF migration `AddPlaceProfiles` (generated).

**Backend — modify**
- `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` — +2 DbSets.
- `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` — +2 DbSets.
- `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs` — +2 DbSets.
- `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs` — +2 DbSets + `PlaceProfile.ReviewLinks` JSON conversion mirror.
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — `TripPlaceDto` +`HasProfile`.
- `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs` — `ToDto` +`hasProfile`; seed-on-capture.
- `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs` — auto-create + `HasProfile`.
- `backend/src/MenuNest.Application/UseCases/Trips/ListTripPlaces/ListTripPlacesHandler.cs` — `HasProfile` (batch).
- `backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/AttachChecklistItemHandler.cs` — auto-create.
- `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — push endpoint.

**Frontend — create**
- `frontend/src/pages/trips/components/ReviewLinksSection.tsx`
- `frontend/src/pages/trips/components/ChecklistSection.tsx`
- `frontend/src/pages/trips/components/PlaceEditorDialog.tsx`

**Frontend — modify**
- `frontend/src/shared/api/api.ts` — `TripPlaceDto` +`hasProfile`; `pushPlaceProfile` mutation + hook.
- `frontend/src/pages/trips/tripsSlice.ts` — `placeEditorPlaceId` + `setPlaceEditor`.
- `frontend/src/pages/trips/components/StopEditorDialog.tsx` — consume the extracted sections.
- `frontend/src/pages/trips/TripDetailPage.tsx` — wire `PlaceCard` onClick + render `PlaceEditorDialog`.

---

## Task 1: PlaceProfile domain + persistence (entities, configs, 3 DbContexts, migration)

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`, `.../PlaceProfileChecklistItem.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`, `.../PlaceProfileChecklistItemConfiguration.cs`
- Modify: `IApplicationDbContext.cs`, `AppDbContext.cs`, `SqliteAppDbContext.cs`, `InMemoryAppDbContext.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileRelationalTests.cs`

**Interfaces:**
- Produces: `PlaceProfile` (`Create(Guid userId, string googlePlaceId)`, `SetBestTime(TimeOnly?, TimeOnly?)`, `SetReviewLinks(IEnumerable<ReviewLink>)`, props `UserId`, `GooglePlaceId`, `BestTimeStart`, `BestTimeEnd`, `IReadOnlyList<ReviewLink> ReviewLinks`); `PlaceProfileChecklistItem` (`Create(Guid placeProfileId, Guid checklistItemId)`, props `PlaceProfileId`, `ChecklistItemId`); `IApplicationDbContext.PlaceProfiles`, `.PlaceProfileChecklistItems`.

- [ ] **Step 1: Write the failing relational test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public PlaceProfileRelationalTests()
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

    [Fact]
    public async Task Profile_round_trips_best_time_and_review_links()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/ChIJ1");
        profile.SetBestTime(new TimeOnly(16, 0), new TimeOnly(18, 30));
        profile.SetReviewLinks(new[] { ReviewLink.Create("https://youtu.be/abc", "clip") });
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var read = await _db.Set<PlaceProfile>().AsNoTracking().FirstAsync(p => p.Id == profile.Id);
        read.BestTimeStart.Should().Be(new TimeOnly(16, 0));
        read.ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://youtu.be/abc");
    }

    [Fact]
    public async Task Unique_index_blocks_a_second_profile_for_the_same_user_and_place()
    {
        _db.Set<PlaceProfile>().Add(PlaceProfile.Create(_user.Id, "places/DUP"));
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfile>().Add(PlaceProfile.Create(_user.Id, "places/DUP"));
        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Deleting_a_profile_cascades_its_checklist_junction_but_not_the_library_item()
    {
        var item = ChecklistItem.Create(_user.Id, "umbrella");
        _db.ChecklistItems.Add(item);
        var profile = PlaceProfile.Create(_user.Id, "places/CJ");
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfileChecklistItem>().Add(PlaceProfileChecklistItem.Create(profile.Id, item.Id));
        await _db.SaveChangesAsync();

        _db.Set<PlaceProfile>().Remove(profile);
        await _db.SaveChangesAsync();

        (await _db.Set<PlaceProfileChecklistItem>().CountAsync()).Should().Be(0);
        (await _db.ChecklistItems.CountAsync(i => i.Id == item.Id)).Should().Be(1);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run to verify it fails (does not compile — types missing)**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileRelationalTests`
Expected: FAIL — `PlaceProfile` / `PlaceProfileChecklistItem` / `Set<PlaceProfile>` do not exist.

- [ ] **Step 3: Create the entities**

`backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`:

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A User-scoped, reusable MASTER record of the user's own enrichment for one Google place —
/// best-time window, review links, and (via PlaceProfileChecklistItem) a checklist item-set.
/// Keyed by (UserId, GooglePlaceId). Seeds a TripPlace on capture; per-trip edits do not change
/// it unless explicitly pushed (ADR-063/064). Holds no per-trip state (no checked flag).
/// </summary>
public sealed class PlaceProfile : Entity
{
    public Guid UserId { get; private set; }
    public string GooglePlaceId { get; private set; } = null!;
    public TimeOnly? BestTimeStart { get; private set; }
    public TimeOnly? BestTimeEnd { get; private set; }

    private readonly List<ReviewLink> _reviewLinks = new();
    public IReadOnlyList<ReviewLink> ReviewLinks => _reviewLinks;

    private PlaceProfile() { } // EF

    public static PlaceProfile Create(Guid userId, string googlePlaceId)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required for a place profile.");
        if (string.IsNullOrWhiteSpace(googlePlaceId)) throw new DomainException("GooglePlaceId is required for a place profile.");
        return new PlaceProfile { UserId = userId, GooglePlaceId = googlePlaceId.Trim() };
    }

    public void SetBestTime(TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null) { start = null; end = null; }
        else if (end <= start) throw new DomainException("Best-time end must be after start.");
        BestTimeStart = start;
        BestTimeEnd = end;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetReviewLinks(IEnumerable<ReviewLink> links)
    {
        var list = (links ?? Enumerable.Empty<ReviewLink>()).ToList();
        if (list.Count > 10) throw new DomainException("A place profile can have at most 10 review links.");
        _reviewLinks.Clear();
        _reviewLinks.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
}
```

`backend/src/MenuNest.Domain/Entities/PlaceProfileChecklistItem.cs`:

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The attachment of one ChecklistItem to a PlaceProfile master (the remembered "things to bring"
/// item-SET). No checked state — that is per-trip on PlaceChecklistEntry (ADR-059/064).
/// </summary>
public sealed class PlaceProfileChecklistItem : Entity
{
    public Guid PlaceProfileId { get; private set; }
    public Guid ChecklistItemId { get; private set; }

    private PlaceProfileChecklistItem() { } // EF

    public static PlaceProfileChecklistItem Create(Guid placeProfileId, Guid checklistItemId)
    {
        if (placeProfileId == Guid.Empty) throw new DomainException("PlaceProfileId is required.");
        if (checklistItemId == Guid.Empty) throw new DomainException("ChecklistItemId is required.");
        return new PlaceProfileChecklistItem { PlaceProfileId = placeProfileId, ChecklistItemId = checklistItemId };
    }
}
```

- [ ] **Step 4: Create the EF configurations**

`backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`:

```csharp
using System.Text.Json;
using MenuNest.Domain.Entities;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceProfileConfiguration : IEntityTypeConfiguration<PlaceProfile>
{
    public void Configure(EntityTypeBuilder<PlaceProfile> b)
    {
        b.ToTable("PlaceProfiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.UserId).IsRequired();
        b.Property(p => p.GooglePlaceId).IsRequired().HasMaxLength(400);

        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reviewConverter = new ValueConverter<IReadOnlyList<ReviewLink>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => string.IsNullOrEmpty(v)
                ? new List<ReviewLink>()
                : JsonSerializer.Deserialize<List<ReviewLink>>(v, jsonOpts) ?? new List<ReviewLink>());
        var reviewComparer = new ValueComparer<IReadOnlyList<ReviewLink>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(b, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ReviewLink>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        b.Property(p => p.ReviewLinks)
            .HasConversion(reviewConverter, reviewComparer)
            .HasColumnName("ReviewLinksJson")
            .HasColumnType("nvarchar(max)")
            .HasField("_reviewLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValueSql("'[]'");

        // One profile per Google place per user (mirrors TripPlace's (TripId, GooglePlaceId)).
        b.HasIndex(p => new { p.UserId, p.GooglePlaceId }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileChecklistItemConfiguration.cs`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceProfileChecklistItemConfiguration : IEntityTypeConfiguration<PlaceProfileChecklistItem>
{
    public void Configure(EntityTypeBuilder<PlaceProfileChecklistItem> b)
    {
        b.ToTable("PlaceProfileChecklistItems");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.PlaceProfileId).IsRequired();
        b.Property(e => e.ChecklistItemId).IsRequired();
        b.HasIndex(e => new { e.PlaceProfileId, e.ChecklistItemId }).IsUnique();
        // Deleting a profile removes its junction rows…
        b.HasOne<PlaceProfile>().WithMany().HasForeignKey(e => e.PlaceProfileId).OnDelete(DeleteBehavior.Cascade);
        // …but NEVER the library item (ADR-059 rule reused).
        b.HasOne<ChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5: Add DbSets to all three contexts + InMemory JSON mirror**

In `IApplicationDbContext.cs`, under the Trip Planner block, add:

```csharp
    DbSet<PlaceProfile> PlaceProfiles { get; }
    DbSet<PlaceProfileChecklistItem> PlaceProfileChecklistItems { get; }
```

In `AppDbContext.cs` and `SqliteAppDbContext.cs`, under the Trip Planner block, add:

```csharp
    public DbSet<PlaceProfile> PlaceProfiles => Set<PlaceProfile>();
    public DbSet<PlaceProfileChecklistItem> PlaceProfileChecklistItems => Set<PlaceProfileChecklistItem>();
```

In `InMemoryAppDbContext.cs` add the same two DbSet properties, AND (because InMemory does not `ApplyConfigurationsFromAssembly`) mirror the review-links JSON conversion inside `OnModelCreating`, right after the existing `TripPlace` block:

```csharp
        // Mirror the JSON-list conversion for PlaceProfile.ReviewLinks (same as TripPlace).
        modelBuilder.Entity<PlaceProfile>()
            .Property(p => p.ReviewLinks)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<ReviewLink>)(JsonSerializer.Deserialize<List<ReviewLink>>(v, jsonOptions) ?? new List<ReviewLink>()),
                reviewLinksComparer)
            .HasField("_reviewLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);
```

(`reviewLinksComparer` and `jsonOptions` already exist in that method.)

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileRelationalTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Generate the EF migration**

Run:
```bash
cd backend
dotnet ef migrations add AddPlaceProfiles --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: new migration files under `src/MenuNest.Infrastructure/Persistence/Migrations/*_AddPlaceProfiles.*` creating `PlaceProfiles` (with `ReviewLinksJson NOT NULL DEFAULT '[]'`, unique `(UserId, GooglePlaceId)`) and `PlaceProfileChecklistItems` (unique `(PlaceProfileId, ChecklistItemId)`, cascade from PlaceProfile, restrict to ChecklistItem). Do NOT apply to prod yet (Task 8).

- [ ] **Step 8: Run the full backend suite (pre-commit parity)**

Run: `cd backend; dotnet build -c Release; dotnet test -c Release`
Expected: PASS (model validation green across all three DbContexts).

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/PlaceProfile.cs backend/src/MenuNest.Domain/Entities/PlaceProfileChecklistItem.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileChecklistItemConfiguration.cs backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileRelationalTests.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(trips): PlaceProfile master entity + junction + migration (#37)"
```

---
## Task 2: Seed-on-capture + `HasProfile` plumbing

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`
- Modify: `TripDtos.cs`, `AddTripPlace/AddTripPlaceHandler.cs`, `UpdateTripPlace/UpdateTripPlaceHandler.cs`, `ListTripPlaces/ListTripPlacesHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeedRelationalTests.cs`

**Interfaces:**
- Consumes: `PlaceProfile`, `PlaceProfileChecklistItem`, `IApplicationDbContext.PlaceProfiles/PlaceProfileChecklistItems` (Task 1).
- Produces: `PlaceProfileSync.SeedIntoAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct) -> Task<bool>`; `PlaceProfileSync.ExistsAsync(IApplicationDbContext db, Guid userId, string? googlePlaceId, CancellationToken ct) -> Task<bool>`; `TripPlaceDto.HasProfile`; `AddTripPlaceHandler.ToDto(p, checklist, hasProfile)`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeedRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileSeedRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileSeedRelationalTests()
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

    private AddTripPlaceHandler NewAdd()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new AddTripPlaceHandler(_db, users.Object, new AddTripPlaceValidator());
    }

    private async Task<PlaceProfile> SeedProfile(string placeId)
    {
        var item = ChecklistItem.Create(_user.Id, "sunscreen");
        _db.ChecklistItems.Add(item);
        var profile = PlaceProfile.Create(_user.Id, placeId);
        profile.SetBestTime(new TimeOnly(16, 0), new TimeOnly(18, 0));
        profile.SetReviewLinks(new[] { ReviewLink.Create("https://youtu.be/x", "clip") });
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfileChecklistItem>().Add(PlaceProfileChecklistItem.Create(profile.Id, item.Id));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return profile;
    }

    [Fact]
    public async Task Capture_seeds_best_time_reviews_and_checklist_from_an_existing_profile()
    {
        await SeedProfile("places/SEED");
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Beach", 1, 2, PlaceCategory.See, "places/SEED", null, null, null, null),
            default);

        dto.HasProfile.Should().BeTrue();
        dto.BestTimeStart.Should().Be(new TimeOnly(16, 0));
        dto.ReviewLinks.Should().ContainSingle();
        dto.Checklist.Should().ContainSingle().Which.Name.Should().Be("sunscreen");
    }

    [Fact]
    public async Task Capture_without_a_profile_seeds_nothing_and_HasProfile_is_false()
    {
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "New", 1, 2, PlaceCategory.See, "places/NONE", null, null, null, null),
            default);
        dto.HasProfile.Should().BeFalse();
        dto.BestTimeStart.Should().BeNull();
        dto.Checklist.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_without_a_google_place_id_never_seeds()
    {
        await SeedProfile("places/SEED2"); // exists but the new place has no place_id
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Manual", 1, 2, PlaceCategory.See, null, null, null, null, null),
            default);
        dto.HasProfile.Should().BeFalse();
        dto.Checklist.Should().BeEmpty();
    }

    [Fact]
    public async Task List_reports_HasProfile_true_only_for_places_with_a_profile()
    {
        await SeedProfile("places/HASP");
        await NewAdd().Handle(new AddTripPlaceCommand(_trip.Id, "WithP", 1, 2, PlaceCategory.See, "places/HASP", null, null, null, null), default);
        await NewAdd().Handle(new AddTripPlaceCommand(_trip.Id, "NoP", 1, 2, PlaceCategory.See, "places/OTHER", null, null, null, null), default);

        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var list = await new ListTripPlacesHandler(_db, users.Object).Handle(new ListTripPlacesQuery(_trip.Id), default);

        list.Single(p => p.Name == "WithP").HasProfile.Should().BeTrue();
        list.Single(p => p.Name == "NoP").HasProfile.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileSeedRelationalTests`
Expected: FAIL — `TripPlaceDto.HasProfile` and `PlaceProfileSync` do not exist.

- [ ] **Step 3: Add `HasProfile` to `TripPlaceDto`**

In `TripDtos.cs`, change the `TripPlaceDto` record's parameter list to append `HasProfile`:

```csharp
public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<PlaceChecklistEntryDto> Checklist,
    bool HasProfile);
```

- [ ] **Step 4: Create `PlaceProfileSync` (Seed + Exists)**

`backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips;

/// <summary>
/// Shared master-profile plumbing used by capture (seed), the editor Save / checklist attach
/// (auto-create), and push-to-master (upsert). None of these methods call SaveChanges — the
/// caller owns the unit of work. All are no-ops for a place with no GooglePlaceId (ADR-066).
/// </summary>
public static class PlaceProfileSync
{
    /// <summary>Copy an existing profile's enrichment into a freshly-created (unsaved) TripPlace.
    /// Returns true iff a profile existed and was applied.</summary>
    public static async Task<bool> SeedIntoAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId)) return false;
        var profile = await db.PlaceProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (profile is null) return false;

        place.SetBestTime(profile.BestTimeStart, profile.BestTimeEnd);
        place.SetReviewLinks(profile.ReviewLinks);
        var itemIds = await db.PlaceProfileChecklistItems
            .Where(x => x.PlaceProfileId == profile.Id)
            .Select(x => x.ChecklistItemId)
            .ToListAsync(ct);
        foreach (var itemId in itemIds)
            db.PlaceChecklistEntries.Add(PlaceChecklistEntry.Create(place.Id, itemId));
        return true;
    }

    /// <summary>Whether a master profile exists for this user + place_id.</summary>
    public static async Task<bool> ExistsAsync(IApplicationDbContext db, Guid userId, string? googlePlaceId, CancellationToken ct)
        => !string.IsNullOrEmpty(googlePlaceId)
           && await db.PlaceProfiles.AnyAsync(p => p.UserId == userId && p.GooglePlaceId == googlePlaceId, ct);
}
```

- [ ] **Step 5: Seed on capture in `AddTripPlaceHandler`**

Update the `ToDto` overloads and `Handle` (add `using Microsoft.EntityFrameworkCore;` — already present):

```csharp
    public async ValueTask<TripPlaceDto> Handle(AddTripPlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var place = TripPlace.Create(c.TripId, c.Name, c.Lat, c.Lng, c.Category,
            c.GooglePlaceId, c.Address, c.PriceLevel, c.PhotoUrl, c.OpeningHoursJson);
        _db.TripPlaces.Add(place);
        var seeded = await PlaceProfileSync.SeedIntoAsync(_db, user.Id, place, ct);
        await _db.SaveChangesAsync(ct);

        var checklist = seeded
            ? await (from e in _db.PlaceChecklistEntries
                     join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                     where e.TripPlaceId == place.Id
                     orderby e.CreatedAt, e.Id
                     select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked)).ToListAsync(ct)
            : (IReadOnlyList<PlaceChecklistEntryDto>)Array.Empty<PlaceChecklistEntryDto>();
        return ToDto(place, checklist, seeded);
    }

    internal static TripPlaceDto ToDto(TripPlace p) => ToDto(p, Array.Empty<PlaceChecklistEntryDto>(), false);

    internal static TripPlaceDto ToDto(TripPlace p, IReadOnlyList<PlaceChecklistEntryDto> checklist, bool hasProfile = false) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.BestTimeStart, p.BestTimeEnd, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        checklist, hasProfile);
```

- [ ] **Step 6: Report `HasProfile` from `UpdateTripPlaceHandler` and `ListTripPlacesHandler`**

In `UpdateTripPlaceHandler.Handle`, after `await _db.SaveChangesAsync(ct);` compute and pass `hasProfile`:

```csharp
        await _db.SaveChangesAsync(ct);
        var hasProfile = await PlaceProfileSync.ExistsAsync(_db, user.Id, place.GooglePlaceId, ct);
        var checklist = await (from e in _db.PlaceChecklistEntries
                               join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                               where e.TripPlaceId == place.Id
                               orderby e.CreatedAt, e.Id
                               select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked))
                              .ToListAsync(ct);
        return AddTripPlaceHandler.ToDto(place, checklist, hasProfile);
```

In `ListTripPlacesHandler.Handle`, after loading `places` add a batch profile-existence lookup and pass it to `ToDto`:

```csharp
        var profiledIds = (await _db.PlaceProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => p.GooglePlaceId)
            .ToListAsync(ct)).ToHashSet();

        return places
            .Select(p => AddTripPlaceHandler.ToDto(
                p,
                byPlace.TryGetValue(p.Id, out var l) ? l : Array.Empty<PlaceChecklistEntryDto>(),
                p.GooglePlaceId != null && profiledIds.Contains(p.GooglePlaceId)))
            .ToList();
```

- [ ] **Step 7: Run the new test + the full backend suite**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileSeedRelationalTests`
Expected: PASS (4 tests). Then `dotnet build -c Release; dotnet test -c Release` — Expected: PASS (all handler/DTO tests compile with the new positional field).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs backend/src/MenuNest.Application/UseCases/Trips/ListTripPlaces/ListTripPlacesHandler.cs backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeedRelationalTests.cs
git commit -m "feat(trips): seed captured places from the master profile + expose HasProfile (#37)"
```

---

## Task 3: Auto-create the master on first enrichment

**Files:**
- Modify: `PlaceProfileSync.cs` (add `EnsureCreatedAsync` + `UpsertFromAsync`), `UpdateTripPlace/UpdateTripPlaceHandler.cs`, `AttachChecklistItem/AttachChecklistItemHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileAutoCreateRelationalTests.cs`

**Interfaces:**
- Produces: `PlaceProfileSync.EnsureCreatedAsync(db, userId, TripPlace place, ct) -> Task<bool>` (true iff a profile was created); `PlaceProfileSync.UpsertFromAsync(db, userId, TripPlace place, ct) -> Task` (create-or-overwrite the profile from the place's current enrichment).

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileAutoCreateRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.TripDtos;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileAutoCreateRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileAutoCreateRelationalTests()
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

    [Fact]
    public async Task First_save_with_enrichment_auto_creates_the_master()
    {
        var placeId = AddPlace("places/AC1");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new TimeOnly(9, 0), new TimeOnly(11, 0), Array.Empty<ReviewLinkDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstOrDefaultAsync(p => p.GooglePlaceId == "places/AC1");
        profile.Should().NotBeNull();
        profile!.BestTimeStart.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Second_save_does_not_overwrite_an_existing_master()
    {
        var placeId = AddPlace("places/AC2");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new TimeOnly(9, 0), new TimeOnly(11, 0), Array.Empty<ReviewLinkDto>()), default);
        // second, different edit — master must stay at 09:00 (per-trip override, ADR-064)
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new TimeOnly(14, 0), new TimeOnly(15, 0), Array.Empty<ReviewLinkDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/AC2");
        profile.BestTimeStart.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task First_checklist_attach_auto_creates_the_master_with_that_item()
    {
        var placeId = AddPlace("places/AC3");
        var handler = new AttachChecklistItemHandler(_db, Users().Object, new AttachChecklistItemValidator());
        await handler.Handle(new AttachChecklistItemCommand(_trip.Id, placeId, "passport"), default);

        var profile = await _db.Set<PlaceProfile>().FirstOrDefaultAsync(p => p.GooglePlaceId == "places/AC3");
        profile.Should().NotBeNull();
        var itemNames = await (from x in _db.Set<PlaceProfileChecklistItem>()
                               join i in _db.ChecklistItems on x.ChecklistItemId equals i.Id
                               where x.PlaceProfileId == profile!.Id select i.Name).ToListAsync();
        itemNames.Should().ContainSingle().Which.Should().Be("passport");
    }

    [Fact]
    public async Task Save_with_no_place_id_creates_no_master()
    {
        var placeId = AddPlace(null);
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new TimeOnly(9, 0), new TimeOnly(11, 0), Array.Empty<ReviewLinkDto>()), default);
        (await _db.Set<PlaceProfile>().CountAsync()).Should().Be(0);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

(Remove the erroneous `using MenuNest.Application.UseCases.Trips.TripDtos;` line — `ReviewLinkDto` lives in `MenuNest.Application.UseCases.Trips`; keep only that namespace.)

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileAutoCreateRelationalTests`
Expected: FAIL — profiles are not created (no auto-create wired yet).

- [ ] **Step 3: Add `UpsertFromAsync` + `EnsureCreatedAsync` to `PlaceProfileSync`**

Append to `PlaceProfileSync`:

```csharp
    /// <summary>Create the profile from the place's CURRENT enrichment iff none exists yet
    /// (first-enrichment auto-create). Returns true iff a profile was created.</summary>
    public static async Task<bool> EnsureCreatedAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId)) return false;
        var exists = await db.PlaceProfiles.AnyAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (exists) return false;
        await UpsertFromAsync(db, userId, place, ct);
        return true;
    }

    /// <summary>Create-or-overwrite the profile from the place's current best-time, review links,
    /// and checklist item-SET (push-to-master).</summary>
    public static async Task UpsertFromAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId))
            throw new Domain.Exceptions.DomainException("This place has no Google place to save to your library.");

        var profile = await db.PlaceProfiles.FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (profile is null)
        {
            profile = PlaceProfile.Create(userId, place.GooglePlaceId);
            db.PlaceProfiles.Add(profile);
        }
        profile.SetBestTime(place.BestTimeStart, place.BestTimeEnd);
        profile.SetReviewLinks(place.ReviewLinks);

        var currentItemIds = await db.PlaceChecklistEntries
            .Where(e => e.TripPlaceId == place.Id).Select(e => e.ChecklistItemId).ToListAsync(ct);
        var links = await db.PlaceProfileChecklistItems.Where(x => x.PlaceProfileId == profile.Id).ToListAsync(ct);
        db.PlaceProfileChecklistItems.RemoveRange(links.Where(x => !currentItemIds.Contains(x.ChecklistItemId)));
        var have = links.Select(x => x.ChecklistItemId).ToHashSet();
        foreach (var id in currentItemIds.Where(id => !have.Contains(id)))
            db.PlaceProfileChecklistItems.Add(PlaceProfileChecklistItem.Create(profile.Id, id));
    }
```

- [ ] **Step 4: Wire auto-create into `UpdateTripPlaceHandler`**

Right before the final `await _db.SaveChangesAsync(ct);` (the one added in Task 2), insert auto-create so the snapshot includes the just-applied best-time/reviews and the current checklist:

```csharp
        place.UpdateDetails(c.Name, c.Category, c.Address, c.FeeNote, c.Notes);
        place.SetBestTime(c.BestTimeStart, c.BestTimeEnd);
        place.SetReviewLinks((c.ReviewLinks ?? Enumerable.Empty<ReviewLinkDto>()).Select(r => ReviewLink.Create(r.Url, r.Label)));

        await PlaceProfileSync.EnsureCreatedAsync(_db, user.Id, place, ct);
        await _db.SaveChangesAsync(ct);
        var hasProfile = await PlaceProfileSync.ExistsAsync(_db, user.Id, place.GooglePlaceId, ct);
        // …checklist join + return ToDto(place, checklist, hasProfile) as in Task 2 Step 6
```

- [ ] **Step 5: Wire auto-create into `AttachChecklistItemHandler`**

Change the handler to LOAD the place (instead of `AnyAsync`), and after persisting the attach, ensure-create the master (which now sees the persisted entry):

```csharp
        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");
        // …existing item create-or-reuse + entry create-if-absent…
        await _db.SaveChangesAsync(ct);                                  // persist the entry first
        if (await PlaceProfileSync.EnsureCreatedAsync(_db, user.Id, place, ct))
            await _db.SaveChangesAsync(ct);                              // persist the new master iff created
        return new PlaceChecklistEntryDto(entry.Id, item.Id, item.Name, entry.IsChecked);
```

(Add `using MenuNest.Application.UseCases.Trips;` for `PlaceProfileSync`. Replace the existing `placeExists` `AnyAsync` guard with the load above.)

- [ ] **Step 6: Run the new test + full suite**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceProfileAutoCreateRelationalTests`
Expected: PASS (4 tests). Then `dotnet test -c Release` — Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/AttachChecklistItemHandler.cs backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileAutoCreateRelationalTests.cs
git commit -m "feat(trips): auto-create the master profile on first enrichment (#37)"
```

---

## Task 4: Push-to-master endpoint

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/PushPlaceProfile/PushPlaceProfileCommand.cs`, `PushPlaceProfileHandler.cs`, `PushPlaceProfileValidator.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PushPlaceProfileHandlerTests.cs`

**Interfaces:**
- Consumes: `PlaceProfileSync.UpsertFromAsync` (Task 3), `AddTripPlaceHandler.ToDto` (Task 2).
- Produces: `PushPlaceProfileCommand(Guid TripId, Guid PlaceId) : ICommand<TripPlaceDto>`; `POST /api/trips/{id}/places/{placeId}/push-to-profile`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PushPlaceProfileHandlerTests.cs` (SQLite fixture as in Task 3; only the test bodies differ):

```csharp
    [Fact]
    public async Task Push_overwrites_the_master_with_the_current_trip_values()
    {
        var placeId = AddPlace("places/PUSH");   // helper from the fixture
        // seed a stale master via a first edit
        await new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator())
            .Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
                new TimeOnly(9, 0), new TimeOnly(10, 0), Array.Empty<ReviewLinkDto>()), default);
        // change this trip only (override) — master still 09:00
        await new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator())
            .Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
                new TimeOnly(20, 0), new TimeOnly(21, 0), Array.Empty<ReviewLinkDto>()), default);

        var dto = await new PushPlaceProfileHandler(_db, Users().Object)
            .Handle(new PushPlaceProfileCommand(_trip.Id, placeId), default);

        dto.HasProfile.Should().BeTrue();
        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/PUSH");
        profile.BestTimeStart.Should().Be(new TimeOnly(20, 0));   // now the pushed value
    }

    [Fact]
    public async Task Push_on_a_place_without_a_google_place_id_throws()
    {
        var placeId = AddPlace(null);
        var act = () => new PushPlaceProfileHandler(_db, Users().Object)
            .Handle(new PushPlaceProfileCommand(_trip.Id, placeId), default).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PushPlaceProfileHandlerTests`
Expected: FAIL — `PushPlaceProfileCommand`/`PushPlaceProfileHandler` do not exist.

- [ ] **Step 3: Create the command, validator, handler**

`PushPlaceProfileCommand.cs`:

```csharp
using Mediator;
namespace MenuNest.Application.UseCases.Trips.PushPlaceProfile;
public sealed record PushPlaceProfileCommand(Guid TripId, Guid PlaceId) : ICommand<TripPlaceDto>;
```

`PushPlaceProfileValidator.cs`:

```csharp
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.PushPlaceProfile;
public sealed class PushPlaceProfileValidator : AbstractValidator<PushPlaceProfileCommand>
{
    public PushPlaceProfileValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
    }
}
```

`PushPlaceProfileHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.PushPlaceProfile;

public sealed class PushPlaceProfileHandler : ICommandHandler<PushPlaceProfileCommand, TripPlaceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public PushPlaceProfileHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<TripPlaceDto> Handle(PushPlaceProfileCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        await PlaceProfileSync.UpsertFromAsync(_db, user.Id, place, ct);
        await _db.SaveChangesAsync(ct);

        var checklist = await (from e in _db.PlaceChecklistEntries
                               join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                               where e.TripPlaceId == place.Id
                               orderby e.CreatedAt, e.Id
                               select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked)).ToListAsync(ct);
        return AddTripPlaceHandler.ToDto(place, checklist, hasProfile: true);
    }
}
```

- [ ] **Step 4: Add the controller endpoint**

In `TripsController.cs` add the `using` and the action after `DeletePlace`:

```csharp
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
// …
    [HttpPost("api/trips/{id:guid}/places/{placeId:guid}/push-to-profile")]
    public async Task<ActionResult<TripPlaceDto>> PushPlaceProfile(Guid id, Guid placeId, CancellationToken ct)
        => Ok(await _mediator.Send(new PushPlaceProfileCommand(id, placeId), ct));
```

- [ ] **Step 5: Run the new test + full suite**

Run: `cd backend; dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PushPlaceProfileHandlerTests`
Expected: PASS (2 tests). Then `dotnet build -c Release; dotnet test -c Release` — Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/PushPlaceProfile/ backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/tests/MenuNest.Application.UnitTests/Trips/PushPlaceProfileHandlerTests.cs
git commit -m "feat(trips): push-to-master endpoint for the place library (#37)"
```

---
## Task 5: Frontend API + slice plumbing

**Files:**
- Modify: `frontend/src/shared/api/api.ts`, `frontend/src/pages/trips/tripsSlice.ts`

**Interfaces:**
- Produces: `TripPlaceDto.hasProfile: boolean`; `usePushPlaceProfileMutation()` (→ `TripPlaceDto`); slice `placeEditorPlaceId: string | null` + `setPlaceEditor(id | null)`.

- [ ] **Step 1: Add `hasProfile` to the `TripPlaceDto` type**

In `api.ts`, extend the interface (line ~510):

```ts
export interface TripPlaceDto {
    id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number
    address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null
    bestTimeStart: string | null; bestTimeEnd: string | null; openingHoursJson: string | null
    feeNote: string | null; notes: string | null
    reviewLinks: ReviewLink[]
    checklist: PlaceChecklistEntry[]
    hasProfile: boolean
}
```

- [ ] **Step 2: Add the `pushPlaceProfile` mutation**

In `api.ts`, right after the `deleteTripPlace` mutation (line ~1310), add:

```ts
        pushPlaceProfile: build.mutation<TripPlaceDto, {tripId: string; placeId: string}>({
            query: ({tripId, placeId}) => ({url: `/api/trips/${tripId}/places/${placeId}/push-to-profile`, method: 'POST'}),
            // Refresh HasProfile flags for the trip's places.
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}],
        }),
```

Then add `usePushPlaceProfileMutation` to the hook export block (the `export const { … } = api` list that includes `useUpdateTripPlaceMutation`).

- [ ] **Step 3: Add editor state to the slice**

In `tripsSlice.ts`: add `placeEditorPlaceId: string | null` to `TripsState`, `placeEditorPlaceId: null` to `initialState`, a reducer `setPlaceEditor(s, a: PayloadAction<string | null>) { s.placeEditorPlaceId = a.payload }`, and add `setPlaceEditor` to the exported actions.

- [ ] **Step 4: Typecheck**

Run: `cd frontend; npx tsc -b`
Expected: PASS (no type errors).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/tripsSlice.ts
git commit -m "feat(trips): add hasProfile + pushPlaceProfile + place-editor slice state (#37)"
```

---

## Task 6: Extract shared editor sections (`ReviewLinksSection`, `ChecklistSection`)

**Files:**
- Create: `frontend/src/pages/trips/components/ReviewLinksSection.tsx`, `frontend/src/pages/trips/components/ChecklistSection.tsx`
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx`

**Interfaces:**
- Produces: `<ReviewLinksSection drafts={ReviewDraft[]} onChange={(d: ReviewDraft[]) => void} />`; `<ChecklistSection tripId={string} placeId={string} checklist={PlaceChecklistEntry[]} />`.
- Consumes: `lib/reviewLinks` (`ReviewDraft`, `MAX_REVIEW_LINKS`), `lib/checklist`, the checklist mutations + `useListChecklistItemsQuery`.

**No unit test** — presentational components; the SPA has no jsdom harness (CLAUDE.md). Pure logic is already covered in `lib/reviewLinks.test.ts` / `lib/checklist` tests. Gate = `tsc` + `build` + **interactive** parity check of the Stop editor.

- [ ] **Step 1: Create `ReviewLinksSection.tsx`** (moved verbatim from `StopEditorDialog` lines ~249-291)

```tsx
import {ReviewIcon} from './ReviewIcon'
import {MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'

export function ReviewLinksSection({
  drafts,
  onChange,
}: {
  drafts: ReviewDraft[]
  onChange: (drafts: ReviewDraft[]) => void
}) {
  return (
    <section className="se-sec">
      <div className="se-sec-head">
        <ReviewIcon />ลิงก์รีวิว (TikTok ฯลฯ)
      </div>
      {drafts.map((d, i) => (
        <div className="rv-row" key={i}>
          <input
            className="rv-url"
            type="url"
            placeholder="https://www.tiktok.com/@..."
            value={d.url}
            onChange={(e) => onChange(drafts.map((r, j) => (j === i ? {...r, url: e.target.value} : r)))}
          />
          <input
            className="rv-lab"
            placeholder="ป้ายกำกับ (ไม่บังคับ)"
            value={d.label}
            onChange={(e) => onChange(drafts.map((r, j) => (j === i ? {...r, label: e.target.value} : r)))}
          />
          <button type="button" className="rv-del" aria-label="ลบลิงก์" onClick={() => onChange(drafts.filter((_, j) => j !== i))}>
            ✕
          </button>
        </div>
      ))}
      {drafts.length < MAX_REVIEW_LINKS && (
        <button type="button" className="rv-add" onClick={() => onChange([...drafts, {url: '', label: ''}])}>
          + เพิ่มลิงก์รีวิว
        </button>
      )}
    </section>
  )
}
```

- [ ] **Step 2: Create `ChecklistSection.tsx`** (moved from `StopEditorDialog` — the checklist state, handlers lines ~73-123, and JSX lines ~293-372; now self-contained by `tripId`+`placeId`)

```tsx
import {useState} from 'react'
import {
  useListChecklistItemsQuery,
  useAttachChecklistItemMutation,
  useDetachChecklistItemMutation,
  useSetChecklistEntryCheckedMutation,
  type PlaceChecklistEntry,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {ChecklistIcon} from './ChecklistIcon'
import {
  MAX_CHECKLIST_ITEMS_PER_PLACE,
  isValidChecklistName,
  normalizeChecklistName,
  matchLibrary,
  exactMatch,
  checklistProgress,
} from '../lib/checklist'

export function ChecklistSection({
  tripId,
  placeId,
  checklist,
}: {
  tripId: string
  placeId: string
  checklist: PlaceChecklistEntry[]
}) {
  const {data: library} = useListChecklistItemsQuery()
  const [attachChecklist] = useAttachChecklistItemMutation()
  const [detachChecklist] = useDetachChecklistItemMutation()
  const [setChecklistChecked] = useSetChecklistEntryCheckedMutation()
  const [ckDraft, setCkDraft] = useState('')
  const [ckError, setCkError] = useState<string | null>(null)

  const progress = checklistProgress(checklist)
  const attachedItemIds = new Set(checklist.map((e) => e.checklistItemId))
  const suggestions = matchLibrary(ckDraft, library ?? []).filter((i) => !attachedItemIds.has(i.id))
  const showCreate = isValidChecklistName(ckDraft) && !exactMatch(ckDraft, library ?? [])

  const addChecklist = async (name: string) => {
    setCkError(null)
    if (!isValidChecklistName(name)) { setCkError('ชื่อไม่ถูกต้อง หรือยาวเกิน 100 ตัวอักษร'); return }
    if (checklist.length >= MAX_CHECKLIST_ITEMS_PER_PLACE) { setCkError(`เพิ่มได้สูงสุด ${MAX_CHECKLIST_ITEMS_PER_PLACE} รายการ`); return }
    try { await attachChecklist({tripId, placeId, name: normalizeChecklistName(name)}).unwrap(); setCkDraft('') }
    catch (err) { setCkError(getErrorMessage(err)) }
  }
  const toggleChecklist = async (entryId: string, next: boolean) => {
    setCkError(null)
    try { await setChecklistChecked({tripId, placeId, entryId, isChecked: next}).unwrap() }
    catch (err) { setCkError(getErrorMessage(err)) }
  }
  const removeChecklist = async (entryId: string) => {
    setCkError(null)
    try { await detachChecklist({tripId, placeId, entryId}).unwrap() }
    catch (err) { setCkError(getErrorMessage(err)) }
  }

  return (
    <section className="se-sec">
      <div className="se-sec-head">
        <ChecklistIcon />สิ่งที่ต้องเตรียม
        {checklist.length > 0 && (<span className="se-ck-pill">เตรียมแล้ว {progress.done}/{progress.total}</span>)}
      </div>
      {checklist.length > 0 && (
        <div className="ck-card">
          {checklist.map((e) => (
            <label className={e.isChecked ? 'ck-row done' : 'ck-row'} key={e.id}>
              <input type="checkbox" checked={e.isChecked} onChange={(ev) => toggleChecklist(e.id, ev.target.checked)} />
              <span className="ck-name">{e.name}</span>
              <button type="button" className="ck-del" aria-label="เอาออก" onClick={(ev) => { ev.preventDefault(); removeChecklist(e.id) }}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
              </button>
            </label>
          ))}
        </div>
      )}
      <div className="ck-add-wrap">
        <div className="ck-add-in">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
          <input value={ckDraft} placeholder="พิมพ์ของที่ต้องเตรียม…" onChange={(ev) => setCkDraft(ev.target.value)}
            onKeyDown={(ev) => { if (ev.key === 'Enter') { ev.preventDefault(); if (ckDraft.trim()) addChecklist(ckDraft) } }} />
        </div>
        {ckDraft.trim().length > 0 && (suggestions.length > 0 || showCreate) && (
          <div className="ck-ac">
            {suggestions.length > 0 && <div className="ac-h">จากคลังของคุณ</div>}
            {suggestions.map((i) => (
              <button type="button" key={i.id} onClick={() => addChecklist(i.name)}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true"><path d="M3 12h18M3 6h18M3 18h18" /></svg>
                {i.name}<span className="lib">ในคลัง</span>
              </button>
            ))}
            {showCreate && (
              <button type="button" className="create" onClick={() => addChecklist(ckDraft)}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
                สร้าง “{normalizeChecklistName(ckDraft)}” ใหม่
              </button>
            )}
          </div>
        )}
      </div>
      {ckError && <p className="trips-field-error">{ckError}</p>}
    </section>
  )
}
```

- [ ] **Step 3: Refactor `StopEditorDialog` to consume the sections**

- Add imports: `import {ReviewLinksSection} from './ReviewLinksSection'` and `import {ChecklistSection} from './ChecklistSection'`.
- Delete the now-moved checklist state/handlers (`library`, `attachChecklist`, `detachChecklist`, `setChecklistChecked`, `ckDraft`, `ckError`, `checklist`, `progress`, `attachedItemIds`, `suggestions`, `showCreate`, `addChecklist`, `toggleChecklist`, `removeChecklist`) and the checklist-related imports (`useListChecklistItemsQuery`, `useAttachChecklistItemMutation`, `useDetachChecklistItemMutation`, `useSetChecklistEntryCheckedMutation`, `ChecklistIcon`, and the `lib/checklist` imports) — they now live in `ChecklistSection`.
- Replace the inline review-links `<section>` (lines ~249-291) with `<ReviewLinksSection drafts={reviewDrafts} onChange={setReviewDrafts} />`.
- Replace the inline checklist `<section>` (lines ~293-372, the `{place && (…)}` block) with `{place && <ChecklistSection tripId={tripId} placeId={place.id} checklist={place.checklist ?? []} />}`.
- Keep everything else (best-time bar, dwell, travel mode, preview, save/delete). The `save()` still writes best-time + reviews via `updateTripPlace`.

- [ ] **Step 4: Typecheck + build**

Run: `cd frontend; npx tsc -b; npm run build`
Expected: PASS. No unused-symbol errors (all moved symbols removed from `StopEditorDialog`).

- [ ] **Step 5: Interactive parity check (REQUIRED — no component harness)**

Run the app (or open `docs/mocks/trip-stop-editor-mock.html` for visual reference). In a seeded/authed trip, open a Stop from แผนเที่ยว: verify best-time, review links (add/edit/remove), and checklist (add via autocomplete, tick "เตรียมแล้ว", remove) all behave exactly as before. This proves the extraction is behavior-preserving.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/ReviewLinksSection.tsx frontend/src/pages/trips/components/ChecklistSection.tsx frontend/src/pages/trips/components/StopEditorDialog.tsx
git commit -m "refactor(trips): extract ReviewLinksSection + ChecklistSection shared editor sections (#37)"
```

---

## Task 7: `PlaceEditorDialog` + wire the Places tab entry point

**Files:**
- Create: `frontend/src/pages/trips/components/PlaceEditorDialog.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`, `frontend/src/pages/trips/TripDetailPage.css`

**Interfaces:**
- Consumes: `ReviewLinksSection`, `ChecklistSection`, `BestTimeBar`, `useUpdateTripPlaceMutation`, `useDeleteTripPlaceMutation`, `usePushPlaceProfileMutation`, `lib/reviewLinks` (`sanitizeReviewDrafts`, `draftsValid`, `MAX_REVIEW_LINKS`, `ReviewDraft`), `setPlaceEditor` (Task 5), `placeCategory` (`catColor`, `catLabel`).

**No unit test** — dialog UI; gate = `tsc` + `build` + **interactive** verification of the full flow.

- [ ] **Step 1: Create `PlaceEditorDialog.tsx`**

```tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {
  useUpdateTripPlaceMutation,
  useDeleteTripPlaceMutation,
  usePushPlaceProfileMutation,
  type TripPlaceDto,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {catColor, catLabel} from '../placeCategory'
import {BestTimeBar} from './BestTimeBar'
import {ReviewLinksSection} from './ReviewLinksSection'
import {ChecklistSection} from './ChecklistSection'
import {sanitizeReviewDrafts, draftsValid, MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'

export function PlaceEditorDialog({
  tripId,
  place,
  onClose,
}: {
  tripId: string
  place: TripPlaceDto
  onClose: () => void
}) {
  const [bestStart, setBestStart] = useState<string | null>(place.bestTimeStart ?? null)
  const [bestEnd, setBestEnd] = useState<string | null>(place.bestTimeEnd ?? null)
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraft[]>(
    (place.reviewLinks ?? []).map((l) => ({url: l.url, label: l.label ?? ''})),
  )
  const [saveError, setSaveError] = useState<string | null>(null)

  const [updatePlace, {isLoading: saving}] = useUpdateTripPlaceMutation()
  const [deletePlace] = useDeleteTripPlaceMutation()
  const [pushProfile, {isLoading: pushing}] = usePushPlaceProfileMutation()

  const save = async () => {
    setSaveError(null)
    if (!draftsValid(reviewDrafts)) { setSaveError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`); return }
    try {
      await updatePlace({
        tripId, placeId: place.id,
        name: place.name, category: place.category, address: place.address,
        feeNote: place.feeNote, notes: place.notes,
        bestTimeStart: bestStart, bestTimeEnd: bestEnd,
        reviewLinks: sanitizeReviewDrafts(reviewDrafts),
      }).unwrap()
      onClose()
    } catch (err) { setSaveError(getErrorMessage(err)) }
  }

  const handleDelete = async () => {
    setSaveError(null)
    try { await deletePlace({tripId, placeId: place.id}).unwrap(); onClose() }
    catch (err) { setSaveError(getErrorMessage(err)) }
  }

  const handlePush = async () => {
    setSaveError(null)
    // Save current best-time/reviews first so the master gets what's on screen.
    if (!draftsValid(reviewDrafts)) { setSaveError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`); return }
    try {
      await updatePlace({
        tripId, placeId: place.id,
        name: place.name, category: place.category, address: place.address,
        feeNote: place.feeNote, notes: place.notes,
        bestTimeStart: bestStart, bestTimeEnd: bestEnd,
        reviewLinks: sanitizeReviewDrafts(reviewDrafts),
      }).unwrap()
      await pushProfile({tripId, placeId: place.id}).unwrap()
    } catch (err) { setSaveError(getErrorMessage(err)) }
  }

  const header = (
    <div className="se-head">
      <div className="se-title">{place.name}</div>
      <div className="se-meta">
        <span className="se-cat">
          <span className="se-cat-dot" style={{background: catColor(place.category)}} />
          {catLabel(place.category)}
        </span>
        <span className="se-crumb">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h10" /></svg>
          คลังสถานที่
        </span>
      </div>
    </div>
  )

  return (
    <Dialog open onClose={onClose} modal className="stop-editor-dialog" header={header}
      style={{width: 'min(480px, calc(100vw - 24px))'}}>
      <div className="stop-editor">
        {place.hasProfile && (
          <div className="se-seed-hint">
            <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M20 6L9 17l-5-5" /></svg>
            เติมจากคลังของคุณ — แก้ในทริปนี้ได้เลย ไม่กระทบทริปอื่น
          </div>
        )}

        <BestTimeBar start={bestStart} end={bestEnd} onChange={(s, e) => { setBestStart(s); setBestEnd(e) }} />

        <ReviewLinksSection drafts={reviewDrafts} onChange={setReviewDrafts} />

        <ChecklistSection tripId={tripId} placeId={place.id} checklist={place.checklist ?? []} />

        {saveError && <p className="trips-field-error">{saveError}</p>}

        <div className="se-foot">
          <button type="button" className="se-delete" onClick={handleDelete}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" /></svg>
            เอาออกจากทริปนี้
          </button>
          <div className="se-actions">
            {place.googlePlaceId && (
              <button type="button" className="se-push" disabled={saving || pushing} onClick={handlePush}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 19V5M5 12l7-7 7 7" /></svg>
                ดันขึ้น master
              </button>
            )}
            <button type="button" className="se-save" disabled={saving || pushing} onClick={save}>บันทึก</button>
          </div>
        </div>
      </div>
    </Dialog>
  )
}
```

- [ ] **Step 2: Add the 3 new CSS rules** (values from the mock) to `TripDetailPage.css`, scoped under the existing `.stop-editor-dialog`:

```css
.stop-editor-dialog .se-seed-hint { display:flex; align-items:center; gap:8px; margin:2px 0 4px; padding:9px 13px;
  border-radius:12px; background:#e9f6ee; border:1px solid #cdead8; color:#1f7a45; font-size:12px; font-weight:600; }
.stop-editor-dialog .se-seed-hint svg { flex:none; }
.stop-editor-dialog .se-actions { display:flex; align-items:center; gap:10px; }
.stop-editor-dialog .se-push { display:inline-flex; align-items:center; gap:7px; border:1.5px solid #f6d9bf; border-radius:13px;
  background:#fff; color:#d95f22; font:inherit; font-size:13px; font-weight:700; padding:11px 16px; cursor:pointer; }
.stop-editor-dialog .se-push:disabled { opacity:.6; cursor:default; }
```

- [ ] **Step 3: Wire the entry point in `TripDetailPage.tsx`**

- Add imports: `import {PlaceEditorDialog} from './components/PlaceEditorDialog'` and add `setPlaceEditor` to the existing `tripsSlice` import.
- Read state: `const placeEditorPlaceId = useAppSelector((s) => s.trips.placeEditorPlaceId)` and `const editingPlace = (places ?? []).find((p) => p.id === placeEditorPlaceId)`.
- On each `<PlaceCard>` (desktop list at line ~116 and mobile list at line ~207) add `onClick={() => dispatch(setPlaceEditor(p.id))}`.
- Render the dialog once, before the closing `</section>` of BOTH the desktop and mobile returns (or factor a shared fragment):

```tsx
{editingPlace && (
  <PlaceEditorDialog
    tripId={tripId}
    place={editingPlace}
    onClose={() => dispatch(setPlaceEditor(null))}
  />
)}
```

- [ ] **Step 4: Typecheck + build**

Run: `cd frontend; npx tsc -b; npm run build`
Expected: PASS.

- [ ] **Step 5: Interactive verification (REQUIRED)** — in a seeded/authed env:
  1. คลังสถานที่ tab → click a place card → dialog opens with the 3 sections + "คลังสถานที่" crumb.
  2. Set best-time + a review link + a checklist item → บันทึก → reopen: values persisted.
  3. Add the same Google place to a SECOND trip → it seeds best-time/reviews/checklist; the green "เติมจากคลังของคุณ" hint shows.
  4. Edit in the second trip → first trip unchanged (per-trip override).
  5. "ดันขึ้น master" in the second trip → capture the place in a THIRD trip → gets the pushed values.
  6. "เอาออกจากทริปนี้" removes it from the tab; re-add via search → data returns.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/PlaceEditorDialog.tsx frontend/src/pages/trips/TripDetailPage.tsx frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): edit place fields from คลังสถานที่ + push-to-master (closes #37)"
```

---

## Task 8: Full-suite verification + apply the migration to prod

**Files:** none (verification + deploy step).

- [ ] **Step 1: Run the full suite (pre-commit parity)**

Run: `cd backend; dotnet build -c Release; dotnet test -c Release` then `cd frontend; npx tsc -b; npm run build`
Expected: all green.

- [ ] **Step 2: Preview the migration SQL**

Run:
```bash
cd backend
dotnet ef migrations script --idempotent --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Confirm it creates `PlaceProfiles` + `PlaceProfileChecklistItems` with the expected indexes.

- [ ] **Step 3: Apply the migration to prod BY HAND** (after merge/deploy; requires the terminal `az` session = `thodsaphonSP@hotmail.co.th`):

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```
Expected: the two tables exist in prod (otherwise the deployed API throws `Invalid object name 'PlaceProfiles'`).

- [ ] **Step 4: Push**

```bash
git push main HEAD:main
```

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- Place editor (spec §7, ADR-062) → Task 6 (shared sections) + Task 7 (dialog + entry point).
- PlaceProfile master + junction (spec §3, ADR-063) → Task 1.
- Seed-on-capture (spec §4.1, ADR-064) → Task 2.
- Auto-create on first enrichment (spec §4.2, ADR-064) → Task 3.
- Push-to-master (spec §4.3, ADR-064) → Task 4 (backend) + Task 7 (button).
- Remove-from-trip keeps master (spec §4.4, ADR-065) → Task 7 (wires existing `deleteTripPlace`; master untouched by construction).
- `HasProfile` / seed hint (spec §5, §7) → Task 2 (backend) + Task 5/7 (frontend).
- MCP parity (spec §8) → free: MCP `add_trip_place` forwards `AddTripPlaceCommand`, which now seeds (Task 2). No MCP file changes. (push over MCP = Phase 2, ADR-066.)
- Migration applied by hand (spec §6) → Task 1 (generate) + Task 8 (apply).
- Phase-2 cuts (spec §11) — intentionally NOT built: master-management screen, push-over-MCP, non-place_id profiles, card badges.

**2. Placeholder scan** — no TBD/TODO; every code step shows full code; every test step shows real assertions.

**3. Type consistency** — `PlaceProfileSync` method names (`SeedIntoAsync`, `ExistsAsync`, `EnsureCreatedAsync`, `UpsertFromAsync`) are used identically across Tasks 2-4. `TripPlaceDto.HasProfile` / `hasProfile` param on `ToDto(p, checklist, hasProfile)` are consistent across Add/Update/List/Push handlers and the frontend `TripPlaceDto.hasProfile`. `PushPlaceProfileCommand : ICommand<TripPlaceDto>` matches the controller/handler return. Frontend `usePushPlaceProfileMutation` / `setPlaceEditor` / `placeEditorPlaceId` names match across Tasks 5-7.

**Note for the executor:** commit boundaries keep the suite green — each backend task commit includes its entity/config/context/handler + tests together; the positional `TripPlaceDto.HasProfile` change (Task 2) updates the single `ToDto` constructor and all callers in the same commit.