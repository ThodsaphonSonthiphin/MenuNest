# Place Season (issue #19) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each Place a list of good/avoid month-periods (with reasons), editable in-app and over MCP, that warns when a Stop is scheduled in an avoid month.

**Architecture:** Season is a per-Place ordered JSON value-list of `SeasonPeriod {kind, months, note}` on both `TripPlace` (per-trip) and `PlaceProfile` (user-scoped master), cloning the shipped **Review-links** feature end to end (value object → entity field + full-replace setter → EF JSON column + `ValueComparer` → DTO → MCP full-replace). It rides the existing `PlaceProfileSync` seed/override/push/auto-create lifecycle. The frontend resolves it via a pure `lib/season.ts` (`monthStatus`, avoid-wins) and shows a `WeatherDiorama` + status row on the stop card and a year-ribbon editor.

**Tech Stack:** .NET 10 / EF Core (SQL Server; SQLite + InMemory in tests) · Mediator + FluentValidation · xUnit + Moq + FluentAssertions · React + TypeScript + RTK Query + Syncfusion · Vitest (node env) · Vite.

## Global Constraints

- **Months are 0-indexed everywhere** (0 = January … 11 = December) — DTO, `lib/season.ts`, entity validation, `monthOfDate`. An off-by-one silently mis-warns.
- **`SeasonPeriod` = `{ kind: SeasonKind, months: int[] (0..11), note?: string }`** — a JSON-embedded value object (record with a **public** ctor for STJ round-trip; validation only in `Create`). **Not** a table/DbSet. Cap **12** periods per list; note max length **200**.
- **`SeasonKind` is a C# enum `{ Good, Bad }`** — serialized as the string `"Good"`/`"Bad"` in API responses (same convention as `PlaceCategory`); frontend union type is `'Good' | 'Bad'`. UI labels: `Good` → **ควรไป** (green), `Bad` → **ควรเลี่ยง** (red). The handoff's lowercase `good|bad` is superseded by this codebase convention.
- **Never reuse "best time"** for season (it is the time-of-day window). UI copy in Thai.
- **One backend commit rule:** entity + EF config + `InMemoryAppDbContext` mirror + migration + DTO + every positional-caller fix + tests must land in the **same** commit — the pre-commit hook (`frontend/.husky/pre-commit`, `set -e`) runs the FULL Release suite (backend `dotnet build`+`dotnet test`, frontend `tsc --noEmit`+`npm run build`) on every commit, and an unmapped collection property fails EF model validation for every DbContext test.
- **Migration applied to prod BY HAND** post-merge (`AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update …`, temp SQL firewall rule) — app/CD never migrate.
- **No frontend component/visual test harness** — Vitest is node-env. Pure logic goes in `lib/season.ts` (unit-tested); the editor, ribbon, diorama, and card layout are verified **interactively before push** (prod deploys on push to `main`; the #36 black-screen lesson).
- **Icons:** new season UI uses `@syncfusion/react-icons` SVG or plain text — not emoji glyphs (project rule; the existing `ReviewLinksSection`/`StopEditorDialog` emoji are pre-existing and not a precedent to copy).
- **Git remote is `main`** (not `origin`); commit subjects reference `#19` (`(#19)` — relates, not closes, until the feature is complete).

---

### Task 1: SeasonPeriod value object + SeasonKind enum

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/SeasonKind.cs`
- Create: `backend/src/MenuNest.Domain/ValueObjects/SeasonPeriod.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/SeasonPeriodTests.cs`

**Interfaces:**
- Produces: `enum SeasonKind { Good, Bad }`; `sealed record SeasonPeriod(SeasonKind Kind, IReadOnlyList<int> Months, string? Note)` with `static SeasonPeriod Create(SeasonKind kind, IEnumerable<int>? months, string? note)`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/SeasonPeriodTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class SeasonPeriodTests
{
    [Fact]
    public void Create_dedupes_and_sorts_months_and_trims_note()
    {
        var p = SeasonPeriod.Create(SeasonKind.Bad, new[] { 9, 5, 9, 6 }, "  น้ำท่วม  ");
        p.Kind.Should().Be(SeasonKind.Bad);
        p.Months.Should().Equal(5, 6, 9);
        p.Note.Should().Be("น้ำท่วม");
    }

    [Fact]
    public void Create_blank_note_becomes_null()
    {
        SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, "   ").Note.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void Create_rejects_out_of_range_month(int bad)
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Good, new[] { bad }, null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_empty_months()
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Bad, System.Array.Empty<int>(), "x");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_too_long_note()
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, new string('x', 201));
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~SeasonPeriodTests"`
Expected: FAIL to compile — `SeasonKind`/`SeasonPeriod` do not exist.

- [ ] **Step 3: Create the enum**

`backend/src/MenuNest.Domain/Enums/SeasonKind.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>Whether a <see cref="ValueObjects.SeasonPeriod"/> marks months good to visit or to avoid.</summary>
public enum SeasonKind { Good, Bad }
```

- [ ] **Step 4: Create the value object**

`backend/src/MenuNest.Domain/ValueObjects/SeasonPeriod.cs` (modelled on `ReviewLink.cs`):

```csharp
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place season period (issue #19): a set of calendar months (0..11) marked good-to-visit
/// or to-avoid, with an optional reason. Positional record with a public ctor so System.Text.Json
/// can round-trip it from the JSON column; user input is validated through <see cref="Create"/>.
/// </summary>
public sealed record SeasonPeriod(SeasonKind Kind, IReadOnlyList<int> Months, string? Note)
{
    public static SeasonPeriod Create(SeasonKind kind, IEnumerable<int>? months, string? note)
    {
        var m = (months ?? Enumerable.Empty<int>())
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (m.Count == 0) throw new DomainException("A season period must include at least one month.");
        if (m.Any(x => x is < 0 or > 11)) throw new DomainException("Season months must be 0–11 (Jan–Dec).");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 200 }) throw new DomainException("Season note is too long (max 200).");
        return new SeasonPeriod(kind, m, n);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~SeasonPeriodTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/SeasonKind.cs \
        backend/src/MenuNest.Domain/ValueObjects/SeasonPeriod.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Domain/SeasonPeriodTests.cs
git commit -m "feat(trips): SeasonPeriod value object + SeasonKind enum (#19)"
```

---

### Task 2: Persist SeasonPeriods on TripPlace + PlaceProfile (entities, EF config, InMemory mirror, migration)

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/TripPlace.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Create: migration `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<ts>_AddSeasonPeriods.cs` (+ Designer) and regenerated `AppDbContextModelSnapshot.cs` (via `dotnet ef`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceSeasonTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceSeasonRelationalTests.cs`

**Interfaces:**
- Consumes: `SeasonPeriod`, `SeasonKind` (Task 1).
- Produces: `TripPlace.SeasonPeriods` / `PlaceProfile.SeasonPeriods` (`IReadOnlyList<SeasonPeriod>`), `TripPlace.SetSeasonPeriods(IEnumerable<SeasonPeriod>)`, `PlaceProfile.SetSeasonPeriods(IEnumerable<SeasonPeriod>)`; a `SeasonPeriodsJson` `nvarchar(max)` column on `TripPlaces` and `PlaceProfiles`.

- [ ] **Step 1: Write the failing domain test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceSeasonTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripPlaceSeasonTests
{
    private static TripPlace NewPlace() =>
        TripPlace.Create(System.Guid.NewGuid(), "3000 โบก", 15.4, 105.4, PlaceCategory.See);

    [Fact]
    public void SetSeasonPeriods_replaces_the_whole_list()
    {
        var p = NewPlace();
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6 }, "ฝน") });
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Good, new[] { 10, 11 }, "เย็น") });
        p.SeasonPeriods.Should().HaveCount(1);
        p.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Good);
    }

    [Fact]
    public void SetSeasonPeriods_empty_clears()
    {
        var p = NewPlace();
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5 }, null) });
        p.SetSeasonPeriods(System.Array.Empty<SeasonPeriod>());
        p.SeasonPeriods.Should().BeEmpty();
    }

    [Fact]
    public void SetSeasonPeriods_rejects_over_cap()
    {
        var p = NewPlace();
        var many = Enumerable.Range(0, 13)
            .Select(_ => SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, null));
        var act = () => p.SetSeasonPeriods(many);
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~TripPlaceSeasonTests"`
Expected: FAIL to compile — `SetSeasonPeriods`/`SeasonPeriods` do not exist.

- [ ] **Step 3: Add the field + setter to `TripPlace`**

In `backend/src/MenuNest.Domain/Entities/TripPlace.cs`, after the `_reviewLinks` block (line 30-31) add:

```csharp
    private readonly List<SeasonPeriod> _seasonPeriods = new();
    public IReadOnlyList<SeasonPeriod> SeasonPeriods => _seasonPeriods;
```

and after `SetReviewLinks` (line 86) add:

```csharp
    public void SetSeasonPeriods(IEnumerable<SeasonPeriod> periods)
    {
        var list = (periods ?? Enumerable.Empty<SeasonPeriod>()).ToList();
        if (list.Count > 12) throw new DomainException("A place can have at most 12 season periods.");
        _seasonPeriods.Clear();
        _seasonPeriods.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Add the same to `PlaceProfile`**

In `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs`, after the `_reviewLinks` block (line 20-21) add:

```csharp
    private readonly List<SeasonPeriod> _seasonPeriods = new();
    public IReadOnlyList<SeasonPeriod> SeasonPeriods => _seasonPeriods;
```

and after `SetReviewLinks` (line 48) add:

```csharp
    public void SetSeasonPeriods(IEnumerable<SeasonPeriod> periods)
    {
        var list = (periods ?? Enumerable.Empty<SeasonPeriod>()).ToList();
        if (list.Count > 12) throw new DomainException("A place profile can have at most 12 season periods.");
        _seasonPeriods.Clear();
        _seasonPeriods.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
```

(Both entities already `using MenuNest.Domain.ValueObjects;`. Add `using MenuNest.Domain.Enums;` to `PlaceProfile.cs` only if not present — `SeasonPeriod` needs no extra using beyond ValueObjects.)

- [ ] **Step 5: Run the domain test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~TripPlaceSeasonTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Configure the EF JSON column on both entities**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs`, after the `ReviewLinks` block (line 36-42) add:

```csharp
        var seasonConverter = new ValueConverter<IReadOnlyList<SeasonPeriod>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => string.IsNullOrEmpty(v)
                ? new List<SeasonPeriod>()
                : JsonSerializer.Deserialize<List<SeasonPeriod>>(v, jsonOpts) ?? new List<SeasonPeriod>());
        var seasonComparer = new ValueComparer<IReadOnlyList<SeasonPeriod>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(b, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<SeasonPeriod>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        b.Property(p => p.SeasonPeriods)
            .HasConversion(seasonConverter, seasonComparer)
            .HasColumnName("SeasonPeriodsJson")
            .HasColumnType("nvarchar(max)")
            .HasField("_seasonPeriods")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValueSql("'[]'");
```

Add `using MenuNest.Domain.ValueObjects;` if not already present (it is). Do the identical block in `PlaceProfileConfiguration.cs` after its ReviewLinks block (use its local `jsonOpts` variable name — match whatever that file calls it; it mirrors `TripPlaceConfiguration`).

- [ ] **Step 7: Mirror the conversion in `InMemoryAppDbContext`**

In `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`, after the `PlaceProfile.ReviewLinks` mirror (ends line 171) add:

```csharp
        // Mirror the JSON-list conversion for SeasonPeriods (TripPlace + PlaceProfile).
        var seasonComparer = new ValueComparer<IReadOnlyList<SeasonPeriod>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<TripPlace>()
            .Property(p => p.SeasonPeriods)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<SeasonPeriod>)(JsonSerializer.Deserialize<List<SeasonPeriod>>(v, jsonOptions) ?? new List<SeasonPeriod>()),
                seasonComparer)
            .HasField("_seasonPeriods")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        modelBuilder.Entity<PlaceProfile>()
            .Property(p => p.SeasonPeriods)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<SeasonPeriod>)(JsonSerializer.Deserialize<List<SeasonPeriod>>(v, jsonOptions) ?? new List<SeasonPeriod>()),
                seasonComparer)
            .HasField("_seasonPeriods")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);
```

`MenuNest.Domain.ValueObjects` is already imported in that file (for `ReviewLink`). Note `SeasonPeriod`'s record equality does a **reference** compare on the `Months` list, so two equal-by-value periods with distinct `Months` instances compare unequal — that is acceptable for the InMemory comparer (it is only a change-tracking hint); the authoritative comparer in the real config compares serialized JSON.

- [ ] **Step 8: Write the relational round-trip test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceSeasonRelationalTests.cs` (uses `SqliteAppDbContext`, which applies the real config — follow the pattern of the existing `TripPlaceReviewLinks*RelationalTests`; if the helper for building a Sqlite context differs, copy that file's setup verbatim):

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class TripPlaceSeasonRelationalTests
{
    [Fact]
    public async Task SeasonPeriods_round_trip_through_the_json_column()
    {
        await using var ctx = SqliteAppDbContext.CreateOpen(); // match the actual factory used by sibling relational tests
        var trip = Trip.Create(System.Guid.NewGuid(), "T", null, new System.DateOnly(2026, 7, 1), 1, TravelMode.Drive);
        ctx.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        place.SetSeasonPeriods(new[]
        {
            SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6, 7, 8, 9 }, "น้ำท่วมหน้าฝน"),
            SeasonPeriod.Create(SeasonKind.Good, new[] { 10, 11, 0, 1 }, "อากาศเย็น"),
        });
        ctx.TripPlaces.Add(place);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var loaded = await ctx.TripPlaces.SingleAsync(p => p.Id == place.Id);
        loaded.SeasonPeriods.Should().HaveCount(2);
        loaded.SeasonPeriods[0].Note.Should().Be("น้ำท่วมหน้าฝน");
        loaded.SeasonPeriods[0].Months.Should().Equal(5, 6, 7, 8, 9);
        loaded.SeasonPeriods[1].Kind.Should().Be(SeasonKind.Good);
    }
}
```

> If `SqliteAppDbContext` exposes a different construction helper (e.g. a fixture or `new SqliteAppDbContext(options)` with an open connection), open `TripPlaceReviewLinksRelationalTests.cs` and copy its exact setup — do not invent `CreateOpen`.

- [ ] **Step 9: Generate the migration**

Run (from `backend/`):

```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations add AddSeasonPeriods \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Verify the generated `Up()` adds `SeasonPeriodsJson` (`nvarchar(max)`, `nullable: false`, `defaultValueSql: "'[]'"`) to **both** `TripPlaces` and `PlaceProfiles`, and that `AppDbContextModelSnapshot.cs` changed. If `dotnet ef` cannot reach a design-time DB it still scaffolds from the model — no live DB needed for `migrations add`.

- [ ] **Step 10: Run the full backend suite**

Run: `cd backend && dotnet build --configuration Release && dotnet test --configuration Release`
Expected: PASS — all existing tests plus the new domain + relational tests; zero errors.

- [ ] **Step 11: Commit (single coherent backend-persistence commit)**

```bash
git add backend/src/MenuNest.Domain/Entities/TripPlace.cs \
        backend/src/MenuNest.Domain/Entities/PlaceProfile.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
        backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceSeasonTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceSeasonRelationalTests.cs
git commit -m "feat(trips): persist SeasonPeriods JSON on TripPlace + PlaceProfile (#19)"
```

> **Prod migration (do NOT skip, but not part of this commit):** after the feature merges, apply by hand per CLAUDE.md — preview with `dotnet ef migrations script --idempotent`, add a temp SQL firewall rule for your IP, run `AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update … --connection "Server=tcp:menunest-sql.database.windows.net,…"`, remove the rule.

---

### Task 3: Expose SeasonPeriods on TripPlaceDto (read side)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs:43-47`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs` (extend)

**Interfaces:**
- Produces: `sealed record SeasonPeriodDto(SeasonKind Kind, IReadOnlyList<int> Months, string? Note)`; `TripPlaceDto.SeasonPeriods` (`IReadOnlyList<SeasonPeriodDto>`), populated by `ToDto`.

- [ ] **Step 1: Add the DTO record + field**

In `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`, after `ReviewLinkDto` (line 9) add:

```csharp
public sealed record SeasonPeriodDto(SeasonKind Kind, IReadOnlyList<int> Months, string? Note);
```

and append `SeasonPeriods` as the **last** member of the `TripPlaceDto` record (after `HasProfile`):

```csharp
public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<PlaceChecklistEntryDto> Checklist,
    bool HasProfile,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods);
```

(`SeasonKind` is in `MenuNest.Domain.Enums`, already imported at the top of `TripDtos.cs`.)

- [ ] **Step 2: Populate it in `ToDto`**

In `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs`, extend the `new(...)` in `ToDto` (lines 43-47) to append the season list as the last argument:

```csharp
    internal static TripPlaceDto ToDto(TripPlace p, IReadOnlyList<PlaceChecklistEntryDto> checklist, bool hasProfile = false) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.BestTimeStart, p.BestTimeEnd, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        checklist, hasProfile,
        p.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList());
```

The single-arg `ToDto(TripPlace p)` overload (line 41) delegates and needs no change. `TripPlaceDto` has exactly one construction site — no other producer breaks.

- [ ] **Step 3: Extend the ToDto/DTO test**

In `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs`, add an assertion (in the existing "adds a place" test, or a new one) that a captured place with no season returns `SeasonPeriods` empty:

```csharp
result.SeasonPeriods.Should().BeEmpty();
```

- [ ] **Step 4: Build + test**

Run: `cd backend && dotnet build --configuration Release && dotnet test --configuration Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs
git commit -m "feat(trips): add SeasonPeriods to TripPlaceDto read model (#19)"
```

---

### Task 4: Carry season through the master lifecycle (PlaceProfileSync)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs:23-24, 63-64`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeasonLifecycleTests.cs`

**Interfaces:**
- Consumes: `TripPlace.SeasonPeriods` / `PlaceProfile.SeasonPeriods` + setters (Task 2).
- Produces: season copied on seed (`SeedIntoAsync`) and on upsert (`UpsertFromAsync`, which both push-to-master and first-enrichment auto-create call).

- [ ] **Step 1: Write the failing lifecycle test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeasonLifecycleTests.cs` (mirror the setup of the existing `PlaceProfileAutoCreateRelationalTests` / `PlaceProfileSeedRelationalTests` — copy their context factory + user provisioning verbatim):

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class PlaceProfileSeasonLifecycleTests
{
    [Fact]
    public async Task Upsert_copies_season_into_master_then_seed_copies_it_back()
    {
        await using var ctx = SqliteAppDbContext.CreateOpen(); // match sibling tests' factory
        var userId = System.Guid.NewGuid();

        var t1 = Trip.Create(userId, "T1", null, new System.DateOnly(2026, 7, 1), 1, TravelMode.Drive);
        ctx.Trips.Add(t1);
        var p1 = TripPlace.Create(t1.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        p1.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6 }, "ฝน") });
        ctx.TripPlaces.Add(p1);
        await ctx.SaveChangesAsync();

        await PlaceProfileSync.UpsertFromAsync(ctx, userId, p1, default); // push-to-master
        await ctx.SaveChangesAsync();
        var master = await ctx.PlaceProfiles.SingleAsync(p => p.UserId == userId && p.GooglePlaceId == "gp1");
        master.SeasonPeriods.Should().HaveCount(1);
        master.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Bad);

        var t2 = Trip.Create(userId, "T2", null, new System.DateOnly(2026, 8, 1), 1, TravelMode.Drive);
        ctx.Trips.Add(t2);
        var p2 = TripPlace.Create(t2.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        ctx.TripPlaces.Add(p2);
        var seeded = await PlaceProfileSync.SeedIntoAsync(ctx, userId, p2, default); // capture seed
        seeded.Should().BeTrue();
        p2.SeasonPeriods.Should().HaveCount(1);
        p2.SeasonPeriods[0].Note.Should().Be("ฝน");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~PlaceProfileSeasonLifecycleTests"`
Expected: FAIL — master/seeded place has 0 season periods (not yet copied).

- [ ] **Step 3: Copy season in `SeedIntoAsync`**

In `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`, in `SeedIntoAsync` after `place.SetReviewLinks(profile.ReviewLinks);` (line 24) add:

```csharp
        place.SetSeasonPeriods(profile.SeasonPeriods);
```

- [ ] **Step 4: Copy season in `UpsertFromAsync`**

In the same file, in `UpsertFromAsync` after `profile.SetReviewLinks(place.ReviewLinks);` (line 64) add:

```csharp
        profile.SetSeasonPeriods(place.SeasonPeriods);
```

(No predicate change needed: `EnsureCreatedAsync` gates only on place_id + no-existing-profile, so a season-only Save mints the master automatically once `UpsertFromAsync` copies season.)

- [ ] **Step 5: Run the lifecycle test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~PlaceProfileSeasonLifecycleTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileSeasonLifecycleTests.cs
git commit -m "feat(trips): carry SeasonPeriods through seed/override/push lifecycle (#19)"
```

---

### Task 5: Write path — update_trip_place full-replace (command, validator, handler, controller, MCP) + fix all positional callers

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs:30`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:75,147-150`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:96-110`
- Modify tests (positional callers): `UpdateTripPlaceHandlerTests.cs`, `PushPlaceProfileHandlerTests.cs`, `PlaceProfileAutoCreateRelationalTests.cs` (every `new UpdateTripPlaceCommand(...)`)
- Test: add an override + full-replace-clears case to `UpdateTripPlaceHandlerTests.cs`

**Interfaces:**
- Consumes: `SeasonPeriodDto` (Task 3), `SeasonPeriod.Create` (Task 1), `TripPlace.SetSeasonPeriods` (Task 2).
- Produces: `UpdateTripPlaceCommand.SeasonPeriods` (last positional member); the HTTP PUT + MCP `update_trip_place` accept `seasonPeriods` as a full-replace list.

- [ ] **Step 1: Write the failing handler test**

In `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripPlaceHandlerTests.cs`, add:

```csharp
[Fact]
public async Task Update_replaces_season_periods()
{
    // Arrange a trip + place via the existing test harness in this file, capture its id, then:
    var cmd = Cmd(tripId, placeId) with
    {
        SeasonPeriods = new[] { new SeasonPeriodDto(SeasonKind.Bad, new[] { 5, 6 }, "ฝน") }
    };
    var dto = await handler.Handle(cmd, default);
    dto.SeasonPeriods.Should().ContainSingle().Which.Kind.Should().Be(SeasonKind.Bad);

    var cleared = Cmd(tripId, placeId) with { SeasonPeriods = System.Array.Empty<SeasonPeriodDto>() };
    (await handler.Handle(cleared, default)).SeasonPeriods.Should().BeEmpty();
}
```

> Use this file's existing `Cmd(...)` builder helper. After Step 3 it must default `SeasonPeriods` to an empty array so all other tests keep compiling — see Step 5.

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateTripPlaceHandlerTests"`
Expected: FAIL to compile — `UpdateTripPlaceCommand` has no `SeasonPeriods`.

- [ ] **Step 3: Add `SeasonPeriods` to the command**

`backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed record UpdateTripPlaceCommand(
    Guid TripId, Guid PlaceId, string Name, PlaceCategory Category,
    string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods)
    : ICommand<TripPlaceDto>;
```

- [ ] **Step 4: Validate + apply it**

In `UpdateTripPlaceValidator.cs`, after the `ReviewLinks` rules (line 19) add:

```csharp
        RuleFor(x => x.SeasonPeriods).NotNull()
            .WithMessage("Season periods are required (send an empty array for none).");
        RuleFor(x => x.SeasonPeriods).Must(l => l is null || l.Count <= 12)
            .WithMessage("A place can have at most 12 season periods.");
        RuleForEach(x => x.SeasonPeriods).ChildRules(sp =>
        {
            sp.RuleFor(s => s.Months).NotEmpty().WithMessage("A season period needs at least one month.");
            sp.RuleForEach(s => s.Months).InclusiveBetween(0, 11);
            sp.RuleFor(s => s.Note).MaximumLength(200);
        });
```

In `UpdateTripPlaceHandler.cs`, after `place.SetReviewLinks(...)` (line 30) add (the `using MenuNest.Domain.ValueObjects;` is already present):

```csharp
        place.SetSeasonPeriods((c.SeasonPeriods ?? Enumerable.Empty<SeasonPeriodDto>())
            .Select(s => SeasonPeriod.Create(s.Kind, s.Months, s.Note)));
```

- [ ] **Step 5: Fix every positional `new UpdateTripPlaceCommand(...)` caller**

Append the new trailing arg to each construction site:
- `TripsController.cs:75` — pass `b.SeasonPeriods` (added to the body in Step 6).
- `TripTools.cs:109-110` — pass `seasonPeriods` (added to the tool signature in Step 7).
- Test builders/sites: the `Cmd(...)` helper in `UpdateTripPlaceHandlerTests.cs`, and each literal `new UpdateTripPlaceCommand(...)` in `PushPlaceProfileHandlerTests.cs` and `PlaceProfileAutoCreateRelationalTests.cs` — add a trailing `Array.Empty<SeasonPeriodDto>()` (or the `Cmd` helper defaults it). Grep to find them all:

Run: `cd backend && grep -rn "new UpdateTripPlaceCommand(" .`
Expected: fix each hit (2 source + ~5 test) so the trailing `SeasonPeriods` argument is supplied.

- [ ] **Step 6: Add `SeasonPeriods` to the HTTP body**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, extend `UpdatePlaceBody` (line 147-150):

```csharp
public sealed record UpdatePlaceBody(
    string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods);
```

and pass it into the command at line 75:

```csharp
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeStart, b.BestTimeEnd, b.ReviewLinks, b.SeasonPeriods), ct));
```

(`SeasonPeriodDto` lives in `MenuNest.Application.UseCases.Trips`, already in scope here.)

- [ ] **Step 7: Add `seasonPeriods` to the MCP `update_trip_place` tool**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, add a parameter to `update_trip_place` (after `reviewLinks`, line 107) and pass it into the command (line 109-110). Update the tool `[Description]` to include season as a full-replace field:

```csharp
        [Description("Season periods, each { kind: 'Good'|'Bad', months: int[] 0-11 (0=Jan), note?: string }; max 12; FULL REPLACE — omitting or passing an empty array CLEARS all seasons.")] IReadOnlyList<SeasonPeriodDto> seasonPeriods,
```

and in the `new UpdateTripPlaceCommand(...)` call append `seasonPeriods` as the final argument.

- [ ] **Step 8: Full backend build + test**

Run: `cd backend && dotnet build --configuration Release && dotnet test --configuration Release`
Expected: PASS — including the new override/clear test; no CS7036 (all positional callers fixed).

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/ \
        backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripPlaceHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/PushPlaceProfileHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfileAutoCreateRelationalTests.cs
git commit -m "feat(trips): seasonPeriods full-replace on update_trip_place (HTTP + MCP) (#19)"
```

---

### Task 6: push_place_profile MCP tool

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (add tool + `using`)
- Test: `backend/tests/MenuNest.McpServer.UnitTests/` (add a tool test mirroring an existing TripTools test)

**Interfaces:**
- Consumes: the existing `PushPlaceProfileCommand(Guid TripId, Guid PlaceId)` (returns `TripPlaceDto`).
- Produces: MCP tool `push_place_profile(tripId, placeId) → TripPlaceDto`.

- [ ] **Step 1: Write the failing tool test**

In `backend/tests/MenuNest.McpServer.UnitTests/` add a test (mirror an existing TripTools test's mediator-mock setup) asserting `push_place_profile` sends a `PushPlaceProfileCommand` with the given ids and returns the mediator's `TripPlaceDto`.

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~PushPlaceProfile"`
Expected: FAIL to compile — no `push_place_profile`.

- [ ] **Step 3: Add the tool**

In `TripTools.cs` add the using near the other `Trips.*` usings:

```csharp
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
```

and add the method (next to `update_trip_place`):

```csharp
    [McpServerTool, Description("Push the current per-trip enrichment of a saved place UP to the user's master place-profile (FULL overwrite of the master: best-time window, review links, checklist item-set, AND season periods), so future captures of the same Google place start from it. Shape the place with update_trip_place FIRST, then push. Returns the place.")]
    public async Task<TripPlaceDto> push_place_profile(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        CancellationToken ct)
        => await mediator.Send(new PushPlaceProfileCommand(tripId, placeId), ct);
```

> Match the surrounding tools' exact mediator accessor (`mediator` vs `_mediator`) and method-async style in this file.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~PushPlaceProfile"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs backend/tests/MenuNest.McpServer.UnitTests/
git commit -m "feat(trips): expose push_place_profile as an MCP tool (#19)"
```

---

### Task 7: Frontend season logic — lib/season.ts (pure, unit-tested)

**Files:**
- Create: `frontend/src/pages/trips/lib/season.ts`
- Test: `frontend/src/pages/trips/lib/season.test.ts`

**Interfaces:**
- Consumes: `SeasonPeriod` type (added to `api.ts` in Task 8 — but `lib/season.ts` may define its own import; add Task 8 first if the type import fails, or inline the type here and re-export). To keep tasks independently green, **define the type in Task 8 and import it here**; order Task 8 before Task 7's commit, or temporarily declare `type SeasonPeriod = {kind:'Good'|'Bad'; months:number[]; note:string|null}` at the top of `season.ts` and remove it in Task 8. Simplest: **do Task 8 first** (types compile alone), then Task 7.
- Produces: `MonthStatus`, `monthStatus(periods, m)`, `rangeLabel(months)`, `monthOfDate(isoDate)`, `THAI_MONTHS`.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/pages/trips/lib/season.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {monthStatus, rangeLabel, monthOfDate} from './season'
import type {SeasonPeriod} from '../../../shared/api/api'

const bad = (months: number[], note = 'x'): SeasonPeriod => ({kind: 'Bad', months, note})
const good = (months: number[], note = 'y'): SeasonPeriod => ({kind: 'Good', months, note})

describe('monthStatus', () => {
  it('bad wins over good on the same month', () => {
    const s = monthStatus([good([5]), bad([5])], 5)
    expect(s.kind).toBe('bad')
  })
  it('returns the first matching bad period on overlap', () => {
    const s = monthStatus([bad([5], 'first'), bad([5], 'second')], 5)
    expect(s.kind === 'bad' && s.period.note).toBe('first')
  })
  it('good when only a good period matches', () => {
    expect(monthStatus([good([10, 11])], 11).kind).toBe('good')
  })
  it('none when nothing matches', () => {
    expect(monthStatus([bad([5])], 0).kind).toBe('none')
  })
})

describe('rangeLabel', () => {
  it('compresses a contiguous run', () => {
    expect(rangeLabel([5, 6, 7, 8, 9])).toBe('มิ.ย.–ต.ค.')
  })
  it('wraps across the year boundary into separate runs', () => {
    expect(rangeLabel([10, 11, 0, 1])).toBe('ม.ค.–ก.พ., พ.ย.–ธ.ค.')
  })
  it('renders a single month without a dash', () => {
    expect(rangeLabel([11])).toBe('ธ.ค.')
  })
})

describe('monthOfDate', () => {
  it('parses 0-based month from an ISO date', () => {
    expect(monthOfDate('2026-07-12')).toBe(6)
    expect(monthOfDate('2026-01-01T00:00:00')).toBe(0)
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/season.test.ts`
Expected: FAIL — `./season` not found.

- [ ] **Step 3: Implement `lib/season.ts`**

```ts
import type {SeasonPeriod} from '../../../shared/api/api'

/** Thai month abbreviations, 0-indexed (0 = January). */
export const THAI_MONTHS = [
  'ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.',
  'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.',
] as const

export type MonthStatus =
  | {kind: 'bad'; period: SeasonPeriod}
  | {kind: 'good'; period: SeasonPeriod}
  | {kind: 'none'}

/** Resolve a place's season for month `m` (0..11): the first `Bad` period wins, then the first `Good`. */
export function monthStatus(periods: SeasonPeriod[] | undefined, m: number): MonthStatus {
  const list = periods ?? []
  const bad = list.find((p) => p.kind === 'Bad' && p.months.includes(m))
  if (bad) return {kind: 'bad', period: bad}
  const good = list.find((p) => p.kind === 'Good' && p.months.includes(m))
  if (good) return {kind: 'good', period: good}
  return {kind: 'none'}
}

/** 0-based month (0 = January) of a 'yyyy-MM-dd' date, matching useSchedule.dayOfWeek's UTC parse. */
export function monthOfDate(isoDate: string): number {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCMonth()
}

/** Compress a month set into wrap-aware Thai ranges, e.g. [10,11,0,1] → "ม.ค.–ก.พ., พ.ย.–ธ.ค.". */
export function rangeLabel(months: number[]): string {
  const sorted = [...new Set(months)].filter((m) => m >= 0 && m <= 11).sort((a, b) => a - b)
  if (sorted.length === 0) return ''
  const runs: Array<[number, number]> = []
  for (const m of sorted) {
    const last = runs[runs.length - 1]
    if (last && m === last[1] + 1) last[1] = m
    else runs.push([m, m])
  }
  return runs
    .map(([s, e]) => (s === e ? THAI_MONTHS[s] : `${THAI_MONTHS[s]}–${THAI_MONTHS[e]}`))
    .join(', ')
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/trips/lib/season.test.ts`
Expected: PASS (all cases). If Task 8's `SeasonPeriod` type is not yet added, do Task 8 first.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/season.ts frontend/src/pages/trips/lib/season.test.ts
git commit -m "feat(trips): lib/season.ts monthStatus + rangeLabel + monthOfDate (#19)"
```

---

### Task 8: Frontend API types (do before Task 7 if you hit a missing-type error)

**Files:**
- Modify: `frontend/src/shared/api/api.ts:496-518, 1300, 1304`

**Interfaces:**
- Produces: `interface SeasonPeriod {kind: 'Good' | 'Bad'; months: number[]; note: string | null}`; `TripPlaceDto.seasonPeriods`; `updateTripPlace` arg `seasonPeriods`; `addTripPlace` Omit excludes `seasonPeriods`.

- [ ] **Step 1: Add the `SeasonPeriod` interface + DTO field**

In `frontend/src/shared/api/api.ts`, after the `ReviewLink` interface (line 496-499) add:

```ts
export interface SeasonPeriod {
    kind: 'Good' | 'Bad'
    months: number[]
    note: string | null
}
```

and add `seasonPeriods` to `TripPlaceDto` (after `hasProfile`, line 517):

```ts
    hasProfile: boolean
    seasonPeriods: SeasonPeriod[]
```

- [ ] **Step 2: Add it to the mutation types**

In `addTripPlace` (line 1300), add `'seasonPeriods'` to the `Omit<...>` (season is set post-capture, like bestTime):

```ts
        addTripPlace: build.mutation<TripPlaceDto, {tripId: string} & Omit<TripPlaceDto, 'id' | 'tripId' | 'bestTimeStart' | 'bestTimeEnd' | 'feeNote' | 'notes' | 'hasProfile' | 'seasonPeriods'>>({
```

In `updateTripPlace` (line 1304), add `seasonPeriods` to the arg type (full-replace, always sent):

```ts
        updateTripPlace: build.mutation<TripPlaceDto, {tripId: string; placeId: string; name: string; category: PlaceCategory; address?: string | null; feeNote?: string | null; notes?: string | null; bestTimeStart?: string | null; bestTimeEnd?: string | null; reviewLinks: ReviewLink[]; seasonPeriods: SeasonPeriod[]}>({
```

- [ ] **Step 3: Typecheck + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS (existing `updateTripPlace` callers — `StopEditorDialog`, `PlaceEditorDialog` — will now FAIL tsc because `seasonPeriods` is required; that is expected and fixed in Task 9. If you are committing Task 8 alone, temporarily pass `seasonPeriods: place.seasonPeriods ?? []` at those two call sites so the tree is green, then replace with real state in Task 9.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/trips/components/StopEditorDialog.tsx \
        frontend/src/pages/trips/components/PlaceEditorDialog.tsx
git commit -m "feat(trips): season API types + updateTripPlace arg (#19)"
```

> Ordering note: Tasks 7 and 8 are both green-alone once the `SeasonPeriod` type exists. Commit **Task 8 first**, then Task 7, so `season.ts`'s type import resolves.

---

### Task 9: PlaceSeasonEditor (year ribbon) + wire into the editors

**Files:**
- Create: `frontend/src/pages/trips/components/PlaceSeasonEditor.tsx`
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx`
- Modify: `frontend/src/pages/trips/components/PlaceEditorDialog.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (ribbon + rows styles)

**Interfaces:**
- Consumes: `SeasonPeriod` (Task 8), `THAI_MONTHS`/`rangeLabel` (Task 7).
- Produces: `PlaceSeasonEditor({periods, onChange})` — a controlled component whose `periods: SeasonPeriod[]` are lifted into the parent dialog and sent through `updateTripPlace`.

- [ ] **Step 1: Implement `PlaceSeasonEditor.tsx`**

```tsx
import {useState} from 'react'
import type {SeasonPeriod} from '../../../shared/api/api'
import {THAI_MONTHS, rangeLabel} from '../lib/season'

type Draft = {kind: 'Good' | 'Bad'; months: number[]; note: string}
const NOW_MONTH = new Date().getMonth() // display-only "now" marker

export function PlaceSeasonEditor({
  periods,
  onChange,
}: {
  periods: SeasonPeriod[]
  onChange: (periods: SeasonPeriod[]) => void
}) {
  const [draft, setDraft] = useState<Draft | null>(null)

  const ribbonKind = (m: number): 'good' | 'bad' | 'none' => {
    if (draft?.months.includes(m)) return draft.kind === 'Bad' ? 'bad' : 'good'
    if (periods.some((p) => p.kind === 'Bad' && p.months.includes(m))) return 'bad'
    if (periods.some((p) => p.kind === 'Good' && p.months.includes(m))) return 'good'
    return 'none'
  }

  const toggleMonth = (m: number) => {
    if (!draft) return
    setDraft({...draft, months: draft.months.includes(m) ? draft.months.filter((x) => x !== m) : [...draft.months, m]})
  }

  const saveDraft = () => {
    if (!draft || draft.months.length === 0) return
    onChange([...periods, {kind: draft.kind, months: [...draft.months].sort((a, b) => a - b), note: draft.note.trim() || null}])
    setDraft(null)
  }

  return (
    <section className="se-sec season-editor">
      <div className="se-sec-head">ช่วงเดือน (ควรไป / ควรเลี่ยง)</div>

      <div className="season-ribbon" role="group" aria-label="ปฏิทินฤดูกาล">
        {THAI_MONTHS.map((label, m) => (
          <button
            type="button"
            key={m}
            className={`sr-cell ${ribbonKind(m)}${m === NOW_MONTH ? ' now' : ''}${draft?.months.includes(m) ? ' draft' : ''}`}
            aria-pressed={draft ? draft.months.includes(m) : undefined}
            onClick={() => (draft ? toggleMonth(m) : undefined)}
            disabled={!draft}
          >
            {label}
          </button>
        ))}
      </div>

      <ul className="season-rows">
        {periods.map((p, i) => (
          <li className={`sp-row ${p.kind === 'Bad' ? 'bad' : 'good'}`} key={i}>
            <span className="sp-pill">{p.kind === 'Bad' ? 'ควรเลี่ยง' : 'ควรไป'}</span>
            <span className="sp-range">{rangeLabel(p.months)}</span>
            {p.note && <span className="sp-note">{p.note}</span>}
            <button type="button" className="sp-del" aria-label="ลบช่วง" onClick={() => onChange(periods.filter((_, j) => j !== i))}>✕</button>
          </li>
        ))}
      </ul>

      {draft ? (
        <div className="sp-draft">
          <div className="sp-kind">
            <button type="button" className={`sp-kbtn good${draft.kind === 'Good' ? ' active' : ''}`} onClick={() => setDraft({...draft, kind: 'Good'})}>ควรไป</button>
            <button type="button" className={`sp-kbtn bad${draft.kind === 'Bad' ? ' active' : ''}`} onClick={() => setDraft({...draft, kind: 'Bad'})}>ควรเลี่ยง</button>
          </div>
          <input className="sp-note-input" placeholder="เหตุผล (ไม่บังคับ)" value={draft.note} onChange={(e) => setDraft({...draft, note: e.target.value})} />
          <div className="sp-draft-foot">
            <button type="button" className="sp-cancel" onClick={() => setDraft(null)}>ยกเลิก</button>
            <button type="button" className="sp-save" disabled={draft.months.length === 0} onClick={saveDraft}>เพิ่มช่วง</button>
          </div>
          <p className="sp-hint">แตะเดือนบนแถบด้านบนเพื่อเลือก</p>
        </div>
      ) : (
        periods.length < 12 && (
          <button type="button" className="sp-add" onClick={() => setDraft({kind: 'Bad', months: [], note: ''})}>+ เพิ่มช่วง</button>
        )
      )}
    </section>
  )
}
```

- [ ] **Step 2: Wire into `StopEditorDialog.tsx`**

Add the import and lifted state, include season in the save payload, and render the editor. Concretely:

Import (near line 22):
```ts
import {PlaceSeasonEditor} from './PlaceSeasonEditor'
```
State (near line 53):
```ts
const [seasonPeriods, setSeasonPeriods] = useState(place?.seasonPeriods ?? [])
```
In `save()` (line 87-102), add a change check and always send `seasonPeriods` on the update:
```ts
      const seasonChanged = JSON.stringify(seasonPeriods) !== JSON.stringify(place?.seasonPeriods ?? [])
      if (place && (bestTimeChanged || reviewsChanged || seasonChanged)) {
        await updatePlace({
          tripId, placeId: place.id, name: place.name, category: place.category,
          address: place.address, feeNote: place.feeNote, notes: place.notes,
          bestTimeStart: bestStart, bestTimeEnd: bestEnd, reviewLinks: cleaned,
          seasonPeriods,
        }).unwrap()
      }
```
Render (after `<ReviewLinksSection .../>`, line 185):
```tsx
        <PlaceSeasonEditor periods={seasonPeriods} onChange={setSeasonPeriods} />
```

- [ ] **Step 3: Wire into `PlaceEditorDialog.tsx`**

Mirror Step 2 in `PlaceEditorDialog.tsx`: import `PlaceSeasonEditor`, add `seasonPeriods` state seeded from `place.seasonPeriods ?? []`, include it in that dialog's `updatePlace`/persist payload, and render `<PlaceSeasonEditor periods={seasonPeriods} onChange={setSeasonPeriods} />` next to its `ReviewLinksSection`. (Open the file to match its exact save function and section placement.)

- [ ] **Step 4: Add styles to `TripDetailPage.css`**

```css
.season-ribbon { display: grid; grid-template-columns: repeat(6, 1fr); gap: 6px; margin: 8px 0; }
.sr-cell { border: 1.5px solid var(--trip-border, #ece4d9); border-radius: 10px; padding: 7px 0; font-size: 12.5px; font-weight: 600; color: #6b625b; background: #fff; cursor: pointer; }
.sr-cell:disabled { cursor: default; }
.sr-cell.good { border-color: #bfe6d3; background: #e7f6ef; color: #1f9d6b; }
.sr-cell.bad { border-color: #f3c9bf; background: #fdece8; color: #d4462a; }
.sr-cell.now { box-shadow: inset 0 0 0 2px #2b2521; }
.sr-cell.draft { box-shadow: inset 0 0 0 2px #ef6d2d; }
.season-rows { list-style: none; margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 7px; }
.sp-row { display: flex; align-items: center; gap: 9px; border: 1.5px solid var(--trip-border, #ece4d9); border-radius: 12px; padding: 8px 10px; }
.sp-pill { font-size: 11.5px; font-weight: 700; padding: 3px 9px; border-radius: 999px; }
.sp-row.good .sp-pill { background: #e7f6ef; color: #1f9d6b; } .sp-row.bad .sp-pill { background: #fdece8; color: #d4462a; }
.sp-range { font-weight: 600; } .sp-note { color: #6b625b; font-size: 12.5px; flex: 1; }
.sp-del, .sp-add, .sp-save, .sp-cancel, .sp-kbtn { cursor: pointer; font: inherit; }
.sp-add { width: 100%; padding: 9px; border: 1.5px dashed #f6d9bf; border-radius: 12px; background: #fdefe1; color: #d95f22; font-weight: 700; }
.sp-kind { display: inline-flex; gap: 6px; } .sp-kbtn { border: 1.5px solid var(--trip-border, #ece4d9); border-radius: 999px; padding: 6px 14px; font-weight: 700; background: #fff; color: #6b625b; }
.sp-kbtn.good.active { border-color: #bfe6d3; background: #e7f6ef; color: #1f9d6b; } .sp-kbtn.bad.active { border-color: #f3c9bf; background: #fdece8; color: #d4462a; }
.sp-note-input { width: 100%; margin-top: 10px; border: 1.5px solid var(--trip-border, #ece4d9); border-radius: 11px; padding: 9px 12px; font: inherit; }
.sp-draft-foot { display: flex; justify-content: flex-end; gap: 8px; margin-top: 10px; } .sp-hint { color: #a99e94; font-size: 12px; margin: 6px 0 0; }
```

- [ ] **Step 5: Typecheck + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS. (No vitest — the editor has no unit coverage; verified interactively in Task 12.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/PlaceSeasonEditor.tsx \
        frontend/src/pages/trips/components/StopEditorDialog.tsx \
        frontend/src/pages/trips/components/PlaceEditorDialog.tsx \
        frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): PlaceSeasonEditor year ribbon in the place/stop editors (#19)"
```

---

### Task 10: WeatherDiorama canvas component

**Files:**
- Create: `frontend/src/pages/trips/components/WeatherDiorama.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (`.weather-diorama` sizing)

**Interfaces:**
- Produces: `WeatherDiorama({kind}: {kind: 'good' | 'bad' | 'none'})` — a self-contained canvas, RAF gated by `IntersectionObserver`, static frame under `prefers-reduced-motion`.

> **Source note:** the owner's `Place Season Redesign.dc.html` is not on disk. The engine below is a faithful **reconstruction** from the handoff scene table. If that file is later added to `docs/mocks/`, replace this engine's body with a verbatim port of its logic class (keep the same props/API).

- [ ] **Step 1: Implement `WeatherDiorama.tsx`**

```tsx
import {useEffect, useRef} from 'react'

type Kind = 'good' | 'bad' | 'none'
const SCENE: Record<Kind, {waterFrac: number; rain: number; lightning: boolean; sky: [string, string]}> = {
  bad: {waterFrac: 0.46, rain: 5, lightning: true, sky: ['#4c5a68', '#8a97a1']},
  good: {waterFrac: 0.26, rain: 0, lightning: false, sky: ['#8fc7e8', '#d7eefb']},
  none: {waterFrac: 0.32, rain: 1, lightning: false, sky: ['#9aa2a9', '#d6dade']},
}

export function WeatherDiorama({kind}: {kind: Kind}) {
  const ref = useRef<HTMLCanvasElement>(null)
  const kindRef = useRef<Kind>(kind)
  kindRef.current = kind

  useEffect(() => {
    const cv = ref.current
    if (!cv) return
    const ctx = cv.getContext('2d')
    if (!ctx) return
    const W = (cv.width = cv.clientWidth * devicePixelRatio)
    const H = (cv.height = cv.clientHeight * devicePixelRatio)
    ctx.scale(1, 1)
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches

    type Drop = {x: number; y: number; v: number}
    const drops: Drop[] = []
    let ripples: {x: number; y: number; r: number}[] = []
    let bolt: {x: number; segs: number[]} | null = null
    let flash = 0
    let raf = 0
    let running = false

    const rand = (n: number) => (Math.sin((n + drops.length) * 12.9898) * 43758.5453 % 1 + 1) % 1 // deterministic-ish

    const frame = (t: number) => {
      const s = SCENE[kindRef.current]
      const waterY = H * (1 - s.waterFrac)
      // sky
      const g = ctx.createLinearGradient(0, 0, 0, H)
      g.addColorStop(0, s.sky[0]); g.addColorStop(1, s.sky[1])
      ctx.fillStyle = g; ctx.fillRect(0, 0, W, H)
      // rocks (สามพันโบก hump), dark
      ctx.fillStyle = '#3b342d'
      ctx.beginPath(); ctx.moveTo(0, H)
      ctx.quadraticCurveTo(W * 0.3, H * 0.45, W * 0.55, H * 0.62)
      ctx.quadraticCurveTo(W * 0.8, H * 0.8, W, H * 0.6)
      ctx.lineTo(W, H); ctx.closePath(); ctx.fill()
      // sun (good)
      if (s.rain === 0) { ctx.fillStyle = 'rgba(255,241,196,0.9)'; ctx.beginPath(); ctx.arc(W * 0.8, H * 0.28, H * 0.14, 0, Math.PI * 2); ctx.fill() }
      // water (semi-transparent → rocks read submerged in bad)
      ctx.fillStyle = kindRef.current === 'bad' ? 'rgba(60,90,110,0.82)' : 'rgba(120,170,200,0.6)'
      ctx.fillRect(0, waterY, W, H - waterY)
      // rain
      if (s.rain > 0 && !reduced) {
        while (drops.length < s.rain * 12) drops.push({x: rand(drops.length) * W, y: rand(drops.length + 1) * H, v: 6 + rand(drops.length + 2) * 8})
        ctx.strokeStyle = 'rgba(220,230,240,0.5)'; ctx.lineWidth = 1
        for (const d of drops) {
          ctx.beginPath(); ctx.moveTo(d.x, d.y); ctx.lineTo(d.x - 2, d.y + 9); ctx.stroke()
          d.y += d.v; if (d.y > waterY) { d.y = -10; d.x = rand(d.x) * W; if (ripples.length < 20) ripples.push({x: d.x, y: waterY, r: 0}) }
        }
      }
      // ripples
      ctx.strokeStyle = 'rgba(255,255,255,0.35)'
      ripples = ripples.filter((r) => r.r < 18)
      for (const r of ripples) { ctx.beginPath(); ctx.ellipse(r.x, r.y, r.r, r.r * 0.3, 0, 0, Math.PI * 2); ctx.stroke(); r.r += 0.6 }
      // lightning (bad)
      if (s.lightning && !reduced) {
        if (!bolt && Math.floor(t / 900) % 3 === 0) bolt = {x: W * (0.3 + rand(t) * 0.4), segs: [0, 0.2, -0.15, 0.25, 0]}
        if (bolt) {
          flash = Math.min(1, flash + 0.3)
          ctx.strokeStyle = 'rgba(255,255,255,0.95)'; ctx.lineWidth = 2; ctx.beginPath()
          let y = 0, x = bolt.x
          ctx.moveTo(x, y)
          for (const dx of bolt.segs) { x += dx * 40; y += H * 0.16; ctx.lineTo(x, y) }
          ctx.stroke()
          if (Math.floor(t / 900) % 3 !== 0) bolt = null
        } else flash = Math.max(0, flash - 0.08)
        if (flash > 0) { ctx.fillStyle = `rgba(255,255,255,${flash * 0.25})`; ctx.fillRect(0, 0, W, H) }
      }
      if (running) raf = requestAnimationFrame(frame)
    }

    const io = new IntersectionObserver(
      ([e]) => {
        if (e.isIntersecting && !reduced) { if (!running) { running = true; raf = requestAnimationFrame(frame) } }
        else { running = false; cancelAnimationFrame(raf); if (reduced) frame(0) }
      },
      {threshold: 0.05},
    )
    io.observe(cv)
    if (reduced) frame(0)
    return () => { running = false; cancelAnimationFrame(raf); io.disconnect() }
  }, [])

  return <canvas ref={ref} className="weather-diorama" aria-hidden="true" />
}
```

- [ ] **Step 2: Size the canvas**

Add to `TripDetailPage.css`:

```css
.weather-diorama { display: block; width: 100%; height: 84px; border-radius: 14px 14px 0 0; }
```

- [ ] **Step 3: Typecheck + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/trips/components/WeatherDiorama.tsx frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): WeatherDiorama illustrative season canvas (#19)"
```

---

### Task 11: Wire the on-card off-season warning

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/components/StopDetailSheet.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`

**Interfaces:**
- Consumes: `monthStatus`, `monthOfDate` (Task 7), `WeatherDiorama` (Task 10), `TripPlaceDto.seasonPeriods` (Task 8).
- Produces: a `tripMonth: number` prop on `ItineraryStopCard` and `StopDetailSheet`; a season region on the card.

- [ ] **Step 1: Add the season region to `ItineraryStopCard`**

In `ItineraryStopCard.tsx`, add the imports:
```ts
import {WeatherDiorama} from './WeatherDiorama'
import {monthStatus} from '../lib/season'
```
Add `tripMonth: number` to the props type (in the destructure and the type block, lines 35-53). Compute status and render the region at the top of the card body (before `<div className="stop-rail">`), and add the status accent:
```tsx
  const season = monthStatus(place.seasonPeriods, tripMonth)
```
Insert, as the first child inside the outer card `<div>`:
```tsx
      {season.kind !== 'none' && (
        <div className={`stop-season ${season.kind}`}>
          <WeatherDiorama kind={season.kind} />
          {season.kind === 'bad' && (
            <div className="stop-season-note">
              <strong>เดือนนี้ควรเลี่ยง{season.period.note ? ` · ${season.period.note}` : ''}</strong>
              <span>ย้ายทริปไปเดือนอื่น</span>
            </div>
          )}
        </div>
      )}
```
Append the season accent to the card className (line 65): `${season.kind === 'bad' ? ' season-bad' : season.kind === 'good' ? ' season-good' : ''}`.

- [ ] **Step 2: Thread `tripMonth` from `ItineraryTab`**

In `ItineraryTab.tsx`, near `resolvedDay` (≈ line 198) compute:
```ts
import {monthOfDate} from '../lib/season'
// ...
const tripMonth = monthOfDate(resolvedDay.date)
```
Pass `tripMonth={tripMonth}` to `<ItineraryStopCard ... />` (≈ line 400-409) and to `<StopDetailSheet ... />` (≈ line 478-511).

- [ ] **Step 3: Show the season status in `StopDetailSheet`**

In `StopDetailSheet.tsx`, add `tripMonth: number` to its props, compute `const season = monthStatus(place.seasonPeriods, tripMonth)`, and render a `<WeatherDiorama kind={season.kind} />` + the same status text near the weather/flag block. Also list the place's season periods (kind pill + `rangeLabel` + note) as reference, mirroring how review links are listed.

- [ ] **Step 4: Styles**

```css
.stop-season { border-radius: 14px 14px 0 0; overflow: hidden; margin: -1px -1px 0; }
.stop-season-note { display: flex; flex-direction: column; gap: 2px; padding: 8px 12px; background: #fdece8; }
.stop-season-note strong { color: #d4462a; font-size: 13px; } .stop-season-note span { color: #6b625b; font-size: 12.5px; }
.stop-card.season-bad { border-top: 2px solid #d4462a; } .stop-card.season-good { border-top: 2px solid #1f9d6b; }
```

- [ ] **Step 5: Typecheck + build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryStopCard.tsx \
        frontend/src/pages/trips/components/ItineraryTab.tsx \
        frontend/src/pages/trips/components/StopDetailSheet.tsx \
        frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): on-card off-season warning via monthStatus + WeatherDiorama (#19)"
```

---

### Task 12: Interactive verification + prod migration + push

**Files:** none (verification gate).

- [ ] **Step 1: Run the whole suite locally**

Run: `cd backend && dotnet build --configuration Release && dotnet test --configuration Release` then `cd ../frontend && npx tsc --noEmit && npm run build && npx vitest run`
Expected: all green (this is what pre-commit already enforced per task, re-run as a final gate).

- [ ] **Step 2: Interactive smoke test (no automated harness covers UI)**

Start the app (see the `run` skill / project README). In a seeded, authed trip:
- Open a place editor → year ribbon renders; add a `ควรเลี่ยง` period (tap months, note) → row appears; Save; reopen → season persisted.
- On the itinerary, a Stop whose day month ∈ an avoid period shows the `WeatherDiorama` (storm/flood) + "เดือนนี้ควรเลี่ยง · <note>" + red top border; a good-month stop shows the calm scene; a neutral month shows none.
- Confirm the diorama pauses off-screen and renders a static frame under OS "reduce motion".
- Over MCP (owner's Claude): `list_trip_places` → `update_trip_place` with `seasonPeriods` writes; `push_place_profile` persists to master; re-capture in a new trip seeds the season.

- [ ] **Step 3: Apply the migration to prod BY HAND**

Per CLAUDE.md: preview `dotnet ef migrations script --idempotent`; add a temp SQL firewall rule for your IP; run the `AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update …` against `menunest-sql`; remove the rule. (Do this before/at deploy, or the deployed API 500s with "Invalid column name 'SeasonPeriodsJson'".)

- [ ] **Step 4: Push**

```bash
git push main HEAD:main
```
Then verify the SPA on prod (season editor + card warning) and confirm the deployed API returns `seasonPeriods`.

---

## Self-Review

**Spec coverage:** data model (Tasks 1-2) · master/override/push lifecycle (Task 4) · AI-via-MCP write + push tool (Tasks 5-6) · read DTO (Task 3) · lib/season.ts monthStatus/rangeLabel (Task 7) · year-ribbon editor (Task 9) · WeatherDiorama illustrative animation (Task 10) · on-card off-season warning threaded from projected date (Task 11) · glossary/ADRs already committed · migration-by-hand + interactive verify (Task 12). All spec sections map to a task.

**Placeholder scan:** the only intentional "reconstruct" is the WeatherDiorama engine (Task 10) — flagged, with a verbatim-port swap path if the mock file arrives; complete working code is provided regardless. Two "open the file to match exact setup" notes (Sqlite test factory in Tasks 2/4; `PlaceEditorDialog` save shape in Task 9) point to concrete sibling files rather than leaving code blank — the surrounding code is fully specified.

**Type consistency:** `SeasonKind {Good,Bad}` (C#) ↔ `'Good'|'Bad'` (TS) throughout; `SeasonPeriod`/`SeasonPeriodDto` field order (`Kind, Months, Note`) consistent across VO, DTO, command, MCP; `monthStatus` returns `{kind:'bad'|'good'|'none'}` (lowercased status) consuming `SeasonPeriod.kind` (`'Good'|'Bad'`) — the case difference is deliberate (status vs kind) and used consistently in Tasks 7/9/11; months 0-indexed everywhere.
