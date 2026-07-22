# Best-time Windows (issue #38) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace a Place's single best-time window (`BestTimeStart`/`BestTimeEnd`) with an ordered LIST of good time-of-day windows `{start, end, note?}`, end-to-end.

**Architecture:** Mirror the shipped `SeasonPeriod` JSON-value-list pattern on the time-of-day axis — a `BestTimeWindow` value object stored as one `nvarchar(max)` JSON column on both `TripPlace` and `PlaceProfile`, threaded through the positional `TripPlaceDto`/`DiscoverPlaceDto`, the `update_trip_place`/`push_place_profile` MCP + REST contracts (full-replace), and the frontend editor/detail-sheet/off-window-flag. Existing single-window prod data migrates into the list; the scalar columns are dropped.

**Tech Stack:** C# / .NET (clean architecture: Domain / Application / Infrastructure / WebApi / McpServer), EF Core (SQL Server), FluentValidation, Mediator, Moq + xUnit + FluentAssertions; React + TypeScript SPA, RTK Query, Syncfusion, Vitest.

**Design source:** spec `docs/superpowers/specs/2026-07-22-best-time-windows-design.md`; ADRs 126–129; mock MenuNest design system → Screens → `issue-38-best-time-windows`.

## Global Constraints

- **The pre-commit hook (`frontend/.husky/pre-commit`, `set -e`) runs the FULL suite on EVERY commit** — backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build`. Every commit must leave the **entire** suite green. Do **not** `--no-verify`.
- **Stage narrowly** — always `git add <explicit paths>`; never `git add -A`/`.`. Never stage `daily-state.md` or `AGENTS.md`.
- **Git remote is named `main`** (not `origin`): push with `git push main HEAD:main`. `gh` needs `--repo ThodsaphonSonthiphin/MenuNest`.
- **Three `IApplicationDbContext` implementers** — `AppDbContext` (prod) + `SqliteAppDbContext` pick up EF configs via `ApplyConfigurationsFromAssembly`; `InMemoryAppDbContext` needs a hand-written `HasConversion` mirror. A new value-list needs the config in `TripPlaceConfiguration`/`PlaceProfileConfiguration` **and** the InMemory mirror.
- **Migrations are applied to prod MANUALLY** (see §Migration & Rollout) — no code applies them.
- **Backend tests:** xUnit + Moq + FluentAssertions; relational tests use `SqliteAppDbContext`.
- **Frontend has no DOM/visual test harness** (vitest runs in `node`): only pure `lib/`/hook logic is unit-testable; the editor/sheet must be verified interactively against the mock.
- **Value-object serialization:** `JsonSerializerDefaults.Web` (camelCase keys); `TimeOnly` ↔ `"HH:mm:ss"`.
- **Naming:** value object `BestTimeWindow {Start, End, Note}`; entity list `BestTimeWindows`; setter `SetBestTimeWindows`; JSON column `BestTimeWindowsJson`; DTO `BestTimeWindowDto`; frontend interface `BestTimeWindow {start, end, note}`; pure resolver `resolveBestTime`. Cap = **6** windows; note ≤ **200** chars; `end > start`.
- **Commit refs:** intermediate commits use `(#38)`; the final commit uses `(closes #38)`.

---

## Task 1: Backend — best-time becomes a per-Place window LIST (atomic)

This is one commit by necessity: removing `BestTimeStart`/`BestTimeEnd`/`SetBestTime` from the entities breaks every consumer (DTOs, handlers, `PlaceProfileSync`, `ListMyPlaces`, REST, MCP, tests) and the EF model, so they must all change together for the pre-commit build to pass (the #33 lesson). Work through the steps in order; the solution will not build until the last code step; **commit once at the end** after the full backend suite is green.

**Files:**
- Create: `backend/src/MenuNest.Domain/ValueObjects/BestTimeWindow.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/TripPlace.cs` (remove `BestTimeStart`/`BestTimeEnd`/`SetBestTime`; add list + `SetBestTimeWindows`)
- Modify: `backend/src/MenuNest.Domain/Entities/PlaceProfile.cs` (same)
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddBestTimeWindows.cs` (via `dotnet ef`, then hand-edit)
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/PlaceProfileSync.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Places/PlaceDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Places/ListMyPlaces/ListMyPlacesHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (`UpdatePlaceBody` + `UpdatePlace`)
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (`update_trip_place`, `push_place_profile` desc)
- Test (new): `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/BestTimeWindowTests.cs`
- Test (modify): `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs`
- Test (modify): `backend/tests/MenuNest.Application.UnitTests/Trips/PlaceProfile*RelationalTests.cs`, `PushPlaceProfileHandlerTests.cs`
- Test (modify): `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs`

**Interfaces:**
- Produces (Domain): `BestTimeWindow(TimeOnly Start, TimeOnly End, string? Note)` with `static BestTimeWindow Create(TimeOnly start, TimeOnly end, string? note)`; `TripPlace.BestTimeWindows`/`PlaceProfile.BestTimeWindows` (`IReadOnlyList<BestTimeWindow>`); `SetBestTimeWindows(IEnumerable<BestTimeWindow>)`.
- Produces (Application): `BestTimeWindowDto(TimeOnly Start, TimeOnly End, string? Note)`; `TripPlaceDto` gains trailing `IReadOnlyList<BestTimeWindowDto> BestTimeWindows` (and loses `BestTimeStart`/`BestTimeEnd`); `UpdateTripPlaceCommand` carries `IReadOnlyList<BestTimeWindowDto> BestTimeWindows`.
- Consumes: the existing `SeasonPeriod`/`SeasonPeriodDto` plumbing as the exact template.

### Step-by-step

- [ ] **Step 1: Write the failing VO test** — `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/BestTimeWindowTests.cs`

```csharp
using FluentAssertions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class BestTimeWindowTests
{
    [Fact]
    public void Create_trims_blank_note_to_null()
    {
        var w = BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), "  ");
        w.Note.Should().BeNull();
        w.Start.Should().Be(new TimeOnly(6, 0));
        w.End.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void Create_rejects_end_not_after_start() =>
        FluentActions.Invoking(() => BestTimeWindow.Create(new TimeOnly(9, 0), new TimeOnly(9, 0), null))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_rejects_note_over_200_chars() =>
        FluentActions.Invoking(() => BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), new string('x', 201)))
            .Should().Throw<DomainException>();
}
```

- [ ] **Step 2: Create the value object** — `backend/src/MenuNest.Domain/ValueObjects/BestTimeWindow.cs`

```csharp
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place "good" time-of-day window (issue #38): a start–end wall-clock range with an
/// optional reason. Positional record with a public ctor so System.Text.Json can round-trip it
/// from the JSON column; user input is validated through <see cref="Create"/>. Every window is
/// "good" — there is no avoid kind (that is Season's calendar axis). Mirrors SeasonPeriod.
/// </summary>
public sealed record BestTimeWindow(TimeOnly Start, TimeOnly End, string? Note)
{
    public static BestTimeWindow Create(TimeOnly start, TimeOnly end, string? note)
    {
        if (end <= start) throw new DomainException("Best-time end must be after start.");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 200 }) throw new DomainException("Best-time note is too long (max 200).");
        return new BestTimeWindow(start, end, n);
    }
}
```

- [ ] **Step 3: Rewrite the entity best-time members** — `TripPlace.cs`

Remove the two scalar properties (lines 24-25) and `SetBestTime` (lines 73-80). Add the backing list beside `_seasonPeriods` (after line 34):

```csharp
    private readonly List<BestTimeWindow> _bestTimeWindows = new();
    public IReadOnlyList<BestTimeWindow> BestTimeWindows => _bestTimeWindows;
```

Add the setter (beside `SetSeasonPeriods`):

```csharp
    public void SetBestTimeWindows(IEnumerable<BestTimeWindow> windows)
    {
        var list = (windows ?? Enumerable.Empty<BestTimeWindow>()).ToList();
        if (list.Count > 6) throw new DomainException("A place can have at most 6 best-time windows.");
        _bestTimeWindows.Clear();
        _bestTimeWindows.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Apply the identical change to `PlaceProfile.cs`** — remove `BestTimeStart`/`BestTimeEnd` (lines 17-18) and `SetBestTime` (lines 36-43); add the same `_bestTimeWindows` field, `BestTimeWindows` property, and `SetBestTimeWindows` (message: "A place profile can have at most 6 best-time windows.").

- [ ] **Step 5: Update the entity setter test** — `TripPlaceTests.cs`, replace the three `SetBestTime_*` tests (lines 31-55) with:

```csharp
    [Fact]
    public void SetBestTimeWindows_replaces_the_whole_list()
    {
        var place = TripPlace.Create(Guid.NewGuid(), "P", 0, 0, PlaceCategory.See);
        place.SetBestTimeWindows(new[]
        {
            BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), "แดดร่ม"),
            BestTimeWindow.Create(new TimeOnly(17, 0), new TimeOnly(19, 0), null),
        });
        place.BestTimeWindows.Should().HaveCount(2);
        place.SetBestTimeWindows(Array.Empty<BestTimeWindow>());
        place.BestTimeWindows.Should().BeEmpty();
    }

    [Fact]
    public void SetBestTimeWindows_rejects_more_than_6() =>
        FluentActions.Invoking(() => TripPlace.Create(Guid.NewGuid(), "P", 0, 0, PlaceCategory.See)
                .SetBestTimeWindows(Enumerable.Range(0, 7)
                    .Select(i => BestTimeWindow.Create(new TimeOnly(i, 0), new TimeOnly(i, 30), null))))
            .Should().Throw<DomainException>();
```

(Check `TripPlaceTests.cs` usings include `MenuNest.Domain.ValueObjects` and `MenuNest.Domain.Enums`; add if missing.)

- [ ] **Step 6: Add the EF JSON config on `TripPlaceConfiguration.cs`** — after the `SeasonPeriods` block (line 58), add:

```csharp
        var bestTimeConverter = new ValueConverter<IReadOnlyList<BestTimeWindow>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => string.IsNullOrEmpty(v)
                ? new List<BestTimeWindow>()
                : JsonSerializer.Deserialize<List<BestTimeWindow>>(v, jsonOpts) ?? new List<BestTimeWindow>());
        var bestTimeComparer = new ValueComparer<IReadOnlyList<BestTimeWindow>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(b, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<BestTimeWindow>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        b.Property(p => p.BestTimeWindows)
            .HasConversion(bestTimeConverter, bestTimeComparer)
            .HasColumnName("BestTimeWindowsJson")
            .HasColumnType("nvarchar(max)")
            .HasField("_bestTimeWindows")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValueSql("'[]'");
```

- [ ] **Step 7: Add the identical block to `PlaceProfileConfiguration.cs`** (after its `SeasonPeriods` block, line 55).

- [ ] **Step 8: Mirror the conversion in `InMemoryAppDbContext.cs`** — after the SeasonPeriods mirror (line 202), add (ensure `using MenuNest.Domain.ValueObjects;` is present):

```csharp
        var bestTimeComparer = new ValueComparer<IReadOnlyList<BestTimeWindow>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, w) => HashCode.Combine(hash, w.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<TripPlace>()
            .Property(p => p.BestTimeWindows)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<BestTimeWindow>)(JsonSerializer.Deserialize<List<BestTimeWindow>>(v, jsonOptions) ?? new List<BestTimeWindow>()),
                bestTimeComparer)
            .HasField("_bestTimeWindows")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        modelBuilder.Entity<PlaceProfile>()
            .Property(p => p.BestTimeWindows)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<BestTimeWindow>)(JsonSerializer.Deserialize<List<BestTimeWindow>>(v, jsonOptions) ?? new List<BestTimeWindow>()),
                bestTimeComparer)
            .HasField("_bestTimeWindows")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);
```

- [ ] **Step 9: Swap the DTOs** — `TripDtos.cs`: add the DTO record after `SeasonPeriodDto` (line 11):

```csharp
public sealed record BestTimeWindowDto(TimeOnly Start, TimeOnly End, string? Note);
```

Rewrite `TripPlaceDto` (lines 17-25) — remove `TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd`, append `BestTimeWindows` last:

```csharp
public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<PlaceChecklistEntryDto> Checklist,
    bool HasProfile,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods,
    IReadOnlyList<BestTimeWindowDto> BestTimeWindows);
```

- [ ] **Step 10: Update the DTO mapper** — `AddTripPlaceHandler.cs` `ToDto` (lines 43-48):

```csharp
    internal static TripPlaceDto ToDto(TripPlace p, IReadOnlyList<PlaceChecklistEntryDto> checklist, bool hasProfile = false) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        checklist, hasProfile,
        p.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
        p.BestTimeWindows.Select(w => new BestTimeWindowDto(w.Start, w.End, w.Note)).ToList());
```

- [ ] **Step 11: Swap the command** — `UpdateTripPlaceCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed record UpdateTripPlaceCommand(
    Guid TripId, Guid PlaceId, string Name, PlaceCategory Category,
    string? Address, string? FeeNote, string? Notes,
    IReadOnlyList<BestTimeWindowDto> BestTimeWindows,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods)
    : ICommand<TripPlaceDto>;
```

- [ ] **Step 12: Update the handler** — `UpdateTripPlaceHandler.cs`, replace line 29 (`place.SetBestTime(...)`) with:

```csharp
        place.SetBestTimeWindows((c.BestTimeWindows ?? Enumerable.Empty<BestTimeWindowDto>())
            .Select(w => BestTimeWindow.Create(w.Start, w.End, w.Note)));
```

- [ ] **Step 13: Update the validator** — `UpdateTripPlaceValidator.cs`, add after the `SeasonPeriods` rules (line 30):

```csharp
        RuleFor(x => x.BestTimeWindows).NotNull()
            .WithMessage("Best-time windows are required (send an empty array for none).");
        RuleFor(x => x.BestTimeWindows).Must(l => l is null || l.Count <= 6)
            .WithMessage("A place can have at most 6 best-time windows.");
        RuleForEach(x => x.BestTimeWindows).ChildRules(w =>
        {
            w.RuleFor(x => x.End).GreaterThan(x => x.Start)
                .WithMessage("Best-time end must be after start.");
            w.RuleFor(x => x.Note).MaximumLength(200);
        });
```

- [ ] **Step 14: Update `PlaceProfileSync.cs`** — line 23 (seed) → `place.SetBestTimeWindows(profile.BestTimeWindows);`; line 65 (push) → `profile.SetBestTimeWindows(place.BestTimeWindows);`. `WriteThroughNotesAndLinksAsync` stays unchanged (best-time excluded, ADR-129).

- [ ] **Step 15: Swap `DiscoverPlaceDto`** — `PlaceDtos.cs`, replace `TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,` (lines 25-26) with `IReadOnlyList<BestTimeWindowDto> BestTimeWindows,` (it keeps the same slot before `SeasonPeriods`; `using MenuNest.Application.UseCases.Trips;` is already present).

- [ ] **Step 16: Update `ListMyPlacesHandler.cs`** — in the `new DiscoverPlaceDto(...)` call (lines 67-84), replace `rep.BestTimeStart, rep.BestTimeEnd,` (lines 78-79) with:

```csharp
                rep.BestTimeWindows.Select(w => new BestTimeWindowDto(w.Start, w.End, w.Note)).ToList(),
```

- [ ] **Step 17: Update the REST layer** — `TripsController.cs`: rewrite `UpdatePlaceBody` (lines 157-161):

```csharp
public sealed record UpdatePlaceBody(
    string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes,
    IReadOnlyList<BestTimeWindowDto> BestTimeWindows,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods);
```

And the `UpdatePlace` action (line 77):

```csharp
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeWindows, b.ReviewLinks, b.SeasonPeriods), ct));
```

- [ ] **Step 18: Update the MCP tool** — `TripTools.cs` `update_trip_place`: replace the two params (lines 108-109) with:

```csharp
        [Description("Best-visit time-of-day windows, each { start: 'HH:mm:ss', end: 'HH:mm:ss' (end after start), note?: string reason }; max 6; all are 'good' windows; FULL REPLACE — omitting or passing an empty array CLEARS all windows.")] IReadOnlyList<BestTimeWindowDto> bestTimeWindows,
```

Update the `=> await mediator.Send(new UpdateTripPlaceCommand(...))` (line 113-114) to pass `bestTimeWindows` in the command's new slot:

```csharp
        => await mediator.Send(new UpdateTripPlaceCommand(
            tripId, placeId, name, category, address, feeNote, notes, bestTimeWindows, reviewLinks, seasonPeriods), ct);
```

In the `update_trip_place` `[Description(...)]` (line 99), replace "the best-visit window (bestTimeStart/bestTimeEnd)" with "the best-visit windows (bestTimeWindows)". In `push_place_profile`'s `[Description(...)]` (line 116), change "best-time window" to "best-time windows".

- [ ] **Step 19: Fix the MCP test literal** — `TripToolsTests.cs` (lines 25-33), match the new `TripPlaceDto` positional shape:

```csharp
        var expectedDto = new TripPlaceDto(
            Guid.NewGuid(), tripId, null, "Wat Arun",
            13.7437, 100.4888, null, PlaceCategory.See,
            null, null,
            null, null, null,
            new List<ReviewLinkDto>(),
            new List<PlaceChecklistEntryDto>(),
            true,
            new List<SeasonPeriodDto>(),
            new List<BestTimeWindowDto>());
```

- [ ] **Step 20: Fix the remaining backend tests that referenced the scalars** — apply these exact swaps (grep `BestTimeStart|BestTimeEnd|SetBestTime` under `backend/tests` to confirm none remain afterward):
  - `PlaceProfileRelationalTests.cs:35` → `profile.SetBestTimeWindows(new[] { BestTimeWindow.Create(new TimeOnly(16, 0), new TimeOnly(18, 30), null) });`; `:42` assertion → `read.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(16, 0));`
  - `PlaceProfileSeedRelationalTests.cs:50` → `profile.SetBestTimeWindows(new[] { BestTimeWindow.Create(new TimeOnly(16, 0), new TimeOnly(18, 0), null) });`; `:68` → `dto.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(16, 0));`; `:80` (the no-profile case) → `dto.BestTimeWindows.Should().BeEmpty();`
  - `PlaceProfileAutoCreateRelationalTests.cs:64,78` → `profile!.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(9, 0));` (whatever window the test's setup now supplies — update the setup to use `SetBestTimeWindows` with a 09:00 start).
  - `PlaceProfileWriteThroughRelationalTests.cs:82` → assert `profile.BestTimeWindows` is unchanged (push-only): `profile.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(9, 0));` and update that test's initial save to set a 09:00 window via `SetBestTimeWindows`.
  - `PushPlaceProfileHandlerTests.cs:73` → `profile.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(20, 0));` and update the test's place setup to `SetBestTimeWindows` a 20:00 window.
  Add `using MenuNest.Domain.ValueObjects;` to any of these files that now reference `BestTimeWindow`.

- [ ] **Step 21: Generate + hand-edit the migration.** With all code compiling, run:

```bash
cd backend
dotnet ef migrations add AddBestTimeWindows --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Then edit the generated `<timestamp>_AddBestTimeWindows.cs` so `Up` **adds the JSON columns, copies the scalar windows into them, then drops the scalars**, and `Down` reverses it. Final body:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(name: "BestTimeWindowsJson", table: "TripPlaces",
        type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'");
    migrationBuilder.AddColumn<string>(name: "BestTimeWindowsJson", table: "PlaceProfiles",
        type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'");

    // Copy each existing single window into a one-element JSON list (note = null).
    // CONVERT(...,108) → 'HH:mm:ss', which System.Text.Json parses back into TimeOnly.
    migrationBuilder.Sql(@"
        UPDATE TripPlaces
        SET BestTimeWindowsJson = '[{""start"":""' + CONVERT(varchar(8), BestTimeStart, 108)
            + '"",""end"":""' + CONVERT(varchar(8), BestTimeEnd, 108) + '"",""note"":null}]'
        WHERE BestTimeStart IS NOT NULL AND BestTimeEnd IS NOT NULL;");
    migrationBuilder.Sql(@"
        UPDATE PlaceProfiles
        SET BestTimeWindowsJson = '[{""start"":""' + CONVERT(varchar(8), BestTimeStart, 108)
            + '"",""end"":""' + CONVERT(varchar(8), BestTimeEnd, 108) + '"",""note"":null}]'
        WHERE BestTimeStart IS NOT NULL AND BestTimeEnd IS NOT NULL;");

    migrationBuilder.DropColumn(name: "BestTimeStart", table: "TripPlaces");
    migrationBuilder.DropColumn(name: "BestTimeEnd", table: "TripPlaces");
    migrationBuilder.DropColumn(name: "BestTimeStart", table: "PlaceProfiles");
    migrationBuilder.DropColumn(name: "BestTimeEnd", table: "PlaceProfiles");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeStart", table: "TripPlaces", type: "time", nullable: true);
    migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeEnd", table: "TripPlaces", type: "time", nullable: true);
    migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeStart", table: "PlaceProfiles", type: "time", nullable: true);
    migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeEnd", table: "PlaceProfiles", type: "time", nullable: true);
    // best-effort restore: first window only
    foreach (var tbl in new[] { "TripPlaces", "PlaceProfiles" })
        migrationBuilder.Sql($@"
            UPDATE {tbl}
            SET BestTimeStart = CONVERT(time, JSON_VALUE(BestTimeWindowsJson, '$[0].start')),
                BestTimeEnd   = CONVERT(time, JSON_VALUE(BestTimeWindowsJson, '$[0].end'))
            WHERE ISJSON(BestTimeWindowsJson) = 1 AND JSON_VALUE(BestTimeWindowsJson, '$[0].start') IS NOT NULL;");
    migrationBuilder.DropColumn(name: "BestTimeWindowsJson", table: "TripPlaces");
    migrationBuilder.DropColumn(name: "BestTimeWindowsJson", table: "PlaceProfiles");
}
```

Confirm the model snapshot (`AppDbContextModelSnapshot.cs`) now shows `BestTimeWindowsJson` on both entities and no `BestTimeStart`/`BestTimeEnd` (EF regenerates it automatically with the migration).

- [ ] **Step 22: Verify the data-copy JSON deserializes.** Add a temporary throwaway assertion OR reason it through: `JsonSerializer.Deserialize<List<BestTimeWindow>>("[{\"start\":\"06:00:00\",\"end\":\"09:00:00\",\"note\":null}]", new JsonSerializerOptions(JsonSerializerDefaults.Web))` yields one window `06:00–09:00`. (System.Text.Json parses `TimeOnly` from `"HH:mm:ss"` and matches camelCase keys case-insensitively.) This is the exact shape the migration SQL emits. Do **not** apply to prod here — that is the manual rollout step.

- [ ] **Step 23: Build + run the full backend suite**

Run:
```bash
cd backend && dotnet build && dotnet test
```
Expected: build succeeds; all four test projects (`Application.UnitTests`, `Infrastructure.IntegrationTests`, `McpServer.UnitTests`, `WebApi.UnitTests`) green. Fix any remaining `BestTimeStart`/`BestTimeEnd`/`SetBestTime` references the compiler flags.

- [ ] **Step 24: Commit**

```bash
git add backend/src/MenuNest.Domain/ValueObjects/BestTimeWindow.cs \
  backend/src/MenuNest.Domain/Entities/TripPlace.cs backend/src/MenuNest.Domain/Entities/PlaceProfile.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Configurations/PlaceProfileConfiguration.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Migrations \
  backend/src/MenuNest.Application/UseCases/Trips backend/src/MenuNest.Application/UseCases/Places \
  backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
  backend/src/MenuNest.McpServer/Tools/TripTools.cs \
  backend/tests/MenuNest.Application.UnitTests backend/tests/MenuNest.McpServer.UnitTests
git commit -m "feat(trips): best-time becomes a per-Place window list — backend (#38)"
```

---

## Task 2: Frontend — pure `resolveBestTime` resolver + the shared type

The resolver is new, pure, and unit-tested; adding the `BestTimeWindow` interface to `api.ts` is additive (nothing consumes it yet), so this whole task is green on its own and independently reviewable.

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (add the `BestTimeWindow` interface only)
- Create: `frontend/src/pages/trips/lib/bestTime.ts`
- Test: `frontend/src/pages/trips/lib/bestTime.test.ts`

**Interfaces:**
- Produces: `interface BestTimeWindow { start: string; end: string; note: string | null }`; `resolveBestTime(windows: BestTimeWindow[] | undefined, arrivalMin: number): OffWindow | null` where `OffWindow = { nearest: BestTimeWindow; dir: 'before' | 'after'; upcoming: BestTimeWindow | null }`.
- Consumed by: Task 3 (`useSchedule.offWindowFlag`, `discoverFilter`).

### Step-by-step

- [ ] **Step 1: Add the shared interface** — `api.ts`, after `ReviewLink` (line 502) / near `SeasonPeriod`:

```ts
export interface BestTimeWindow {
    start: string
    end: string
    note: string | null
}
```

- [ ] **Step 2: Write the failing resolver test** — `frontend/src/pages/trips/lib/bestTime.test.ts`

```ts
import {describe, it, expect} from 'vitest'
import {resolveBestTime} from './bestTime'
import type {BestTimeWindow} from '../../../shared/api/api'

const w = (start: string, end: string, note: string | null = null): BestTimeWindow => ({start, end, note})
const morning = w('06:00:00', '09:00:00', 'แดดร่ม')
const evening = w('17:00:00', '19:00:00', 'แดดร่ม')

describe('resolveBestTime', () => {
  it('returns null when no windows', () => {
    expect(resolveBestTime([], 8 * 60)).toBeNull()
    expect(resolveBestTime(undefined, 8 * 60)).toBeNull()
  })
  it('returns null when arrival is inside any window (bounds inclusive)', () => {
    expect(resolveBestTime([morning, evening], 7 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 6 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 9 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 18 * 60)).toBeNull()
  })
  it('between windows: nearest is the missed morning (dir after), upcoming is the evening', () => {
    const off = resolveBestTime([morning, evening], 12 * 60 + 30)!
    expect(off.nearest).toBe(morning)
    expect(off.dir).toBe('after')
    expect(off.upcoming).toBe(evening)
  })
  it('before all windows: nearest = first, dir before, upcoming = first', () => {
    const off = resolveBestTime([morning, evening], 5 * 60)!
    expect(off.nearest).toBe(morning)
    expect(off.dir).toBe('before')
    expect(off.upcoming).toBe(morning)
  })
  it('after all windows: dir after, no upcoming', () => {
    const off = resolveBestTime([morning, evening], 21 * 60)!
    expect(off.nearest).toBe(evening)
    expect(off.dir).toBe('after')
    expect(off.upcoming).toBeNull()
  })
})
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/bestTime.test.ts`
Expected: FAIL — `resolveBestTime` not defined.

- [ ] **Step 4: Implement the resolver** — `frontend/src/pages/trips/lib/bestTime.ts`

```ts
import type {BestTimeWindow} from '../../../shared/api/api'

export interface OffWindow {
  nearest: BestTimeWindow
  dir: 'before' | 'after'
  upcoming: BestTimeWindow | null
}

const toMin = (hms: string): number => {
  const [h, m] = hms.slice(0, 5).split(':').map(Number)
  return h * 60 + m
}

/**
 * null when `arrivalMin` is inside ANY window (bounds inclusive). Otherwise the nearest
 * window (smallest time gap), the direction relative to it, and the next window that starts
 * after arrival (if any) — the basis for the off-window Timing flag (ADR-127).
 */
export function resolveBestTime(windows: BestTimeWindow[] | undefined, arrivalMin: number): OffWindow | null {
  const list = windows ?? []
  if (list.length === 0) return null
  for (const win of list) {
    if (arrivalMin >= toMin(win.start) && arrivalMin <= toMin(win.end)) return null
  }
  let nearest = list[0]
  let bestGap = Infinity
  for (const win of list) {
    const s = toMin(win.start)
    const e = toMin(win.end)
    const gap = arrivalMin < s ? s - arrivalMin : arrivalMin - e
    if (gap < bestGap) {
      bestGap = gap
      nearest = win
    }
  }
  const dir: 'before' | 'after' = arrivalMin < toMin(nearest.start) ? 'before' : 'after'
  const upcoming =
    list
      .filter((win) => toMin(win.start) > arrivalMin)
      .sort((a, b) => toMin(a.start) - toMin(b.start))[0] ?? null
  return {nearest, dir, upcoming}
}
```

- [ ] **Step 5: Run the test to verify it passes** — `cd frontend && npx vitest run src/pages/trips/lib/bestTime.test.ts` → PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/lib/bestTime.ts frontend/src/pages/trips/lib/bestTime.test.ts
git commit -m "feat(trips): pure resolveBestTime for multi-window best-time (#38)"
```

---

## Task 3: Frontend — wire the window list through types, schedule, discover, and UI (atomic + closes #38)

Swapping `bestTimeStart`/`bestTimeEnd` → `bestTimeWindows` on `TripPlaceDto`/`DiscoverPlaceDto` breaks every consumer's `tsc`, so the type change, the schedule/flag logic, the discover filter, the new editor + its wiring, the detail-sheet display, and the affected tests all land in one green commit.

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (`TripPlaceDto`, `DiscoverPlaceDto`, `addTripPlace` Omit, `updateTripPlace` args)
- Modify: `frontend/src/pages/trips/hooks/useSchedule.ts` (`TimingFlag` fields + `offWindowFlag`)
- Modify: `frontend/src/pages/trips/timingFlag.ts` (off-window copy)
- Modify: `frontend/src/pages/discover/lib/discoverFilter.ts` (`bestTimeMatch` any-window)
- Create: `frontend/src/pages/trips/components/BestTimeEditor.tsx`
- Delete: `frontend/src/pages/trips/components/BestTimeBar.tsx`
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx`, `PlaceEditorDialog.tsx`, `StopDetailSheet.tsx`
- Test (modify): `frontend/src/pages/trips/hooks/useSchedule.test.ts`, `frontend/src/pages/trips/timingFlag.test.ts`

**Interfaces:**
- Consumes: `BestTimeWindow`, `resolveBestTime` (Task 2).
- Produces: `TripPlaceDto.bestTimeWindows: BestTimeWindow[]`; `<BestTimeEditor windows onChange />`.

### Step-by-step

- [ ] **Step 1: Swap the API types** — `api.ts`:
  - `TripPlaceDto` (line 521): remove `bestTimeStart: string | null; bestTimeEnd: string | null;`; add `bestTimeWindows: BestTimeWindow[]` (e.g. after `seasonPeriods`).
  - `DiscoverPlaceDto` (lines 544-545): remove `bestTimeStart`/`bestTimeEnd`; add `bestTimeWindows: BestTimeWindow[]`.
  - `addTripPlace` mutation `Omit` (line 1377): change `'bestTimeStart' | 'bestTimeEnd'` → `'bestTimeWindows'`.
  - `updateTripPlace` mutation arg (line 1381): remove `bestTimeStart?: string | null; bestTimeEnd?: string | null;`; add `bestTimeWindows: BestTimeWindow[]`.

- [ ] **Step 2: Update the off-window flag** — `useSchedule.ts`:
  - Add to `TimingFlag` (after `windowDir`, line 22): `upcomingStart?: string` and `upcomingEnd?: string` (`'HH:MM'`, off-window).
  - Add import at top: `import {resolveBestTime} from '../lib/bestTime'`.
  - Replace `offWindowFlag` (lines 100-113):

```ts
/** Off-window when arrival is outside ALL of the place's best-time windows; references the nearest window (ADR-127). */
export function offWindowFlag(place: TripPlaceDto, arrival: string): TimingFlag | null {
  const off = resolveBestTime(place.bestTimeWindows, toMin(arrival))
  if (!off) return null
  return {
    reason: 'off-window',
    severity: 'suggestion',
    bestStart: off.nearest.start.slice(0, 5),
    bestEnd: off.nearest.end.slice(0, 5),
    windowDir: off.dir,
    upcomingStart: off.upcoming ? off.upcoming.start.slice(0, 5) : undefined,
    upcomingEnd: off.upcoming ? off.upcoming.end.slice(0, 5) : undefined,
  }
}
```

- [ ] **Step 3: Update the flag copy** — `timingFlag.ts`, replace the `off-window` case (lines 11-15):

```ts
    case 'off-window':
      return {
        reasonLine: `${flag.windowDir === 'before' ? 'ไปถึงก่อนช่วงแนะนำ' : 'ไปถึงหลังช่วงแนะนำ'} · ช่วงเหมาะ ${flag.bestStart}–${flag.bestEnd}`,
        fixLine: flag.upcomingStart
          ? `รอช่วง ${flag.upcomingStart}–${flag.upcomingEnd}`
          : 'เลื่อนสตอปนี้ให้เร็วขึ้น',
      }
```

- [ ] **Step 4: Update Discover** — `discoverFilter.ts`:
  - Import: `import type {BestTimeWindow, DiscoverPlaceDto, PlaceCategory} from '../../../shared/api/api'`.
  - Replace `bestTimeMatch` (lines 42-48):

```ts
/** now inside ANY window? [start, end); null when there are no windows. */
function bestTimeMatch(windows: BestTimeWindow[] | undefined, now: Date): boolean | null {
  const list = windows ?? []
  if (list.length === 0) return null
  const cur = now.getHours() * 60 + now.getMinutes()
  return list.some((w) => {
    const s = hmsToMinutes(w.start)
    const e = hmsToMinutes(w.end)
    return s != null && e != null && cur >= s && cur < e
  })
}
```

  - In `computePlaceView` (line 60): `bestTimeMatch: bestTimeMatch(p.bestTimeWindows, input.now),`.

- [ ] **Step 5: Create the editor** — `frontend/src/pages/trips/components/BestTimeEditor.tsx` (reuses existing `.se-sec`/`.se-time-*`/`.sp-*` styles; no new CSS needed):

```tsx
import {useState} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import type {BestTimeWindow} from '../../../shared/api/api'
import {hmsToDate, dateToHms} from '../utils/time'

const MAX_WINDOWS = 6
type Draft = {start: string | null; end: string | null; note: string}

export function BestTimeEditor({
  windows,
  onChange,
}: {
  windows: BestTimeWindow[]
  onChange: (windows: BestTimeWindow[]) => void
}) {
  const [draft, setDraft] = useState<Draft | null>(null)
  const fmt = (hms: string) => hms.slice(0, 5)
  const draftValid = !!draft && !!draft.start && !!draft.end && draft.end > draft.start

  const saveDraft = () => {
    if (!draft || !draft.start || !draft.end || draft.end <= draft.start) return
    onChange([...windows, {start: draft.start, end: draft.end, note: draft.note.trim() || null}])
    setDraft(null)
  }

  return (
    <section className="se-sec se-best">
      <div className="se-sec-head">
        <span className="se-ico">🕐</span>ช่วงเวลาที่ดี
        <span className="se-pill">หลายช่วงได้</span>
      </div>
      <p className="se-sub">ใส่ช่วงเวลาในวันที่เหมาะจะไป — ได้หลายช่วง แต่ละช่วงใส่เหตุผลได้</p>

      <ul className="season-rows">
        {windows.map((w, i) => (
          <li className="sp-row good" key={i}>
            <span className="sp-range">{fmt(w.start)}–{fmt(w.end)}</span>
            {w.note && <span className="sp-note">{w.note}</span>}
            <button type="button" className="sp-del" aria-label="ลบช่วง" onClick={() => onChange(windows.filter((_, j) => j !== i))}>✕</button>
          </li>
        ))}
      </ul>

      {draft ? (
        <div className="sp-draft">
          <div className="se-time-grid">
            <div className="se-time-card">
              <span className="se-time-lab">เริ่ม</span>
              <TimePicker value={hmsToDate(draft.start)} onChange={(e: TimePickerChangeEvent) => setDraft({...draft, start: dateToHms(e.value)})} format="HH:mm" step={15} placeholder="--:--" />
            </div>
            <span className="se-time-dash">–</span>
            <div className="se-time-card">
              <span className="se-time-lab">สิ้นสุด</span>
              <TimePicker value={hmsToDate(draft.end)} onChange={(e: TimePickerChangeEvent) => setDraft({...draft, end: dateToHms(e.value)})} format="HH:mm" step={15} placeholder="--:--" />
            </div>
          </div>
          <input className="sp-note-input" placeholder="เหตุผล (ไม่บังคับ)" value={draft.note} onChange={(e) => setDraft({...draft, note: e.target.value})} />
          <div className="sp-draft-foot">
            <button type="button" className="sp-cancel" onClick={() => setDraft(null)}>ยกเลิก</button>
            <button type="button" className="sp-save" disabled={!draftValid} onClick={saveDraft}>เพิ่มช่วง</button>
          </div>
          <p className="sp-hint">เวลาสิ้นสุดต้องหลังเวลาเริ่ม</p>
        </div>
      ) : (
        windows.length < MAX_WINDOWS && (
          <button type="button" className="sp-add" onClick={() => setDraft({start: null, end: null, note: ''})}>+ เพิ่มช่วง</button>
        )
      )}
    </section>
  )
}
```

- [ ] **Step 6: Wire into `StopEditorDialog.tsx`:**
  - Replace the `BestTimeBar` import (line 18) with `import {BestTimeEditor} from './BestTimeEditor'` and add `type BestTimeWindow` to the `../../../shared/api/api` import.
  - Replace the `bestStart`/`bestEnd` state (lines 51-52) with `const [bestTimeWindows, setBestTimeWindows] = useState<BestTimeWindow[]>(place?.bestTimeWindows ?? [])`.
  - In `save` (line 89): replace `const bestTimeChanged = bestStart !== place?.bestTimeStart || bestEnd !== place?.bestTimeEnd` with `const bestTimeChanged = JSON.stringify(bestTimeWindows) !== JSON.stringify(place?.bestTimeWindows ?? [])`.
  - In the `updatePlace({...})` call (lines 102-103): replace `bestTimeStart: bestStart, bestTimeEnd: bestEnd,` with `bestTimeWindows,`.
  - Replace the `<BestTimeBar .../>` element (lines 155-162) with `<BestTimeEditor windows={bestTimeWindows} onChange={setBestTimeWindows} />`.

- [ ] **Step 7: Wire into `PlaceEditorDialog.tsx`:**
  - Replace the `BestTimeBar` import (line 11) with `import {BestTimeEditor} from './BestTimeEditor'` and add `type BestTimeWindow` to the api import.
  - Replace `bestStart`/`bestEnd` state (lines 26-27) with `const [bestTimeWindows, setBestTimeWindows] = useState<BestTimeWindow[]>(place.bestTimeWindows ?? [])`.
  - In `persist` (lines 48-49): replace `bestTimeStart: bestStart, bestTimeEnd: bestEnd,` with `bestTimeWindows,`.
  - Replace the `<BestTimeBar .../>` element (line 130) with `<BestTimeEditor windows={bestTimeWindows} onChange={(w) => { setBestTimeWindows(w); setPushed(false) }} />`.

- [ ] **Step 8: Add the detail-sheet display** — `StopDetailSheet.tsx`:
  - After `const seasonPeriods = place.seasonPeriods ?? []` (line 64) add `const bestTimeWindows = place.bestTimeWindows ?? []`.
  - Insert a section (mirror the `sd-seasons` block) just before or after it in the JSX, e.g. after the `{seasonPeriods.length > 0 && (...)}` block (line 180):

```tsx
        {bestTimeWindows.length > 0 && (
          <div className="sd-seasons">
            <div className="sd-sec-lab">ช่วงเวลาที่ดี</div>
            <ul className="season-rows">
              {bestTimeWindows.map((w, i) => (
                <li key={i} className="sp-row good">
                  <span className="sp-range">{w.start.slice(0, 5)}–{w.end.slice(0, 5)}</span>
                  {w.note && <span className="sp-note">{w.note}</span>}
                </li>
              ))}
            </ul>
          </div>
        )}
```

- [ ] **Step 9: Delete `BestTimeBar.tsx`** — `git rm frontend/src/pages/trips/components/BestTimeBar.tsx` (grep `BestTimeBar` first to confirm no other references remain after Steps 6-7).

- [ ] **Step 10: Update the frontend tests:**
  - `useSchedule.test.ts`: line 111 `mkPlace` default — replace `bestTimeStart: null, bestTimeEnd: null,` with `bestTimeWindows: [],`. Replace the `offWindowFlag` cases (lines 129-147) to build windows and assert the new shape:

```ts
describe('offWindowFlag', () => {
  it('inside any window → null', () => {
    const p = mkPlace({bestTimeWindows: [{start: '08:00:00', end: '10:00:00', note: null}]})
    expect(offWindowFlag(p, '09:00')).toBeNull()
    expect(offWindowFlag(p, '08:00')).toBeNull()
    expect(offWindowFlag(p, '10:00')).toBeNull()
  })
  it('between windows → nearest missed window, dir after, upcoming next', () => {
    const p = mkPlace({bestTimeWindows: [
      {start: '06:00:00', end: '09:00:00', note: null},
      {start: '17:00:00', end: '19:00:00', note: null},
    ]})
    expect(offWindowFlag(p, '12:30')).toMatchObject({
      reason: 'off-window', severity: 'suggestion', windowDir: 'after',
      bestStart: '06:00', bestEnd: '09:00', upcomingStart: '17:00', upcomingEnd: '19:00',
    })
  })
  it('before all windows → windowDir before', () => {
    const p = mkPlace({bestTimeWindows: [{start: '17:30:00', end: '18:30:00', note: null}]})
    expect(offWindowFlag(p, '13:50')).toMatchObject({windowDir: 'before'})
  })
  it('no windows → null', () => {
    expect(offWindowFlag(mkPlace(), '13:50')).toBeNull()
  })
})
```

  and the `composeFlags` "closed outranks off-window" case (line 196): replace `bestTimeStart: '12:00:00', bestTimeEnd: '13:00:00',` with `bestTimeWindows: [{start: '12:00:00', end: '13:00:00', note: null}],`.
  - `timingFlag.test.ts` (lines 27-33): the two off-window flags still construct validly; add `upcomingStart`/`upcomingEnd` where a `รอช่วง` fix is asserted, and update any `fixLine` expectation to the new copy (`รอช่วง H–H` when `upcomingStart` is set, else `เลื่อนสตอปนี้ให้เร็วขึ้น`). Read the file's current assertions and align them to `flagText`'s new output.

- [ ] **Step 11: Run the full frontend gate**

Run:
```bash
cd frontend && npx tsc --noEmit && npm run build && npx vitest run
```
Expected: type-check clean, build succeeds, all vitest green.

- [ ] **Step 12: Interactive verification (required — gates are blind to rendering).** Run the app; open a trip → Stop editor and the คลังสถานที่ Place editor: add two windows (06:00–09:00 "แดดร่ม", 17:00–19:00) — confirm they list, delete works, the `+ เพิ่มช่วง` hides at 6, and end≤start blocks save. Tap a stop card to open the detail sheet: confirm the "ช่วงเวลาที่ดี" list and, for a stop whose arrival lands between the windows, the off-window flag reads "ไปถึงหลังช่วงแนะนำ · ช่วงเหมาะ 06:00–09:00 — รอช่วง 17:00–19:00". Diff against the mock (Screens → `issue-38-best-time-windows`).

- [ ] **Step 13: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/hooks/useSchedule.ts \
  frontend/src/pages/trips/timingFlag.ts frontend/src/pages/discover/lib/discoverFilter.ts \
  frontend/src/pages/trips/components/BestTimeEditor.tsx \
  frontend/src/pages/trips/components/StopEditorDialog.tsx frontend/src/pages/trips/components/PlaceEditorDialog.tsx \
  frontend/src/pages/trips/components/StopDetailSheet.tsx \
  frontend/src/pages/trips/hooks/useSchedule.test.ts frontend/src/pages/trips/timingFlag.test.ts
git rm frontend/src/pages/trips/components/BestTimeBar.tsx
git commit -m "feat(trips): multi-window best-time editor, display, and off-window flag (closes #38)"
```

---

## Migration & Rollout (manual — do NOT skip)

After both commits merge to local `main` and interactive verification passes, apply the migration to prod **by hand** (per `CLAUDE.md`) before pushing, since prod deploys on push and the deployed code will expect `BestTimeWindowsJson`:

1. Preview the SQL: `cd backend && dotnet ef migrations script --idempotent --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi` — confirm the add-columns, the two `UPDATE ... BestTimeWindowsJson` copies, and the four drop-columns are all present and correctly ordered (copy **before** drop).
2. If the prod SQL server rejects your IP, add a temporary firewall rule (`az sql server firewall-rule create ... tmp-apply`), apply, then remove it.
3. Apply with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update ...` (personal `az` session as SQL Entra admin, per `CLAUDE.md`).
4. Spot-check: a Place that had a best-time window now shows one entry in `BestTimeWindowsJson`; the scalar columns are gone.
5. `git push main HEAD:main` (closes #38 on merge).

## Self-Review

- **Spec coverage:** §3 VO/entities → Task 1 Steps 2-5; §4 persistence + InMemory → Steps 6-8; §4.1 migration → Step 21; §5 DTO/REST/MCP blast radius → Steps 9-19; §6 off-window resolver + flag → Task 2 + Task 3 Steps 2-3; §7 validation → Step 13; §8 Discover → Task 3 Step 4; §9 lifecycle (push-only, seed) → Step 14 (WriteThrough unchanged); §10 UI (editor/sheet/card-flag-only) → Task 3 Steps 5-9; §11 testing → tests throughout + Step 12; §12 rollout → Migration & Rollout. All covered.
- **Placeholder scan:** every code step carries full code; the one grep-guided step (Task 1 Step 20) lists each file:line with the exact replacement — no "handle the rest" left open.
- **Type consistency:** `BestTimeWindow`/`BestTimeWindowDto`/`bestTimeWindows`/`SetBestTimeWindows`/`resolveBestTime`/`OffWindow`/`upcomingStart`/`upcomingEnd` are used identically across all tasks; `TripPlaceDto` positional order (BestTimeWindows appended last) matches `ToDto` (Step 10) and the MCP test literal (Step 19).
