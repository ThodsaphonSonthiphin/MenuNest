# Place Checklist Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-Place checklist of things to bring/prepare ("สิ่งที่ต้องเตรียม"), editable in the Stop editor modal, where each item is a User-scoped reusable library entry attached to Places and checked per-Place.

**Architecture:** New User-scoped `ChecklistItem` (the library) + relational junction `PlaceChecklistEntry` (per-Place, carries `IsChecked`). Backend is Clean Architecture (Domain → Application → Infrastructure/WebApi/McpServer) with the Mediator library; the Trip aggregate is FK-only (no EF navigation graph). The Place's checklist is embedded in `TripPlaceDto` (delivered by `listTripPlaces`) so the modal needs no new prop; writes are granular (attach / detach / toggle-check), the toggle being an optimistic non-invalidating RTK mutation like `setStopVisited` (ADR-042). Exposed over REST + MCP.

**Tech Stack:** .NET (C#, Mediator, EF Core, FluentValidation, xUnit + FluentAssertions + SQLite relational tests); React + TypeScript + Redux Toolkit / RTK Query + Syncfusion; Vitest (node env, no jsdom).

## Global Constraints

- **Decisions are fixed** — implement exactly per ADR-058 (User-scoped reusable library, NOT per-Place JSON), ADR-059 (junction + per-Place `IsChecked`, detach ≠ delete, orphans persist), ADR-060 (granular attach/detach/toggle + embed-in-DTO read + MCP), ADR-061 (Phase-1 modal-only; NO card badge, NO library-management/rename/delete UI). Spec: `docs/superpowers/specs/2026-07-13-place-checklist-items-design.md`. Mock: `docs/mocks/trip-place-checklist-mock.html`.
- **Pre-commit hook runs the FULL suite** (backend `dotnet build` + `dotnet test` Release, frontend `tsc --noEmit` + `npm run build`, ~40s+). Every commit must leave the ENTIRE suite green. Do NOT `--no-verify`.
- **EF entity + configuration + DbSet must land in the SAME commit** — an entity added to the DbContext without its mapping fails EF model validation for every DbContext test (learned on #33).
- **A new DbSet must be added to BOTH** `AppDbContext` AND the test `SqliteAppDbContext` (the test context duplicates the full DbSet list) — else relational tests can't see the entity.
- **EF configurations auto-apply** via `ApplyConfigurationsFromAssembly` — just drop `internal sealed class XConfiguration : IEntityTypeConfiguration<X>` in `src/MenuNest.Infrastructure/Persistence/Configurations/`. No manual registration.
- **Migration is applied to prod BY HAND after merge** (neither the app nor CD runs `Migrate()`) — out of plan scope; generate the migration file in Task 3 and preview with `dotnet ef migrations script --idempotent`.
- **Bounds:** name max 100 chars; ≤ 20 entries per Place; name unique per User (case-insensitive).
- **Icons are inline-SVG components, never emoji** (trips convention; `docs/frontend-guidelines.md`).
- **Commit style:** conventional `feat(trips): …`; every commit references the ticket — intermediate commits end the subject with `(#23)`, the final commit may use `(closes #23)`.
- **Stage narrowly:** always `git add <explicit paths>`, never `git add -A`/`.` (`daily-state.md` and `AGENTS.md` must never be swept in).

## File Structure

**Backend (new files)**
- `backend/src/MenuNest.Domain/Entities/ChecklistItem.cs` — User-scoped library entity.
- `backend/src/MenuNest.Domain/Entities/PlaceChecklistEntry.cs` — junction entity.
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChecklistItemConfiguration.cs`
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceChecklistEntryConfiguration.cs`
- `backend/src/MenuNest.Application/UseCases/Trips/ListChecklistItems/{ListChecklistItemsQuery,ListChecklistItemsHandler}.cs`
- `backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/{AttachChecklistItemCommand,AttachChecklistItemValidator,AttachChecklistItemHandler}.cs`
- `backend/src/MenuNest.Application/UseCases/Trips/DetachChecklistItem/{DetachChecklistItemCommand,DetachChecklistItemHandler}.cs`
- `backend/src/MenuNest.Application/UseCases/Trips/SetChecklistEntryChecked/{SetChecklistEntryCheckedCommand,SetChecklistEntryCheckedHandler}.cs`
- Backend tests under `backend/tests/MenuNest.Application.UnitTests/Trips/Checklist*.cs`

**Backend (modified)**
- `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` — add 2 DbSets.
- `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` — add 2 DbSets.
- `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs` — add 2 DbSets.
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — `ChecklistItemDto`, `PlaceChecklistEntryDto`, extend `TripPlaceDto`.
- `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs` — `ToDto` overload.
- `backend/src/MenuNest.Application/UseCases/Trips/ListTripPlaces/ListTripPlacesHandler.cs` — project checklist.
- `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs` — project checklist.
- `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — 4 endpoints + body records.
- `backend/src/MenuNest.McpServer/Tools/TripTools.cs` — 4 MCP tools.

**Frontend (new files)**
- `frontend/src/pages/trips/lib/checklist.ts` + `checklist.test.ts`
- `frontend/src/pages/trips/components/ChecklistIcon.tsx`

**Frontend (modified)**
- `frontend/src/shared/api/api.ts` — types, tag, 4 endpoints, hooks.
- `frontend/src/pages/trips/components/StopEditorDialog.tsx` — checklist section.
- `frontend/src/pages/trips/TripDetailPage.css` — checklist styles.

---

## Task 1: Domain — `ChecklistItem` entity

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/ChecklistItem.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ChecklistItemDomainTests.cs`

**Interfaces:**
- Produces: `ChecklistItem` (sealed, `: Entity`) with `Guid UserId`, `string Name`; static `ChecklistItem.Create(Guid userId, string name)` (trims, non-empty, max 100, throws `DomainException`).

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ChecklistItemDomainTests
{
    [Fact]
    public void Create_trims_and_sets_fields()
    {
        var userId = Guid.NewGuid();
        var item = ChecklistItem.Create(userId, "  ร่ม  ");
        item.UserId.Should().Be(userId);
        item.Name.Should().Be("ร่ม");
        item.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => ChecklistItem.Create(Guid.NewGuid(), name);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_name_over_100_chars()
    {
        var act = () => ChecklistItem.Create(Guid.NewGuid(), new string('x', 101));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_empty_userId()
    {
        var act = () => ChecklistItem.Create(Guid.Empty, "ร่ม");
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ChecklistItemDomainTests`
Expected: FAIL to compile — `ChecklistItem` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/MenuNest.Domain/Entities/ChecklistItem.cs`:

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A User-scoped, reusable checklist label ("thing to bring/prepare for a place",
/// e.g. ร่ม / พาสปอร์ต / ครีมกันแดด). Owned by the User (not a Trip/Place) so one item is
/// reused across many Places and Trips (ADR-058). Attached to a Place via PlaceChecklistEntry.
/// </summary>
public sealed class ChecklistItem : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;

    private ChecklistItem() { } // EF

    public static ChecklistItem Create(Guid userId, string name)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required for a checklist item.");
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new DomainException("Checklist item name is required.");
        if (n.Length > 100) throw new DomainException("Checklist item name is too long (max 100).");
        return new ChecklistItem { UserId = userId, Name = n };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ChecklistItemDomainTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/ChecklistItem.cs backend/tests/MenuNest.Application.UnitTests/Trips/ChecklistItemDomainTests.cs
git commit -m "feat(trips): add ChecklistItem domain entity (#23)"
```

---

## Task 2: Domain — `PlaceChecklistEntry` entity

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/PlaceChecklistEntry.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceChecklistEntryDomainTests.cs`

**Interfaces:**
- Produces: `PlaceChecklistEntry` (sealed, `: Entity`) with `Guid TripPlaceId`, `Guid ChecklistItemId`, `bool IsChecked`; static `Create(Guid tripPlaceId, Guid checklistItemId)` (IsChecked=false); `SetChecked(bool)`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceChecklistEntryDomainTests
{
    [Fact]
    public void Create_sets_fields_unchecked()
    {
        var placeId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var e = PlaceChecklistEntry.Create(placeId, itemId);
        e.TripPlaceId.Should().Be(placeId);
        e.ChecklistItemId.Should().Be(itemId);
        e.IsChecked.Should().BeFalse();
        e.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SetChecked_toggles()
    {
        var e = PlaceChecklistEntry.Create(Guid.NewGuid(), Guid.NewGuid());
        e.SetChecked(true);
        e.IsChecked.Should().BeTrue();
        e.SetChecked(false);
        e.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_empty_ids()
    {
        FluentActions.Invoking(() => PlaceChecklistEntry.Create(Guid.Empty, Guid.NewGuid())).Should().Throw<DomainException>();
        FluentActions.Invoking(() => PlaceChecklistEntry.Create(Guid.NewGuid(), Guid.Empty)).Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter PlaceChecklistEntryDomainTests`
Expected: FAIL to compile — `PlaceChecklistEntry` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/MenuNest.Domain/Entities/PlaceChecklistEntry.cs`:

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The attachment of one <see cref="ChecklistItem"/> to one Place (TripPlace), carrying a
/// per-Place <see cref="IsChecked"/> flag ("เตรียมแล้ว"). Detaching removes this row only,
/// never the library <see cref="ChecklistItem"/> (ADR-059).
/// </summary>
public sealed class PlaceChecklistEntry : Entity
{
    public Guid TripPlaceId { get; private set; }
    public Guid ChecklistItemId { get; private set; }
    public bool IsChecked { get; private set; }

    private PlaceChecklistEntry() { } // EF

    public static PlaceChecklistEntry Create(Guid tripPlaceId, Guid checklistItemId)
    {
        if (tripPlaceId == Guid.Empty) throw new DomainException("TripPlaceId is required.");
        if (checklistItemId == Guid.Empty) throw new DomainException("ChecklistItemId is required.");
        return new PlaceChecklistEntry { TripPlaceId = tripPlaceId, ChecklistItemId = checklistItemId, IsChecked = false };
    }

    public void SetChecked(bool isChecked)
    {
        IsChecked = isChecked;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter PlaceChecklistEntryDomainTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/PlaceChecklistEntry.cs backend/tests/MenuNest.Application.UnitTests/Trips/PlaceChecklistEntryDomainTests.cs
git commit -m "feat(trips): add PlaceChecklistEntry domain entity (#23)"
```

---
## Task 3: Persistence — EF configs, DbSets, migration, relational round-trip

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChecklistItemConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceChecklistEntryConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`
- Create (generated): a migration under `backend/src/MenuNest.Infrastructure/Persistence/Migrations/`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ChecklistPersistenceRelationalTests.cs`

**Interfaces:**
- Consumes: `ChecklistItem`, `PlaceChecklistEntry` (Tasks 1-2); `SqliteAppDbContext` harness.
- Produces: `IApplicationDbContext.ChecklistItems` and `.PlaceChecklistEntries` `DbSet<>`s; tables `ChecklistItems` (unique `(UserId, Name)`) and `PlaceChecklistEntries` (unique `(TripPlaceId, ChecklistItemId)`, cascade from TripPlace, restrict from ChecklistItem).

> This whole task is ONE commit — entities+configs+DbSets+migration land together (EF model-validation rule).

- [ ] **Step 1: Write the failing relational test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/ChecklistPersistenceRelationalTests.cs`:

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

public sealed class ChecklistPersistenceRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ChecklistPersistenceRelationalTests()
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
    public async Task Item_and_entry_round_trip()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        entry.SetChecked(true);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var readItem = await _db.ChecklistItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
        var readEntry = await _db.PlaceChecklistEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id);
        readItem.Name.Should().Be("ร่ม");
        readItem.UserId.Should().Be(_user.Id);
        readEntry.TripPlaceId.Should().Be(place.Id);
        readEntry.ChecklistItemId.Should().Be(item.Id);
        readEntry.IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_item_name_for_same_user_violates_unique_index()
    {
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ร่ม"));
        await _db.SaveChangesAsync();
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ร่ม"));
        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ChecklistPersistenceRelationalTests`
Expected: FAIL to compile — `_db.ChecklistItems` / `_db.PlaceChecklistEntries` do not exist.

- [ ] **Step 3: Add the two DbSets to `IApplicationDbContext.cs`**

In the `// Trip Planner module` block, after `DbSet<Stop> Stops { get; }`:

```csharp
    DbSet<ChecklistItem> ChecklistItems { get; }
    DbSet<PlaceChecklistEntry> PlaceChecklistEntries { get; }
```

- [ ] **Step 4: Add the two DbSets to `AppDbContext.cs`**

In the `// Trip Planner module` block, after `public DbSet<Stop> Stops => Set<Stop>();`:

```csharp
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<PlaceChecklistEntry> PlaceChecklistEntries => Set<PlaceChecklistEntry>();
```

- [ ] **Step 5: Add the same two DbSets to the test `SqliteAppDbContext.cs`**

In its `// Trip Planner module` block, after `public DbSet<Stop> Stops => Set<Stop>();`:

```csharp
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<PlaceChecklistEntry> PlaceChecklistEntries => Set<PlaceChecklistEntry>();
```

- [ ] **Step 6: Create `ChecklistItemConfiguration.cs`**

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> b)
    {
        b.ToTable("ChecklistItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        // One library row per name per user. SQL Server's default collation is
        // case-insensitive, so this also blocks "Umbrella"/"umbrella" dupes in prod;
        // the attach handler additionally reuses by LOWER(name) for provider-independence.
        b.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 7: Create `PlaceChecklistEntryConfiguration.cs`**

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceChecklistEntryConfiguration : IEntityTypeConfiguration<PlaceChecklistEntry>
{
    public void Configure(EntityTypeBuilder<PlaceChecklistEntry> b)
    {
        b.ToTable("PlaceChecklistEntries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.TripPlaceId).IsRequired();
        b.Property(e => e.ChecklistItemId).IsRequired();
        b.Property(e => e.IsChecked).IsRequired();
        // An item attaches to a place at most once.
        b.HasIndex(e => new { e.TripPlaceId, e.ChecklistItemId }).IsUnique();
        // Deleting a Place (or a Trip cascading its Places) removes its entries…
        b.HasOne<TripPlace>().WithMany().HasForeignKey(e => e.TripPlaceId).OnDelete(DeleteBehavior.Cascade);
        // …but deleting a Place must NEVER touch the library item (ADR-059).
        b.HasOne<ChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 8: Generate the migration**

Run:
```bash
cd backend
dotnet ef migrations add AddChecklistItems --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: a new migration file appears under `src/MenuNest.Infrastructure/Persistence/Migrations/` creating both tables + indexes + FKs. Open it and confirm it creates `ChecklistItems` and `PlaceChecklistEntries` (no unrelated changes). Preview the SQL:
```bash
dotnet ef migrations script --idempotent --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
(Do NOT apply to prod here — that is a manual post-merge step per CLAUDE.md.)

- [ ] **Step 9: Build + run the relational tests**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ChecklistPersistenceRelationalTests`
Expected: PASS (2 tests). If `Duplicate…` fails, verify the unique index in `ChecklistItemConfiguration`.

- [ ] **Step 10: Full suite (pre-commit dry run) + commit**

Run: `cd backend && dotnet test`
Expected: all green.

```bash
git add backend/src/MenuNest.Domain/Entities/ChecklistItem.cs backend/src/MenuNest.Domain/Entities/PlaceChecklistEntry.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChecklistItemConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceChecklistEntryConfiguration.cs backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Trips/ChecklistPersistenceRelationalTests.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(trips): persist ChecklistItem + PlaceChecklistEntry (EF config + migration) (#23)"
```

---

## Task 4: Application read model — embed checklist in `TripPlaceDto`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ListTripPlaces/ListTripPlacesHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ListTripPlacesChecklistRelationalTests.cs`

**Interfaces:**
- Consumes: DbSets (Task 3); `AddTripPlaceHandler.ToDto`.
- Produces: `ChecklistItemDto(Guid Id, string Name)`; `PlaceChecklistEntryDto(Guid Id, Guid ChecklistItemId, string Name, bool IsChecked)`; `TripPlaceDto` gains trailing `IReadOnlyList<PlaceChecklistEntryDto> Checklist`; `AddTripPlaceHandler.ToDto(TripPlace)` (empty checklist) + `ToDto(TripPlace, IReadOnlyList<PlaceChecklistEntryDto>)`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/ListTripPlacesChecklistRelationalTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ListTripPlacesChecklistRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListTripPlacesChecklistRelationalTests()
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
    public async Task ListTripPlaces_embeds_checklist_entries_with_name_and_checked()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        entry.SetChecked(true);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        var users = Substitute.For<IUserProvisioner>();
        users.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>()).Returns(_user);
        var handler = new ListTripPlacesHandler(_db, users);

        var result = await handler.Handle(new ListTripPlacesQuery(trip.Id), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Checklist.Should().ContainSingle();
        result[0].Checklist[0].Name.Should().Be("ร่ม");
        result[0].Checklist[0].ChecklistItemId.Should().Be(item.Id);
        result[0].Checklist[0].IsChecked.Should().BeTrue();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

(Note: `IUserProvisioner` is mocked with NSubstitute — confirm NSubstitute is the test project's mocking lib; if it uses Moq, translate to `new Mock<IUserProvisioner>()`. Check an existing handler test in the same folder for the pattern.)

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListTripPlacesChecklistRelationalTests`
Expected: FAIL to compile — `TripPlaceDto` has no `Checklist` member.

- [ ] **Step 3: Add DTOs + extend `TripPlaceDto` in `TripDtos.cs`**

Add near `ReviewLinkDto`:

```csharp
public sealed record ChecklistItemDto(Guid Id, string Name);

public sealed record PlaceChecklistEntryDto(Guid Id, Guid ChecklistItemId, string Name, bool IsChecked);
```

Append `Checklist` as the LAST positional parameter of `TripPlaceDto`:

```csharp
public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<PlaceChecklistEntryDto> Checklist);
```

- [ ] **Step 4: Add the `ToDto` overload in `AddTripPlaceHandler.cs`**

Replace the single `ToDto` with an overloaded pair (existing single-arg callers keep working via the empty overload):

```csharp
    internal static TripPlaceDto ToDto(TripPlace p) => ToDto(p, Array.Empty<PlaceChecklistEntryDto>());

    internal static TripPlaceDto ToDto(TripPlace p, IReadOnlyList<PlaceChecklistEntryDto> checklist) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.BestTimeStart, p.BestTimeEnd, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        checklist);
```

- [ ] **Step 5: Project checklist in `ListTripPlacesHandler.cs`**

Replace the final `return places.Select(...)` block with a join that loads all entries for the trip's places in one query:

```csharp
        var placeIds = places.Select(p => p.Id).ToList();
        var entries = await (from e in _db.PlaceChecklistEntries
                             join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                             where placeIds.Contains(e.TripPlaceId)
                             select new { e.TripPlaceId, Dto = new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked) })
                            .ToListAsync(ct);
        var byPlace = entries.GroupBy(x => x.TripPlaceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlaceChecklistEntryDto>)g.Select(x => x.Dto).ToList());

        return places
            .Select(p => AddTripPlaceHandler.ToDto(p, byPlace.TryGetValue(p.Id, out var l) ? l : Array.Empty<PlaceChecklistEntryDto>()))
            .ToList();
```

- [ ] **Step 6: Project checklist in `UpdateTripPlaceHandler.cs`**

Replace `return AddTripPlaceHandler.ToDto(place);` with:

```csharp
        var checklist = await (from e in _db.PlaceChecklistEntries
                               join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                               where e.TripPlaceId == place.Id
                               select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked))
                              .ToListAsync(ct);
        return AddTripPlaceHandler.ToDto(place, checklist);
```

- [ ] **Step 7: Build — fix any remaining `TripPlaceDto` construction sites**

Run: `cd backend && dotnet build`
Expected: PASS. The `ToDto` empty overload keeps `AddTripPlaceHandler` and any test calling `ToDto(place)` compiling. If the build flags a direct `new TripPlaceDto(...)` anywhere, append `, Array.Empty<PlaceChecklistEntryDto>()` as its last argument.

- [ ] **Step 8: Run the test + full suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListTripPlacesChecklistRelationalTests` then `cd backend && dotnet test`
Expected: PASS; whole suite green.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs backend/src/MenuNest.Application/UseCases/Trips/ListTripPlaces/ListTripPlacesHandler.cs backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs backend/tests/MenuNest.Application.UnitTests/Trips/ListTripPlacesChecklistRelationalTests.cs
git commit -m "feat(trips): embed Place checklist in TripPlaceDto read model (#23)"
```

---
## Task 5: Application — `ListChecklistItems` query (the library / autocomplete source)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/ListChecklistItems/ListChecklistItemsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/ListChecklistItems/ListChecklistItemsHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ListChecklistItemsRelationalTests.cs`

**Interfaces:**
- Produces: `ListChecklistItemsQuery : IQuery<IReadOnlyList<ChecklistItemDto>>` (no params — user-scoped); handler returns the current User's items ordered by name.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ListChecklistItemsRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListChecklistItemsRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Returns_only_current_users_items_ordered_by_name()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "หมวก"));
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ครีมกันแดด"));
        _db.ChecklistItems.Add(ChecklistItem.Create(other.Id, "ของคนอื่น"));
        await _db.SaveChangesAsync();

        var users = Substitute.For<IUserProvisioner>();
        users.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>()).Returns(_user);
        var handler = new ListChecklistItemsHandler(_db, users);

        var result = await handler.Handle(new ListChecklistItemsQuery(), CancellationToken.None);

        result.Select(i => i.Name).Should().Equal("ครีมกันแดด", "หมวก");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListChecklistItemsRelationalTests`
Expected: FAIL to compile — query/handler do not exist.

- [ ] **Step 3: Create the query**

`ListChecklistItemsQuery.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.ListChecklistItems;

public sealed record ListChecklistItemsQuery : IQuery<IReadOnlyList<ChecklistItemDto>>;
```

- [ ] **Step 4: Create the handler**

`ListChecklistItemsHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.ListChecklistItems;

public sealed class ListChecklistItemsHandler : IQueryHandler<ListChecklistItemsQuery, IReadOnlyList<ChecklistItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListChecklistItemsHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<ChecklistItemDto>> Handle(ListChecklistItemsQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        return await _db.ChecklistItems
            .Where(i => i.UserId == user.Id)
            .OrderBy(i => i.Name)
            .Select(i => new ChecklistItemDto(i.Id, i.Name))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 5: Run test + commit**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ListChecklistItemsRelationalTests` → PASS.
```bash
git add backend/src/MenuNest.Application/UseCases/Trips/ListChecklistItems/ backend/tests/MenuNest.Application.UnitTests/Trips/ListChecklistItemsRelationalTests.cs
git commit -m "feat(trips): add ListChecklistItems query (library autocomplete source) (#23)"
```

---

## Task 6: Application — `AttachChecklistItem` (create-or-reuse by name)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/AttachChecklistItemCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/AttachChecklistItemValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/AttachChecklistItemHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AttachChecklistItemRelationalTests.cs`

**Interfaces:**
- Produces: `AttachChecklistItemCommand(Guid TripId, Guid PlaceId, string Name) : ICommand<PlaceChecklistEntryDto>`. Handler: verify trip ownership + place∈trip; reuse the User's item whose `LOWER(Name)` matches (else create it); create the `(place,item)` entry if absent (else return the existing one); return `PlaceChecklistEntryDto`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class AttachChecklistItemRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AttachChecklistItemCommand> _validator = new AttachChecklistItemValidator();

    public AttachChecklistItemRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
        _users = Substitute.For<IUserProvisioner>();
        _users.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>()).Returns(_user);
    }

    private (Guid tripId, Guid placeId) Seed()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        _db.SaveChanges();
        return (trip.Id, place.Id);
    }

    private AttachChecklistItemHandler Handler() => new(_db, _users, _validator);

    [Fact]
    public async Task New_name_creates_library_item_and_entry()
    {
        var (tripId, placeId) = Seed();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, " ร่ม "), CancellationToken.None);
        dto.Name.Should().Be("ร่ม");
        dto.IsChecked.Should().BeFalse();
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id && i.Name == "ร่ม")).Should().Be(1);
        (await _db.PlaceChecklistEntries.CountAsync(e => e.TripPlaceId == placeId)).Should().Be(1);
    }

    [Fact]
    public async Task Existing_name_is_reused_case_insensitively_no_duplicate_item()
    {
        var (tripId, placeId) = Seed();
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "Umbrella"));
        await _db.SaveChangesAsync();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "umbrella"), CancellationToken.None);
        dto.Name.Should().Be("Umbrella");
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Attaching_same_item_twice_is_idempotent()
    {
        var (tripId, placeId) = Seed();
        var first = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "ร่ม"), CancellationToken.None);
        var second = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "ร่ม"), CancellationToken.None);
        second.Id.Should().Be(first.Id);
        (await _db.PlaceChecklistEntries.CountAsync(e => e.TripPlaceId == placeId)).Should().Be(1);
    }

    [Fact]
    public async Task Rejects_trip_not_owned()
    {
        var (_, placeId) = Seed();
        var stranger = Substitute.For<IUserProvisioner>();
        stranger.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(User.CreateFromExternalLogin("oidX", "x@x.com", "X", AuthProvider.Microsoft));
        var handler = new AttachChecklistItemHandler(_db, stranger, _validator);
        var act = () => handler.Handle(new AttachChecklistItemCommand(Guid.NewGuid(), placeId, "ร่ม"), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AttachChecklistItemRelationalTests`
Expected: FAIL to compile — command/validator/handler do not exist.

- [ ] **Step 3: Create the command**

`AttachChecklistItemCommand.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed record AttachChecklistItemCommand(Guid TripId, Guid PlaceId, string Name)
    : ICommand<PlaceChecklistEntryDto>;
```

- [ ] **Step 4: Create the validator**

`AttachChecklistItemValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed class AttachChecklistItemValidator : AbstractValidator<AttachChecklistItemCommand>
{
    public AttachChecklistItemValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Checklist item name is required.")
            .Must(n => (n ?? string.Empty).Trim().Length <= 100).WithMessage("Checklist item name is too long (max 100).");
    }
}
```

- [ ] **Step 5: Create the handler**

`AttachChecklistItemHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed class AttachChecklistItemHandler : ICommandHandler<AttachChecklistItemCommand, PlaceChecklistEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AttachChecklistItemCommand> _validator;
    public AttachChecklistItemHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AttachChecklistItemCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<PlaceChecklistEntryDto> Handle(AttachChecklistItemCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var placeExists = await _db.TripPlaces.AnyAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct);
        if (!placeExists) throw new DomainException("Place not found.");

        var name = c.Name.Trim();
        var normalized = name.ToLower();
        // Reuse by case-insensitive name (LOWER translates on both SQL Server and SQLite);
        // the (UserId, Name) unique index is the race backstop.
        var item = await _db.ChecklistItems.FirstOrDefaultAsync(i => i.UserId == user.Id && i.Name.ToLower() == normalized, ct);
        if (item is null)
        {
            item = ChecklistItem.Create(user.Id, name);
            _db.ChecklistItems.Add(item);
        }

        var entry = await _db.PlaceChecklistEntries
            .FirstOrDefaultAsync(e => e.TripPlaceId == c.PlaceId && e.ChecklistItemId == item.Id, ct);
        if (entry is null)
        {
            entry = PlaceChecklistEntry.Create(c.PlaceId, item.Id);
            _db.PlaceChecklistEntries.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
        return new PlaceChecklistEntryDto(entry.Id, item.Id, item.Name, entry.IsChecked);
    }
}
```

- [ ] **Step 6: Run tests + commit**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AttachChecklistItemRelationalTests` → PASS (4).
```bash
git add backend/src/MenuNest.Application/UseCases/Trips/AttachChecklistItem/ backend/tests/MenuNest.Application.UnitTests/Trips/AttachChecklistItemRelationalTests.cs
git commit -m "feat(trips): AttachChecklistItem (create-or-reuse by name) (#23)"
```

---

## Task 7: Application — `DetachChecklistItem` (junction only)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/DetachChecklistItem/DetachChecklistItemCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/DetachChecklistItem/DetachChecklistItemHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/DetachChecklistItemRelationalTests.cs`

**Interfaces:**
- Produces: `DetachChecklistItemCommand(Guid TripId, Guid PlaceId, Guid EntryId) : ICommand<bool>`. Handler removes the entry (scoped to place∈trip owned by user); the library `ChecklistItem` is left intact.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class DetachChecklistItemRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public DetachChecklistItemRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Detach_removes_entry_but_keeps_library_item()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        var users = Substitute.For<IUserProvisioner>();
        users.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>()).Returns(_user);
        var handler = new DetachChecklistItemHandler(_db, users);

        await handler.Handle(new DetachChecklistItemCommand(trip.Id, place.Id, entry.Id), CancellationToken.None);

        (await _db.PlaceChecklistEntries.AnyAsync(e => e.Id == entry.Id)).Should().BeFalse();
        (await _db.ChecklistItems.AnyAsync(i => i.Id == item.Id)).Should().BeTrue(); // library survives
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter DetachChecklistItemRelationalTests`
Expected: FAIL to compile.

- [ ] **Step 3: Create the command**

`DetachChecklistItemCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Trips.DetachChecklistItem;

public sealed record DetachChecklistItemCommand(Guid TripId, Guid PlaceId, Guid EntryId) : ICommand<bool>;
```

- [ ] **Step 4: Create the handler**

`DetachChecklistItemHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.DetachChecklistItem;

public sealed class DetachChecklistItemHandler : ICommandHandler<DetachChecklistItemCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DetachChecklistItemHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<bool> Handle(DetachChecklistItemCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var placeExists = await _db.TripPlaces.AnyAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct);
        if (!placeExists) throw new DomainException("Place not found.");

        var entry = await _db.PlaceChecklistEntries.FirstOrDefaultAsync(e => e.Id == c.EntryId && e.TripPlaceId == c.PlaceId, ct)
            ?? throw new DomainException("Checklist entry not found.");
        _db.PlaceChecklistEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 5: Run test + commit**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter DetachChecklistItemRelationalTests` → PASS.
```bash
git add backend/src/MenuNest.Application/UseCases/Trips/DetachChecklistItem/ backend/tests/MenuNest.Application.UnitTests/Trips/DetachChecklistItemRelationalTests.cs
git commit -m "feat(trips): DetachChecklistItem (removes junction, keeps library) (#23)"
```

---

## Task 8: Application — `SetChecklistEntryChecked` (per-place toggle)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/SetChecklistEntryChecked/SetChecklistEntryCheckedCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/SetChecklistEntryChecked/SetChecklistEntryCheckedHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/SetChecklistEntryCheckedRelationalTests.cs`

**Interfaces:**
- Produces: `SetChecklistEntryCheckedCommand(Guid TripId, Guid PlaceId, Guid EntryId, bool IsChecked) : ICommand<PlaceChecklistEntryDto>`. Handler sets `IsChecked` on the entry (scoped) and returns the updated DTO (with the joined item name).

- [ ] **Step 1: Write the failing test** (also proves per-place independence)

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class SetChecklistEntryCheckedRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public SetChecklistEntryCheckedRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Check_is_per_place_independent()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        _db.Trips.Add(trip);
        var placeA = TripPlace.Create(trip.Id, "Beach", 0, 0, PlaceCategory.See);
        var placeB = TripPlace.Create(trip.Id, "Market", 1, 1, PlaceCategory.Shop);
        _db.TripPlaces.AddRange(placeA, placeB);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entryA = PlaceChecklistEntry.Create(placeA.Id, item.Id);
        var entryB = PlaceChecklistEntry.Create(placeB.Id, item.Id);
        _db.PlaceChecklistEntries.AddRange(entryA, entryB);
        await _db.SaveChangesAsync();

        var users = Substitute.For<IUserProvisioner>();
        users.GetOrProvisionCurrentAsync(Arg.Any<CancellationToken>()).Returns(_user);
        var handler = new SetChecklistEntryCheckedHandler(_db, users);

        var dto = await handler.Handle(new SetChecklistEntryCheckedCommand(trip.Id, placeA.Id, entryA.Id, true), CancellationToken.None);

        dto.IsChecked.Should().BeTrue();
        dto.Name.Should().Be("ร่ม");
        (await _db.PlaceChecklistEntries.FirstAsync(e => e.Id == entryA.Id)).IsChecked.Should().BeTrue();
        (await _db.PlaceChecklistEntries.FirstAsync(e => e.Id == entryB.Id)).IsChecked.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter SetChecklistEntryCheckedRelationalTests`
Expected: FAIL to compile.

- [ ] **Step 3: Create the command**

`SetChecklistEntryCheckedCommand.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;

public sealed record SetChecklistEntryCheckedCommand(Guid TripId, Guid PlaceId, Guid EntryId, bool IsChecked)
    : ICommand<PlaceChecklistEntryDto>;
```

- [ ] **Step 4: Create the handler**

`SetChecklistEntryCheckedHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;

public sealed class SetChecklistEntryCheckedHandler : ICommandHandler<SetChecklistEntryCheckedCommand, PlaceChecklistEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public SetChecklistEntryCheckedHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<PlaceChecklistEntryDto> Handle(SetChecklistEntryCheckedCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var placeExists = await _db.TripPlaces.AnyAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct);
        if (!placeExists) throw new DomainException("Place not found.");

        var entry = await _db.PlaceChecklistEntries.FirstOrDefaultAsync(e => e.Id == c.EntryId && e.TripPlaceId == c.PlaceId, ct)
            ?? throw new DomainException("Checklist entry not found.");
        entry.SetChecked(c.IsChecked);
        await _db.SaveChangesAsync(ct);

        var name = await _db.ChecklistItems.Where(i => i.Id == entry.ChecklistItemId).Select(i => i.Name).FirstAsync(ct);
        return new PlaceChecklistEntryDto(entry.Id, entry.ChecklistItemId, name, entry.IsChecked);
    }
}
```

- [ ] **Step 5: Run test + full suite + commit**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter SetChecklistEntryCheckedRelationalTests` → PASS, then `cd backend && dotnet test` → all green.
```bash
git add backend/src/MenuNest.Application/UseCases/Trips/SetChecklistEntryChecked/ backend/tests/MenuNest.Application.UnitTests/Trips/SetChecklistEntryCheckedRelationalTests.cs
git commit -m "feat(trips): SetChecklistEntryChecked per-place toggle (#23)"
```

---
## Task 9: WebApi — 4 endpoints on `TripsController`

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`

**Interfaces:**
- Consumes: `ListChecklistItemsQuery`, `AttachChecklistItemCommand`, `DetachChecklistItemCommand`, `SetChecklistEntryCheckedCommand` (Tasks 5-8).
- Produces: `GET /api/checklist-items`, `POST/DELETE/PATCH /api/trips/{id}/places/{placeId}/checklist[/{entryId}]`.

> Controllers are thin `IMediator` delegations with no controller-level tests in this repo (behavior is covered at the Application layer, Tasks 5-8). Verify by build; the endpoints are exercised end-to-end during Task 14's interactive check.

- [ ] **Step 1: Add usings** at the top of `TripsController.cs` (with the other `MenuNest.Application.UseCases.Trips.*` usings):

```csharp
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
```

- [ ] **Step 2: Add the four actions** inside the `TripsController` class (near the other places actions):

```csharp
    [HttpGet("api/checklist-items")]
    public async Task<ActionResult<IReadOnlyList<ChecklistItemDto>>> ListChecklistItems(CancellationToken ct)
        => Ok(await _mediator.Send(new ListChecklistItemsQuery(), ct));

    [HttpPost("api/trips/{id:guid}/places/{placeId:guid}/checklist")]
    public async Task<ActionResult<PlaceChecklistEntryDto>> AttachChecklistItem(Guid id, Guid placeId, [FromBody] AttachChecklistBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AttachChecklistItemCommand(id, placeId, b.Name), ct));

    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}/checklist/{entryId:guid}")]
    public async Task<IActionResult> DetachChecklistItem(Guid id, Guid placeId, Guid entryId, CancellationToken ct)
    { await _mediator.Send(new DetachChecklistItemCommand(id, placeId, entryId), ct); return NoContent(); }

    [HttpPatch("api/trips/{id:guid}/places/{placeId:guid}/checklist/{entryId:guid}")]
    public async Task<ActionResult<PlaceChecklistEntryDto>> SetChecklistItemChecked(Guid id, Guid placeId, Guid entryId, [FromBody] SetChecklistCheckedBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new SetChecklistEntryCheckedCommand(id, placeId, entryId, b.IsChecked), ct));
```

- [ ] **Step 3: Add the body records** at the bottom of the file, beside `UpdatePlaceBody`/`AddPlaceBody`:

```csharp
public sealed record AttachChecklistBody(string Name);

public sealed record SetChecklistCheckedBody(bool IsChecked);
```

- [ ] **Step 4: Build + full suite**

Run: `cd backend && dotnet build && dotnet test`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/TripsController.cs
git commit -m "feat(trips): REST endpoints for place checklist (list/attach/detach/toggle) (#23)"
```

---

## Task 10: McpServer — 4 tools on `TripTools`

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs`

**Interfaces:**
- Consumes: the four use cases (Tasks 5-8).
- Produces: MCP tools `list_checklist_items`, `attach_checklist_item`, `detach_checklist_item`, `set_checklist_item_checked`. (Read of a place's checklist is automatic via the embedded `TripPlaceDto` from `list_trip_places` — no separate read tool, mirroring review links.)

- [ ] **Step 1: Add usings** at the top of `TripTools.cs`:

```csharp
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
```

- [ ] **Step 2: Add the four tools** inside `TripTools`:

```csharp
    [McpServerTool, Description("List the current user's reusable checklist items — the personal library of 'things to prepare/bring' reused across places and trips. Use these names with attach_checklist_item.")]
    public async Task<IReadOnlyList<ChecklistItemDto>> list_checklist_items(CancellationToken ct)
        => await mediator.Send(new ListChecklistItemsQuery(), ct);

    [McpServerTool, Description("Attach a checklist item to a place BY NAME. Create-or-reuse: an existing library item of the same name (case-insensitive) for this user is reused; a new name creates one. Idempotent per (place,item). Returns the place checklist entry.")]
    public async Task<PlaceChecklistEntryDto> attach_checklist_item(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Checklist item name, e.g. ร่ม / passport / sunscreen")] string name,
        CancellationToken ct)
        => await mediator.Send(new AttachChecklistItemCommand(tripId, placeId, name), ct);

    [McpServerTool, Description("Detach a checklist item from a place. Removes the place's entry ONLY; the reusable library item survives for other places.")]
    public async Task<bool> detach_checklist_item(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place checklist entry ID (from the place's checklist)")] Guid entryId,
        CancellationToken ct)
        => await mediator.Send(new DetachChecklistItemCommand(tripId, placeId, entryId), ct);

    [McpServerTool, Description("Set the per-place checked ('prepared') state of a place checklist entry. Checked is independent per place.")]
    public async Task<PlaceChecklistEntryDto> set_checklist_item_checked(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place checklist entry ID")] Guid entryId,
        [Description("true = prepared/checked, false = not yet")] bool isChecked,
        CancellationToken ct)
        => await mediator.Send(new SetChecklistEntryCheckedCommand(tripId, placeId, entryId, isChecked), ct);
```

- [ ] **Step 3: Build + full suite + commit**

Run: `cd backend && dotnet build && dotnet test` → green.
```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs
git commit -m "feat(trips): MCP tools for place checklist (list/attach/detach/toggle) (#23)"
```

---
## Task 11: Frontend — `lib/checklist.ts` pure logic (TDD)

**Files:**
- Create: `frontend/src/pages/trips/lib/checklist.ts`
- Test: `frontend/src/pages/trips/lib/checklist.test.ts`

**Interfaces:**
- Produces: `MAX_CHECKLIST_ITEMS_PER_PLACE=20`, `MAX_CHECKLIST_NAME=100`, `normalizeChecklistName(raw)`, `isValidChecklistName(raw)`, `matchLibrary<T extends {name:string}>(query, items)`, `exactMatch<T extends {name:string}>(query, items)`, `checklistProgress(entries: {isChecked:boolean}[])`. NO imports from `api.ts` (kept self-contained so it is testable before Task 12).

- [ ] **Step 1: Write the failing test**

`frontend/src/pages/trips/lib/checklist.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import {
  MAX_CHECKLIST_NAME,
  normalizeChecklistName,
  isValidChecklistName,
  matchLibrary,
  exactMatch,
  checklistProgress,
} from './checklist'

describe('checklist lib', () => {
  it('normalizes whitespace', () => {
    expect(normalizeChecklistName('  ร่ม  ')).toBe('ร่ม')
    expect(normalizeChecklistName('a\t b   c')).toBe('a b c')
  })

  it('validates name (non-empty, <= max)', () => {
    expect(isValidChecklistName('ร่ม')).toBe(true)
    expect(isValidChecklistName('   ')).toBe(false)
    expect(isValidChecklistName('x'.repeat(MAX_CHECKLIST_NAME))).toBe(true)
    expect(isValidChecklistName('x'.repeat(MAX_CHECKLIST_NAME + 1))).toBe(false)
  })

  it('matches library case-insensitively by substring; empty query returns all', () => {
    const items = [{name: 'Umbrella'}, {name: 'Passport'}]
    expect(matchLibrary('umb', items)).toEqual([{name: 'Umbrella'}])
    expect(matchLibrary('', items)).toHaveLength(2)
  })

  it('finds exact match case-insensitively', () => {
    const items = [{name: 'Umbrella'}]
    expect(exactMatch('umbrella', items)).toEqual({name: 'Umbrella'})
    expect(exactMatch('umb', items)).toBeNull()
  })

  it('computes checked/total progress', () => {
    expect(checklistProgress([{isChecked: true}, {isChecked: false}, {isChecked: true}])).toEqual({done: 2, total: 3})
    expect(checklistProgress([])).toEqual({done: 0, total: 0})
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/checklist.test.ts`
Expected: FAIL — module `./checklist` not found.

- [ ] **Step 3: Write the implementation**

`frontend/src/pages/trips/lib/checklist.ts`:

```ts
// Pure helpers for the Place checklist (issue #23). No api.ts import so this is
// unit-testable in isolation (the SPA vitest runs in node env, no jsdom).

export const MAX_CHECKLIST_ITEMS_PER_PLACE = 20
export const MAX_CHECKLIST_NAME = 100

export function normalizeChecklistName(raw: string): string {
  return raw.trim().replace(/\s+/g, ' ')
}

export function isValidChecklistName(raw: string): boolean {
  const n = normalizeChecklistName(raw)
  return n.length > 0 && n.length <= MAX_CHECKLIST_NAME
}

export function matchLibrary<T extends {name: string}>(query: string, items: T[]): T[] {
  const q = normalizeChecklistName(query).toLowerCase()
  if (q.length === 0) return items
  return items.filter((i) => i.name.toLowerCase().includes(q))
}

export function exactMatch<T extends {name: string}>(query: string, items: T[]): T | null {
  const q = normalizeChecklistName(query).toLowerCase()
  return items.find((i) => i.name.toLowerCase() === q) ?? null
}

export function checklistProgress(entries: {isChecked: boolean}[]): {done: number; total: number} {
  return {done: entries.filter((e) => e.isChecked).length, total: entries.length}
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/lib/checklist.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/checklist.ts frontend/src/pages/trips/lib/checklist.test.ts
git commit -m "feat(trips): add checklist lib pure helpers (#23)"
```

---

## Task 12: Frontend — RTK Query types, tag, endpoints, hooks

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Consumes: backend endpoints (Tasks 9-10).
- Produces: `ChecklistItem`, `PlaceChecklistEntry` types; `TripPlaceDto.checklist: PlaceChecklistEntry[]`; tag `'ChecklistItems'`; hooks `useListChecklistItemsQuery`, `useAttachChecklistItemMutation`, `useDetachChecklistItemMutation`, `useSetChecklistEntryCheckedMutation`.

> No unit test (RTK wiring; the SPA has no jsdom harness). Verified by `tsc`/build here and interactively in Task 14. Guard: after this task the app still builds because the new `checklist` field is optional-at-runtime (present once backend Tasks 3-10 are merged first).

- [ ] **Step 1: Add the two types + extend `TripPlaceDto`**

In the Trips type block, add after the `ReviewLink` interface:

```ts
export interface ChecklistItem {
    id: string
    name: string
}
export interface PlaceChecklistEntry {
    id: string
    checklistItemId: string
    name: string
    isChecked: boolean
}
```

Add `checklist` to `TripPlaceDto` (last field):

```ts
export interface TripPlaceDto {
    id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number
    address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null
    bestTimeStart: string | null; bestTimeEnd: string | null; openingHoursJson: string | null
    feeNote: string | null; notes: string | null
    reviewLinks: ReviewLink[]
    checklist: PlaceChecklistEntry[]
}
```

- [ ] **Step 2: Register the `ChecklistItems` tag**

In the `tagTypes` array, add after `'TripItinerary'`:

```ts
        'ChecklistItems',
```

- [ ] **Step 3: Add the four endpoints** (in the Trips endpoints block, e.g. after `updateTripPlace`)

```ts
        listChecklistItems: build.query<ChecklistItem[], void>({
            query: () => `/api/checklist-items`,
            providesTags: [{type: 'ChecklistItems', id: 'LIST'}],
        }),
        attachChecklistItem: build.mutation<PlaceChecklistEntry, {tripId: string; placeId: string; name: string}>({
            query: ({tripId, placeId, name}) => ({url: `/api/trips/${tripId}/places/${placeId}/checklist`, method: 'POST', body: {name}}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'ChecklistItems', id: 'LIST'}],
        }),
        detachChecklistItem: build.mutation<void, {tripId: string; placeId: string; entryId: string}>({
            query: ({tripId, placeId, entryId}) => ({url: `/api/trips/${tripId}/places/${placeId}/checklist/${entryId}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}],
        }),
        setChecklistEntryChecked: build.mutation<void, {tripId: string; placeId: string; entryId: string; isChecked: boolean}>({
            query: ({tripId, placeId, entryId, isChecked}) => ({
                url: `/api/trips/${tripId}/places/${placeId}/checklist/${entryId}`, method: 'PATCH', body: {isChecked},
            }),
            // Optimistic, NON-invalidating (ADR-042 / ADR-060). placesById is fed by listTripPlaces,
            // keyed by the plain tripId string — a single-entry patch (simpler than setStopVisited's fan-out).
            onQueryStarted: async ({tripId, placeId, entryId, isChecked}, {dispatch, queryFulfilled}) => {
                const patch = dispatch(
                    api.util.updateQueryData('listTripPlaces', tripId, (draft) => {
                        const place = draft.find((p) => p.id === placeId)
                        const entry = place?.checklist.find((e) => e.id === entryId)
                        if (entry) entry.isChecked = isChecked
                    }),
                )
                try {
                    await queryFulfilled
                } catch {
                    patch.undo()
                }
            },
        }),
```

- [ ] **Step 4: Export the four hooks** (in the `// -------- Trips --------` hook export block)

```ts
    useListChecklistItemsQuery,
    useAttachChecklistItemMutation,
    useDetachChecklistItemMutation,
    useSetChecklistEntryCheckedMutation,
```

- [ ] **Step 5: Typecheck + build + commit**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.
```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(trips): RTK Query endpoints + types for place checklist (#23)"
```

---

## Task 13: Frontend — `ChecklistIcon` inline SVG

**Files:**
- Create: `frontend/src/pages/trips/components/ChecklistIcon.tsx`

**Interfaces:**
- Produces: `ChecklistIcon()` — a clipboard-check SVG (currentColor, size from parent CSS), mirroring `ReviewIcon.tsx`.

- [ ] **Step 1: Create the component**

`frontend/src/pages/trips/components/ChecklistIcon.tsx`:

```tsx
// frontend/src/pages/trips/components/ChecklistIcon.tsx
// Clipboard-check glyph for the Place checklist section head ("สิ่งที่ต้องเตรียม").
// Inline SVG, never emoji (trips convention). Colour from currentColor, size from
// the parent CSS (.se-sec-head svg).
export function ChecklistIcon() {
  return (
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M9 4h6a1 1 0 0 1 1 1v1H8V5a1 1 0 0 1 1-1z" />
      <path d="M8 5H6a1 1 0 0 0-1 1v13a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-2" />
      <path d="M9 13l2 2 4-4" />
    </svg>
  )
}
```

- [ ] **Step 2: Typecheck + commit**

Run: `cd frontend && npx tsc --noEmit`
Expected: PASS.
```bash
git add frontend/src/pages/trips/components/ChecklistIcon.tsx
git commit -m "feat(trips): add ChecklistIcon inline SVG (#23)"
```

---
## Task 14: Frontend — checklist section in `StopEditorDialog` + CSS (interactive)

**Files:**
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`

**Interfaces:**
- Consumes: `lib/checklist.ts` (Task 11), the 4 hooks + `PlaceChecklistEntry` type (Task 12), `ChecklistIcon` (Task 13), `place.checklist` (embedded read, Task 4), `getErrorMessage`.
- Produces: the editable checklist section in the modal — entries list (checkbox toggle + remove), add input with library autocomplete + create-new. Toggle/attach/detach are immediate (NOT part of the "บันทึก" button), matching ADR-060.

> The SPA has NO jsdom/RTL — this section CANNOT be unit-tested. It MUST be verified interactively (Step 5). Match `docs/mocks/trip-place-checklist-mock.html`.

- [ ] **Step 1: Add imports** to `StopEditorDialog.tsx`

Add hooks to the existing `api` import:

```tsx
import {
  useUpdateStopMutation,
  useUpdateTripPlaceMutation,
  useRemoveStopMutation,
  useListChecklistItemsQuery,
  useAttachChecklistItemMutation,
  useDetachChecklistItemMutation,
  useSetChecklistEntryCheckedMutation,
  type ItineraryDayDto,
  type TripPlaceDto,
  type TravelMode,
} from '../../../shared/api/api'
```

Add the icon + lib imports (beside the existing `ReviewIcon` / `reviewLinks` imports):

```tsx
import {ChecklistIcon} from './ChecklistIcon'
import {
  MAX_CHECKLIST_ITEMS_PER_PLACE,
  isValidChecklistName,
  normalizeChecklistName,
  matchLibrary,
  exactMatch,
  checklistProgress,
} from '../lib/checklist'
```

- [ ] **Step 2: Add hooks + local state + handlers** (inside the component, near the other hooks/state)

```tsx
  const {data: library} = useListChecklistItemsQuery()
  const [attachChecklist] = useAttachChecklistItemMutation()
  const [detachChecklist] = useDetachChecklistItemMutation()
  const [setChecklistChecked] = useSetChecklistEntryCheckedMutation()
  const [ckDraft, setCkDraft] = useState('')
  const [ckError, setCkError] = useState<string | null>(null)

  const checklist = place?.checklist ?? []
  const progress = checklistProgress(checklist)
  const attachedItemIds = new Set(checklist.map((e) => e.checklistItemId))
  const suggestions = matchLibrary(ckDraft, library ?? []).filter((i) => !attachedItemIds.has(i.id))
  const showCreate = isValidChecklistName(ckDraft) && !exactMatch(ckDraft, library ?? [])

  const addChecklist = async (name: string) => {
    setCkError(null)
    if (!place) return
    if (!isValidChecklistName(name)) {
      setCkError('ชื่อไม่ถูกต้อง หรือยาวเกิน 100 ตัวอักษร')
      return
    }
    if (checklist.length >= MAX_CHECKLIST_ITEMS_PER_PLACE) {
      setCkError(`เพิ่มได้สูงสุด ${MAX_CHECKLIST_ITEMS_PER_PLACE} รายการ`)
      return
    }
    try {
      await attachChecklist({tripId, placeId: place.id, name: normalizeChecklistName(name)}).unwrap()
      setCkDraft('')
    } catch (err) {
      setCkError(getErrorMessage(err))
    }
  }

  const toggleChecklist = async (entryId: string, next: boolean) => {
    if (!place) return
    setCkError(null)
    try {
      await setChecklistChecked({tripId, placeId: place.id, entryId, isChecked: next}).unwrap()
    } catch (err) {
      setCkError(getErrorMessage(err))
    }
  }

  const removeChecklist = async (entryId: string) => {
    if (!place) return
    setCkError(null)
    try {
      await detachChecklist({tripId, placeId: place.id, entryId}).unwrap()
    } catch (err) {
      setCkError(getErrorMessage(err))
    }
  }
```

- [ ] **Step 3: Add the section JSX** — place it right after the review-links `</section>` (still inside `.stop-editor`)

```tsx
        {place && (
          <section className="se-sec">
            <div className="se-sec-head">
              <ChecklistIcon />สิ่งที่ต้องเตรียม
              {checklist.length > 0 && (
                <span className="se-ck-pill">เตรียมแล้ว {progress.done}/{progress.total}</span>
              )}
            </div>

            {checklist.length > 0 && (
              <div className="ck-card">
                {checklist.map((e) => (
                  <label className={e.isChecked ? 'ck-row done' : 'ck-row'} key={e.id}>
                    <input
                      type="checkbox"
                      checked={e.isChecked}
                      onChange={(ev) => toggleChecklist(e.id, ev.target.checked)}
                    />
                    <span className="ck-name">{e.name}</span>
                    <button
                      type="button"
                      className="ck-del"
                      aria-label="เอาออก"
                      onClick={(ev) => {
                        ev.preventDefault()
                        removeChecklist(e.id)
                      }}
                    >
                      <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" aria-hidden="true">
                        <path d="M6 6l12 12M18 6L6 18" />
                      </svg>
                    </button>
                  </label>
                ))}
              </div>
            )}

            <div className="ck-add-wrap">
              <div className="ck-add-in">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true">
                  <path d="M12 5v14M5 12h14" />
                </svg>
                <input
                  value={ckDraft}
                  placeholder="พิมพ์ของที่ต้องเตรียม…"
                  onChange={(ev) => setCkDraft(ev.target.value)}
                  onKeyDown={(ev) => {
                    if (ev.key === 'Enter') {
                      ev.preventDefault()
                      if (ckDraft.trim()) addChecklist(ckDraft)
                    }
                  }}
                />
              </div>
              {ckDraft.trim().length > 0 && (suggestions.length > 0 || showCreate) && (
                <div className="ck-ac">
                  {suggestions.length > 0 && <div className="ac-h">จากคลังของคุณ</div>}
                  {suggestions.map((i) => (
                    <button type="button" key={i.id} onClick={() => addChecklist(i.name)}>
                      <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true">
                        <path d="M3 12h18M3 6h18M3 18h18" />
                      </svg>
                      {i.name}
                      <span className="lib">ในคลัง</span>
                    </button>
                  ))}
                  {showCreate && (
                    <button type="button" className="create" onClick={() => addChecklist(ckDraft)}>
                      <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true">
                        <path d="M12 5v14M5 12h14" />
                      </svg>
                      สร้าง “{normalizeChecklistName(ckDraft)}” ใหม่
                    </button>
                  )}
                </div>
              )}
            </div>
            {ckError && <p className="trips-field-error">{ckError}</p>}
          </section>
        )}
```

- [ ] **Step 4: Add the CSS** to `TripDetailPage.css` (after the review-links section rules, near line ~654)

```css
/* Stop editor — place checklist section (issue #23, ADR-058..061) */
.stop-editor-dialog .se-sec-head .se-ck-pill { margin-left: auto; padding: 2px 10px; background: #e9f6ee; color: #1f7a45; border-radius: 999px; font-size: 11px; font-weight: 700; }
.stop-editor-dialog .ck-card { margin-top: 12px; border: 1.5px solid var(--se-border); border-radius: 14px; background: #fff; overflow: hidden; }
.stop-editor-dialog .ck-row { display: flex; align-items: center; gap: 11px; padding: 11px 13px; border-bottom: 1px solid var(--se-border); margin: 0; cursor: pointer; }
.stop-editor-dialog .ck-row:last-child { border-bottom: 0; }
.stop-editor-dialog .ck-row input[type='checkbox'] { width: 20px; height: 20px; flex: none; accent-color: #2e9e57; cursor: pointer; margin: 0; }
.stop-editor-dialog .ck-name { flex: 1; min-width: 0; font-size: 14.5px; font-weight: 600; color: var(--se-ink); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.stop-editor-dialog .ck-row.done { background: #e9f6ee; }
.stop-editor-dialog .ck-row.done .ck-name { color: #7d8a80; text-decoration: line-through; text-decoration-color: #bcd6c5; font-weight: 500; }
.stop-editor-dialog .ck-del { flex: none; width: 30px; height: 30px; border: 0; background: transparent; color: var(--se-muted); border-radius: 8px; cursor: pointer; display: flex; align-items: center; justify-content: center; }
.stop-editor-dialog .ck-del:hover { background: #fbeae3; color: #b4543a; }
.stop-editor-dialog .ck-add-wrap { position: relative; margin-top: 11px; }
.stop-editor-dialog .ck-add-in { display: flex; align-items: center; gap: 9px; border: 1.5px dashed var(--se-accent-line); background: var(--se-accent-soft); border-radius: 13px; padding: 11px 14px; }
.stop-editor-dialog .ck-add-in svg { color: var(--se-accent); flex: none; }
.stop-editor-dialog .ck-add-in input { flex: 1; border: 0; background: transparent; font: inherit; font-size: 14px; color: var(--se-ink); outline: none; }
.stop-editor-dialog .ck-add-in input::placeholder { color: #c2a789; }
.stop-editor-dialog .ck-ac { position: absolute; left: 0; right: 0; top: calc(100% + 6px); background: #fff; border: 1px solid var(--se-border); border-radius: 13px; box-shadow: 0 14px 34px rgba(26, 20, 14, 0.16); padding: 6px; z-index: 5; }
.stop-editor-dialog .ck-ac .ac-h { font-size: 10px; font-weight: 800; color: var(--se-muted); text-transform: uppercase; letter-spacing: 0.05em; margin: 4px 8px 6px; }
.stop-editor-dialog .ck-ac button { display: flex; align-items: center; gap: 9px; width: 100%; border: 0; background: transparent; text-align: left; font: inherit; font-size: 13.5px; font-weight: 600; color: var(--se-ink); padding: 9px 9px; border-radius: 9px; cursor: pointer; }
.stop-editor-dialog .ck-ac button:hover { background: var(--se-accent-soft); }
.stop-editor-dialog .ck-ac button .lib { margin-left: auto; font-size: 10px; font-weight: 600; color: #3f9d54; background: #e9f6ee; padding: 2px 7px; border-radius: 999px; }
.stop-editor-dialog .ck-ac button svg { color: var(--se-muted); flex: none; }
.stop-editor-dialog .ck-ac .create { color: var(--se-accent-deep); font-weight: 700; }
.stop-editor-dialog .ck-ac .create svg { color: var(--se-accent); }
```

- [ ] **Step 5: Typecheck + build, then VERIFY INTERACTIVELY**

Run: `cd frontend && npx tsc --noEmit && npm run build` → PASS.

Then run the app and exercise the real flow (no jsdom = no automated cover):
1. Open a trip → Itinerary → tap a Stop to open the editor modal.
2. In "สิ่งที่ต้องเตรียม": type a new name → the "สร้าง …" row appears → click it → the entry appears with an unchecked box; the pill shows "เตรียมแล้ว 0/1".
3. Type part of an existing library item → it appears under "จากคลังของคุณ" → click → attaches (no duplicate library item).
4. Tick the checkbox → row goes green + strike-through, pill increments; reopen the modal → state persisted (optimistic patch held, no full-day refetch/re-bill).
5. Remove (✕) an entry → it disappears; confirm via list refetch it is gone but the library still offers it in autocomplete.
6. Attach the same item to a SECOND place → tick it there → confirm the first place's tick is unaffected (per-place independence).
7. Compare against `docs/mocks/trip-place-checklist-mock.html`.

If a seeded/authenticated environment is unavailable locally, verify against the deployed app after the backend migration is applied, and at minimum confirm `tsc`/`build`/mock parity before sign-off.

- [ ] **Step 6: Full suite + commit (closes the issue)**

Run: `cd frontend && npx tsc --noEmit && npm run build && npx vitest run` and `cd backend && dotnet test` → all green.
```bash
git add frontend/src/pages/trips/components/StopEditorDialog.tsx frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): place checklist section in the stop editor modal (closes #23)"
```

---

## Self-Review (completed while writing)

**1. Spec coverage** — every spec section maps to a task:
- Domain `ChecklistItem` + `PlaceChecklistEntry` → Tasks 1-3. Cascade/lifecycle (§3.3) → Task 3 configs. Create-or-reuse (§4) → Task 6. API REST+MCP (§5) → Tasks 9-10. Read embed in `TripPlaceDto` (§6) → Task 4. Frontend types/endpoints/optimistic toggle (§7.1-7.2) → Task 12; modal section (§7.3) → Task 14; lib (§7.4) → Task 11; icon → Task 13. Validation/bounds (§8) → domain (T1), validator (T6), lib (T11), DB indexes (T3). Testing/rollout (§10) → relational tests throughout + T14 interactive + T3 migration note.
- Out-of-scope (card badge, library-management/rename/delete, reorder) correctly has NO task (ADR-061).

**2. Placeholder scan** — no TBD/"handle appropriately"/"similar to Task N"; every code step shows complete code; commands have expected output.

**3. Type consistency** — verified names/signatures across tasks: `ChecklistItem.Create(Guid,string)`, `PlaceChecklistEntry.Create(Guid,Guid)` + `SetChecked(bool)`; `PlaceChecklistEntryDto(Guid Id, Guid ChecklistItemId, string Name, bool IsChecked)`; `ChecklistItemDto(Guid Id, string Name)`; `ToDto` overload pair; commands/queries `ICommand<T>`/`IQuery<T>` with `GetOrProvisionCurrentAsync`; DbSets `ChecklistItems`/`PlaceChecklistEntries` added to interface + `AppDbContext` + `SqliteAppDbContext`; frontend `PlaceChecklistEntry{id,checklistItemId,name,isChecked}` + `TripPlaceDto.checklist`; optimistic patch targets `listTripPlaces` (matches how `placesById` is fed). REST/MCP names align across Tasks 9/10/12.

**Watch-outs for the executor:**
- Confirm the test project's mocking lib (NSubstitute assumed; switch to Moq if that is what the `Trips` tests use).
- Task 4 changes the positional `TripPlaceDto` — let the compiler (Step 7) surface any construction site the empty `ToDto` overload didn't cover.
- Backend Tasks 1-10 must merge before the frontend consumes `place.checklist` at runtime; within the shared suite everything stays green regardless of order because tsc only needs the type (Task 12) and unit tests never hit the live API.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-13-place-checklist-items.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task with two-stage review between tasks; fast iteration and isolation.
2. **Inline Execution** — execute tasks in this session via executing-plans, batched with checkpoints.

Which approach?