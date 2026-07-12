# Trip Stop "Review link" (TikTok review) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a Trip owner attach a list of external review links (`{url, label?}`, framed around TikTok) to each Place, surfaced on the itinerary Stop card as a click-to-watch affordance and edited in the Stop editor.

**Architecture:** A `ReviewLink` value object held as a list on the `TripPlace` aggregate, persisted as one JSON `nvarchar(max)` column via an EF `ValueConverter` (mirrors `OpeningHoursJson`). It flows out through the existing `TripPlaceDto` (already on the card) and back through the existing **full-replace** `updateTripPlace` PUT — which also gains the field on the `update_trip_place` MCP tool. The card shows a trailing icon (1 link opens directly; ≥2 shows a count badge + popover); the editor gets a "ลิงก์รีวิว" section. No new endpoint, no non-invalidating mutation.

**Tech Stack:** .NET 10 / EF Core 10 / Mediator source-gen / FluentValidation / xUnit + FluentAssertions + `SqliteAppDbContext` (backend); React + RTK Query + Syncfusion + Vitest (node env, pure-logic only — no RTL) (frontend); ModelContextProtocol C# SDK (MCP).

## Global Constraints

- **Icons:** Syncfusion react-icons or custom inline-SVG components — **never emoji** glyphs. The review icon is an SVG component (`ReviewIcon`), like `NavIcon`.
- **Commit → issue reference (CLAUDE.md):** every commit subject references tracking issue #33 — `(closes #33)` on the final code commit (Task 9), `(#33)` on the rest.
- **Staging:** always `git add <explicit paths>` — never `git add -A`/`.`. Never stage `daily-state.md` (tracked, dirty) or `AGENTS.md` (untracked).
- **Pre-commit hook** runs the FULL suite (backend `dotnet build`+`dotnet test` Release, frontend `tsc -b`+`npm run build`, ~40s) on every commit. Expect the wait; never `--no-verify`.
- **EF migrations are applied to prod MANUALLY** (§Task 10) — neither the app nor CD runs `Database.Migrate()`.
- **`reviewLinks` is a FULL-REPLACE field:** the command parameter is **required (no default)** so the compiler forces every construction site (controller + MCP) to send the current list — omitting it would silently wipe reviews.
- **Validation limits:** each URL absolute `http`/`https`, ≤ 500 chars; label optional/trimmed, ≤ 80; ≤ 10 links per place. Any host allowed.
- **Frontend tests run in vitest `environment: 'node'`** — pure logic only. DOM wiring (card/editor) is verified by `tsc -b` + `npm run build` + the interactive checklist in Task 10, not by component tests.

---

### Task 1: `ReviewLink` value object (domain)

**Files:**
- Create: `backend/src/MenuNest.Domain/ValueObjects/ReviewLink.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/ReviewLinkTests.cs`

**Interfaces:**
- Produces: `MenuNest.Domain.ValueObjects.ReviewLink` — positional record `ReviewLink(string Url, string? Label)` with static `ReviewLink Create(string? url, string? label)` (throws `DomainException` on invalid URL / over-length).

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/ReviewLinkTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class ReviewLinkTests
{
    [Fact]
    public void Create_keeps_a_valid_https_url_and_trimmed_label()
    {
        var link = ReviewLink.Create("  https://www.tiktok.com/@u/video/1  ", "  @foodie  ");
        link.Url.Should().Be("https://www.tiktok.com/@u/video/1");
        link.Label.Should().Be("@foodie");
    }

    [Fact]
    public void Create_nulls_a_blank_label()
    {
        ReviewLink.Create("https://x.com/v", "   ").Label.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://x.com/v")]
    [InlineData("/relative/path")]
    public void Create_rejects_non_http_urls(string url)
    {
        var act = () => ReviewLink.Create(url, null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_over_length_url()
    {
        var act = () => ReviewLink.Create("https://x.com/" + new string('a', 500), null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_over_length_label()
    {
        var act = () => ReviewLink.Create("https://x.com/v", new string('a', 81));
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ReviewLinkTests`
Expected: FAIL — `ReviewLink` does not exist (compile error).

- [ ] **Step 3: Write the value object**

Create `backend/src/MenuNest.Domain/ValueObjects/ReviewLink.cs`:

```csharp
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place link to an external short-video review (framed around TikTok, any http(s) URL).
/// Positional record with a public ctor so System.Text.Json can round-trip it from the JSON
/// column; user input is validated through <see cref="Create"/>.
/// </summary>
public sealed record ReviewLink(string Url, string? Label)
{
    public static ReviewLink Create(string? url, string? label)
    {
        var u = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new DomainException("Review link must be a valid http(s) URL.");
        if (u.Length > 500) throw new DomainException("Review link URL is too long (max 500).");
        var l = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (l is { Length: > 80 }) throw new DomainException("Review link label is too long (max 80).");
        return new ReviewLink(u, l);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter ReviewLinkTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/ValueObjects/ReviewLink.cs backend/tests/MenuNest.Application.UnitTests/Trips/Domain/ReviewLinkTests.cs
git commit -m "feat(trips): ReviewLink value object with http(s) URL validation (#33)"
```

---

### Task 2: `TripPlace.ReviewLinks` + `SetReviewLinks` (domain)

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/TripPlace.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs`

**Interfaces:**
- Consumes: `ReviewLink` (Task 1).
- Produces: `TripPlace.ReviewLinks` (`IReadOnlyList<ReviewLink>`) and `TripPlace.SetReviewLinks(IEnumerable<ReviewLink>)` (full-replace; throws `DomainException` when > 10).

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs` (add `using MenuNest.Domain.ValueObjects;` at the top if absent):

```csharp
    [Fact]
    public void New_place_has_no_review_links()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public void SetReviewLinks_replaces_the_whole_list()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", "one") });
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/2", null), ReviewLink.Create("https://x.com/3", null) });
        p.ReviewLinks.Select(r => r.Url).Should().Equal("https://x.com/2", "https://x.com/3");
    }

    [Fact]
    public void SetReviewLinks_with_empty_clears_the_list()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", null) });
        p.SetReviewLinks(Array.Empty<ReviewLink>());
        p.ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public void SetReviewLinks_rejects_more_than_ten()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        var many = Enumerable.Range(0, 11).Select(i => ReviewLink.Create($"https://x.com/{i}", null));
        var act = () => p.SetReviewLinks(many);
        act.Should().Throw<DomainException>();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceTests`
Expected: FAIL — `ReviewLinks` / `SetReviewLinks` do not exist (compile error).

- [ ] **Step 3: Add the list + mutator to `TripPlace`**

In `backend/src/MenuNest.Domain/Entities/TripPlace.cs`: add `using MenuNest.Domain.ValueObjects;` at the top. Add the backing field + property after the `Notes` property (line 27):

```csharp
    private readonly List<ReviewLink> _reviewLinks = new();
    public IReadOnlyList<ReviewLink> ReviewLinks => _reviewLinks;
```

Add the mutator after `SetBestTime(...)`:

```csharp
    public void SetReviewLinks(IEnumerable<ReviewLink> links)
    {
        var list = (links ?? Enumerable.Empty<ReviewLink>()).ToList();
        if (list.Count > 10) throw new DomainException("A place can have at most 10 review links.");
        _reviewLinks.Clear();
        _reviewLinks.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
```

(Add `using System.Linq;` if the file does not already have it — most files use implicit usings, so this is usually unnecessary.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceTests`
Expected: PASS (existing TripPlace tests + 4 new).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/TripPlace.cs backend/tests/MenuNest.Application.UnitTests/Trips/Domain/TripPlaceTests.cs
git commit -m "feat(trips): TripPlace.ReviewLinks list + SetReviewLinks full-replace mutator (#33)"
```

---

### Task 3: EF persistence (JSON `ValueConverter`) + migration

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs`
- Create (generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddTripPlaceReviewLinks.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceReviewLinksPersistenceTests.cs`

**Interfaces:**
- Consumes: `TripPlace.ReviewLinks` / `SetReviewLinks` (Task 2); `HandlerTestFixture` (`fx.Db` = `SqliteAppDbContext`).
- Produces: `TripPlaces.ReviewLinksJson` `nvarchar(max)` column; a round-trippable `ReviewLinks` mapping.

- [ ] **Step 1: Write the failing round-trip test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceReviewLinksPersistenceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class TripPlaceReviewLinksPersistenceTests
{
    [Fact]
    public async Task ReviewLinks_round_trip_through_the_json_column()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        place.SetReviewLinks(new[]
        {
            ReviewLink.Create("https://www.tiktok.com/@u/video/1", "@foodie"),
            ReviewLink.Create("https://youtu.be/abc", null),
        });
        fx.Db.TripPlaces.Add(place);
        await fx.Db.SaveChangesAsync();

        // fresh context to force a real read back from storage
        fx.Db.ChangeTracker.Clear();
        var read = await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id);

        read.ReviewLinks.Should().HaveCount(2);
        read.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@u/video/1");
        read.ReviewLinks[0].Label.Should().Be("@foodie");
        read.ReviewLinks[1].Label.Should().BeNull();
    }

    [Fact]
    public async Task Empty_review_links_round_trip_as_empty()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        (await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id))
            .ReviewLinks.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceReviewLinksPersistenceTests`
Expected: FAIL — EF cannot map `ReviewLinks` (no converter): model-building or query error.

- [ ] **Step 3: Configure the JSON `ValueConverter` mapping**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs`, add usings:

```csharp
using System.Text.Json;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
```

Inside `Configure(EntityTypeBuilder<TripPlace> b)`, after the `Notes` line, add:

```csharp
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reviewConverter = new ValueConverter<IReadOnlyList<ReviewLink>, string?>(
            v => v.Count == 0 ? null : JsonSerializer.Serialize(v, jsonOpts),
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
            .UsePropertyAccessMode(PropertyAccessMode.Field);
```

`.HasField("_reviewLinks")` binds the get-only `ReviewLinks` property to its backing field so EF
materializes into it. The converter's `ConvertFromProvider` returns a concrete `List<ReviewLink>`,
which the `List<ReviewLink> _reviewLinks` field accepts.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter TripPlaceReviewLinksPersistenceTests`
Expected: PASS (2 cases). (The `SqliteAppDbContext` builds the schema from the model, so no migration is needed for the test.)

- [ ] **Step 5: Generate the production migration**

Run: `cd backend && AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations add AddTripPlaceReviewLinks --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi`
Expected: a new migration whose `Up` is a single `AddColumn<string>(name: "ReviewLinksJson", table: "TripPlaces", type: "nvarchar(max)", nullable: true)` and `Down` a matching `DropColumn`. Open the generated file and confirm it contains exactly that (no unrelated model drift).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/TripPlaceConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/ backend/tests/MenuNest.Application.UnitTests/Trips/TripPlaceReviewLinksPersistenceTests.cs
git commit -m "feat(trips): persist TripPlace.ReviewLinks as JSON column + AddTripPlaceReviewLinks migration (#33)"
```

---

### Task 4: DTO — `ReviewLinkDto`, `TripPlaceDto.ReviewLinks`, `ToDto`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs` (`ToDto`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs`

**Interfaces:**
- Produces: `ReviewLinkDto(string Url, string? Label)`; `TripPlaceDto` gains trailing `IReadOnlyList<ReviewLinkDto> ReviewLinks`; `AddTripPlaceHandler.ToDto` maps it.

- [ ] **Step 1: Write the failing test**

Append to `backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs`:

```csharp
    [Fact]
    public void ToDto_maps_review_links()
    {
        var place = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.Eat);
        place.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", "one") });
        var dto = AddTripPlaceHandler.ToDto(place);
        dto.ReviewLinks.Should().ContainSingle();
        dto.ReviewLinks[0].Url.Should().Be("https://x.com/1");
        dto.ReviewLinks[0].Label.Should().Be("one");
    }
```

(Add `using MenuNest.Domain.ValueObjects;` to the test file if absent.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter AddTripPlaceHandlerTests`
Expected: FAIL — `TripPlaceDto.ReviewLinks` / `ReviewLinkDto` do not exist (compile error).

- [ ] **Step 3: Add the DTO field + mapping**

In `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` add the value record (near `TripPlaceDto`):

```csharp
public sealed record ReviewLinkDto(string Url, string? Label);
```

and append the trailing field to `TripPlaceDto`:

```csharp
public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks);
```

In `backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs`, update `ToDto` to pass the new trailing argument (append to the `new(...)` call):

```csharp
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList());
```

(Add `using MenuNest.Domain.ValueObjects;` to the handler file if the compiler asks.)

- [ ] **Step 4: Run the test + the full Application suite to verify green**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests`
Expected: PASS — the new test passes and no other `ToDto`/`TripPlaceDto` construction broke (there is one canonical `ToDto`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs backend/src/MenuNest.Application/UseCases/Trips/AddTripPlace/AddTripPlaceHandler.cs backend/tests/MenuNest.Application.UnitTests/Trips/AddTripPlaceHandlerTests.cs
git commit -m "feat(trips): ReviewLinkDto + TripPlaceDto.ReviewLinks + ToDto mapping (#33)"
```

---

### Task 5: Write path — command, validator, handler, controller, MCP tool

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (`UpdatePlaceBody` + construction at ~:70)
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (`update_trip_place` ~:92-104)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripPlaceHandlerTests.cs` (create if absent)

**Interfaces:**
- Consumes: `ReviewLinkDto` (Task 4), `ReviewLink.Create` (Task 1), `SetReviewLinks` (Task 2).
- Produces: `UpdateTripPlaceCommand` with a **required trailing** `IReadOnlyList<ReviewLinkDto> ReviewLinks`; validator + handler that apply it full-replace; `update_trip_place` MCP tool + `UpdatePlaceBody` carrying it.

- [ ] **Step 1: Write the failing handler tests**

Create (or append to) `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripPlaceHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class UpdateTripPlaceHandlerTests
{
    private static (Trip trip, TripPlace place) Seed(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        fx.Db.SaveChanges();
        return (trip, place);
    }

    private static UpdateTripPlaceHandler Handler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new UpdateTripPlaceValidator());

    private static UpdateTripPlaceCommand Cmd(Guid tripId, Guid placeId, IReadOnlyList<ReviewLinkDto> links) =>
        new(tripId, placeId, "A", PlaceCategory.Eat, null, null, null, null, null, links);

    [Fact]
    public async Task Sets_review_links_full_replace()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);

        await Handler(fx).Handle(Cmd(trip.Id, place.Id, new[]
        {
            new ReviewLinkDto("https://www.tiktok.com/@u/1", "one"),
            new ReviewLinkDto("https://youtu.be/x", null),
        }), CancellationToken.None);

        fx.Db.ChangeTracker.Clear();
        var read = await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id);
        read.ReviewLinks.Select(r => r.Url).Should().Equal("https://www.tiktok.com/@u/1", "https://youtu.be/x");
    }

    [Fact]
    public async Task Empty_list_clears_existing_review_links()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        await Handler(fx).Handle(Cmd(trip.Id, place.Id, new[] { new ReviewLinkDto("https://x.com/1", null) }), CancellationToken.None);
        fx.Db.ChangeTracker.Clear();

        await Handler(fx).Handle(Cmd(trip.Id, place.Id, Array.Empty<ReviewLinkDto>()), CancellationToken.None);
        fx.Db.ChangeTracker.Clear();

        (await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id)).ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_invalid_review_url()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        var act = () => Handler(fx).Handle(Cmd(trip.Id, place.Id, new[] { new ReviewLinkDto("not-a-url", null) }), CancellationToken.None);
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Rejects_more_than_ten_links()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        var links = Enumerable.Range(0, 11).Select(i => new ReviewLinkDto($"https://x.com/{i}", null)).ToArray();
        var act = () => Handler(fx).Handle(Cmd(trip.Id, place.Id, links), CancellationToken.None);
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter UpdateTripPlaceHandlerTests`
Expected: FAIL — `UpdateTripPlaceCommand` has no `ReviewLinks` parameter (compile error).

- [ ] **Step 3: Add `ReviewLinks` to the command (required trailing param)**

`backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed record UpdateTripPlaceCommand(
    Guid TripId, Guid PlaceId, string Name, PlaceCategory Category,
    string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    IReadOnlyList<ReviewLinkDto> ReviewLinks)
    : ICommand<TripPlaceDto>;
```

- [ ] **Step 4: Add validation rules**

`backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceValidator.cs`:

```csharp
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed class UpdateTripPlaceValidator : AbstractValidator<UpdateTripPlaceCommand>
{
    public UpdateTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ReviewLinks).Must(l => l.Count <= 10)
            .WithMessage("A place can have at most 10 review links.");
        RuleForEach(x => x.ReviewLinks).ChildRules(link =>
        {
            link.RuleFor(l => l.Url).NotEmpty().MaximumLength(500)
                .Must(BeHttpUrl).WithMessage("Review link must be a valid http(s) URL.");
            link.RuleFor(l => l.Label).MaximumLength(80);
        });
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
```

- [ ] **Step 5: Apply the links in the handler**

In `backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/UpdateTripPlaceHandler.cs`, add `using MenuNest.Domain.ValueObjects;`, and after the `place.SetBestTime(...)` line add:

```csharp
        place.SetReviewLinks(c.ReviewLinks.Select(r => ReviewLink.Create(r.Url, r.Label)));
```

- [ ] **Step 6: Thread it through the WebApi controller**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`: append the field to `UpdatePlaceBody`:

```csharp
public sealed record UpdatePlaceBody(
    string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    IReadOnlyList<ReviewLinkDto> ReviewLinks);
```

and pass it into the command in `UpdatePlace` (~:70):

```csharp
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeStart, b.BestTimeEnd, b.ReviewLinks), ct));
```

(Add `using MenuNest.Application.UseCases.Trips;` if `ReviewLinkDto` is not already in scope in this file.)

- [ ] **Step 7: Expose it on the MCP tool**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, update `update_trip_place`: add the parameter (before `CancellationToken ct`) and pass it into the command; extend the `[Description]` to mention it is full-replace too.

```csharp
    [McpServerTool, Description("Update a saved place's editable fields. FULL REPLACE of the listed fields: address, feeNote, notes, the best-visit window (bestTimeStart/bestTimeEnd), and reviewLinks are overwritten — omitting or passing an empty/null value CLEARS the stored value. To change just one field, pass the current values of the others (get them from list_trip_places).")]
    public async Task<TripPlaceDto> update_trip_place(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place name")] string name,
        [Description("Category: Stay, Eat, See, Cafe, Shop, or Other")] PlaceCategory category,
        [Description("Address (optional)")] string? address,
        [Description("Fee/ticket note (optional)")] string? feeNote,
        [Description("Free-form notes (optional)")] string? notes,
        [Description("Best-visit window start, HH:mm (optional)")] TimeOnly? bestTimeStart,
        [Description("Best-visit window end, HH:mm (optional)")] TimeOnly? bestTimeEnd,
        [Description("Review links (TikTok/YouTube/etc.), each {url,label?}; max 10; FULL REPLACE")] IReadOnlyList<ReviewLinkDto> reviewLinks,
        CancellationToken ct)
        => await mediator.Send(new UpdateTripPlaceCommand(
            tripId, placeId, name, category, address, feeNote, notes, bestTimeStart, bestTimeEnd, reviewLinks), ct);
```

(`ReviewLinkDto` is in `MenuNest.Application.UseCases.Trips`, already imported at the top of TripTools.cs via `using MenuNest.Application.UseCases.Trips.UpdateTripPlace;` and the DTO's namespace — add `using MenuNest.Application.UseCases.Trips;` if the compiler asks.)

- [ ] **Step 8: Run the handler tests + full backend build to verify green**

Run: `cd backend && dotnet build && dotnet test tests/MenuNest.Application.UnitTests --filter UpdateTripPlaceHandlerTests`
Expected: PASS (4 cases). The full `dotnet build` proves the required param compiled at **all** sites (controller + MCP + any test fixtures) — no silent-wipe call site remains.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/UpdateTripPlace/ backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/src/MenuNest.McpServer/Tools/TripTools.cs backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripPlaceHandlerTests.cs
git commit -m "feat(trips): reviewLinks on update_trip_place (full-replace) + validation + MCP tool (#33)"
```

---

### Task 6: Frontend API types + fixture

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/pages/trips/hooks/useSchedule.test.ts` (the `mkPlace` fixture, ~:109-112)
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx` (one-line pass-through — the only `updateTripPlace` caller; superseded by Task 9)

**Interfaces:**
- Produces: `ReviewLink` TS interface (`{url: string; label: string | null}`); `TripPlaceDto.reviewLinks: ReviewLink[]`; `updateTripPlace` arg type gains `reviewLinks: ReviewLink[]`.

> **Why the pass-through:** making `reviewLinks` a **required** mutation arg breaks `tsc -b` at the
> one existing caller (`StopEditorDialog.tsx:80`), which does not send it yet. Task 6 adds a minimal
> pass-through (`reviewLinks: place.reviewLinks ?? []`) so the build stays green; Task 9 replaces it
> with the edited drafts. (`useUpdateTripPlaceMutation` has exactly one call site — verified.)

- [ ] **Step 1: Add the TS type + DTO field + mutation arg**

In `frontend/src/shared/api/api.ts`:

Add near the other trip types:

```ts
export interface ReviewLink {
  url: string
  label: string | null
}
```

Append `reviewLinks` to the `TripPlaceDto` interface (:496):

```ts
export interface TripPlaceDto {
    id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number
    address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null
    bestTimeStart: string | null; bestTimeEnd: string | null; openingHoursJson: string | null
    feeNote: string | null; notes: string | null
    reviewLinks: ReviewLink[]
}
```

Add `reviewLinks: ReviewLink[]` to the `updateTripPlace` mutation's arg type (the generic on `build.mutation<TripPlaceDto, {...}>`):

```ts
        updateTripPlace: build.mutation<TripPlaceDto, {tripId: string; placeId: string; name: string; category: PlaceCategory; address?: string | null; feeNote?: string | null; notes?: string | null; bestTimeStart?: string | null; bestTimeEnd?: string | null; reviewLinks: ReviewLink[]}>({
```

(Leave `query` and `invalidatesTags` unchanged — `reviewLinks` is already spread into the body via `...b`.)

- [ ] **Step 2: Fix the one `TripPlaceDto` fixture**

In `frontend/src/pages/trips/hooks/useSchedule.test.ts`, add `reviewLinks: []` to the `mkPlace` base object (~:112, after `notes: null,`):

```ts
  openingHoursJson: null, feeNote: null, notes: null, reviewLinks: [], ...over,
```

- [ ] **Step 3: Keep the one existing `updateTripPlace` caller compiling**

In `frontend/src/pages/trips/components/StopEditorDialog.tsx`, the `save()` function calls
`updatePlace({...})` (~:80) inside an `if (place && ...)` block. Add `reviewLinks: place.reviewLinks ?? []`
to that object so the now-required arg is satisfied:

```tsx
        await updatePlace({
          tripId,
          placeId: place.id,
          name: place.name,
          category: place.category,
          address: place.address,
          feeNote: place.feeNote,
          notes: place.notes,
          bestTimeStart: bestStart,
          bestTimeEnd: bestEnd,
          reviewLinks: place.reviewLinks ?? [],
        }).unwrap()
```

(This is a temporary pass-through — Task 9 replaces `place.reviewLinks ?? []` with the edited drafts
and broadens the save condition.)

- [ ] **Step 4: Run the typecheck to verify green**

Run: `cd frontend && npx tsc -b`
Expected: PASS — `mkPlace` and the `StopEditorDialog` caller are the only sites the required field touches (the server supplies `TripPlaceDto`s at runtime everywhere else).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/hooks/useSchedule.test.ts frontend/src/pages/trips/components/StopEditorDialog.tsx
git commit -m "feat(trips): ReviewLink API type + TripPlaceDto.reviewLinks (#33)"
```

---

### Task 7: `reviewLinks` client helper lib (pure, tested)

**Files:**
- Create: `frontend/src/pages/trips/lib/reviewLinks.ts`
- Test: `frontend/src/pages/trips/lib/reviewLinks.test.ts`

**Interfaces:**
- Consumes: `ReviewLink` (Task 6).
- Produces: `MAX_REVIEW_LINKS`, `isValidReviewUrl(url)`, `reviewHost(url)`, `reviewLabel(link, index)`, `type ReviewDraft = {url: string; label: string}`, `sanitizeReviewDrafts(drafts)`, `draftsValid(drafts)`.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/pages/trips/lib/reviewLinks.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import {
  MAX_REVIEW_LINKS,
  isValidReviewUrl,
  reviewHost,
  reviewLabel,
  sanitizeReviewDrafts,
  draftsValid,
} from './reviewLinks'

describe('reviewLinks', () => {
  it('accepts http(s) URLs and rejects others', () => {
    expect(isValidReviewUrl('https://www.tiktok.com/@u/video/1')).toBe(true)
    expect(isValidReviewUrl('http://x.com')).toBe(true)
    expect(isValidReviewUrl('ftp://x.com')).toBe(false)
    expect(isValidReviewUrl('not a url')).toBe(false)
    expect(isValidReviewUrl('')).toBe(false)
  })

  it('extracts host without www', () => {
    expect(reviewHost('https://www.tiktok.com/@u/1')).toBe('tiktok.com')
    expect(reviewHost('https://youtu.be/x')).toBe('youtu.be')
    expect(reviewHost('garbage')).toBe('')
  })

  it('falls back to a numbered label when blank', () => {
    expect(reviewLabel({url: 'https://x.com', label: '@foodie'}, 0)).toBe('@foodie')
    expect(reviewLabel({url: 'https://x.com', label: null}, 0)).toBe('ดูรีวิว 1')
    expect(reviewLabel({url: 'https://x.com', label: '   '}, 1)).toBe('ดูรีวิว 2')
  })

  it('sanitize trims, drops blank-url rows, nulls blank labels', () => {
    const out = sanitizeReviewDrafts([
      {url: '  https://x.com/1 ', label: '  one '},
      {url: '   ', label: 'ignored'},
      {url: 'https://x.com/2', label: ''},
    ])
    expect(out).toEqual([
      {url: 'https://x.com/1', label: 'one'},
      {url: 'https://x.com/2', label: null},
    ])
  })

  it('draftsValid rejects invalid urls and over-cap counts', () => {
    expect(draftsValid([{url: 'https://x.com', label: ''}])).toBe(true)
    expect(draftsValid([{url: '', label: ''}])).toBe(true) // blank rows are dropped, not invalid
    expect(draftsValid([{url: 'nope', label: ''}])).toBe(false)
    const eleven = Array.from({length: MAX_REVIEW_LINKS + 1}, (_, i) => ({url: `https://x.com/${i}`, label: ''}))
    expect(draftsValid(eleven)).toBe(false)
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/pages/trips/lib/reviewLinks.test.ts`
Expected: FAIL — `./reviewLinks` module not found.

- [ ] **Step 3: Write the helper**

Create `frontend/src/pages/trips/lib/reviewLinks.ts`:

```ts
import type {ReviewLink} from '../../../shared/api/api'

export const MAX_REVIEW_LINKS = 10

export function isValidReviewUrl(url: string): boolean {
  try {
    const u = new URL(url.trim())
    return u.protocol === 'http:' || u.protocol === 'https:'
  } catch {
    return false
  }
}

export function reviewHost(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '')
  } catch {
    return ''
  }
}

export function reviewLabel(link: ReviewLink, index: number): string {
  const trimmed = link.label?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : `ดูรีวิว ${index + 1}`
}

export type ReviewDraft = {url: string; label: string}

export function sanitizeReviewDrafts(drafts: ReviewDraft[]): ReviewLink[] {
  return drafts
    .map((d) => ({url: d.url.trim(), label: d.label.trim()}))
    .filter((d) => d.url.length > 0)
    .map((d) => ({url: d.url, label: d.label.length > 0 ? d.label : null}))
}

export function draftsValid(drafts: ReviewDraft[]): boolean {
  const urls = drafts.map((d) => d.url.trim()).filter((u) => u.length > 0)
  return urls.length <= MAX_REVIEW_LINKS && urls.every(isValidReviewUrl)
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/trips/lib/reviewLinks.test.ts`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/reviewLinks.ts frontend/src/pages/trips/lib/reviewLinks.test.ts
git commit -m "feat(trips): reviewLinks client helpers (validate/host/label/sanitize) (#33)"
```

---

### Task 8: Card review button + popover + CSS

**Files:**
- Create: `frontend/src/pages/trips/components/ReviewIcon.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `place.reviewLinks` (`TripPlaceDto`, Task 6); `reviewHost`, `reviewLabel` (Task 7); `ReviewIcon`.
- Produces: the trailing `.stop-review-btn` affordance + `.rv-menu` popover on the card.

- [ ] **Step 1: Add the `ReviewIcon` SVG component**

Create `frontend/src/pages/trips/components/ReviewIcon.tsx` (video/play glyph — SVG, not emoji, per the icon rule):

```tsx
// frontend/src/pages/trips/components/ReviewIcon.tsx
// Video/play glyph for the per-Stop review affordance. Colour from currentColor,
// size from the parent CSS (.stop-review-btn svg).
export function ReviewIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
      <rect x="2.5" y="6" width="14" height="12" rx="2.5" />
      <path d="M16.5 10l5-3v10l-5-3z" fill="currentColor" stroke="none" />
    </svg>
  )
}
```

- [ ] **Step 2: Render the review affordance in `ItineraryStopCard`**

In `frontend/src/pages/trips/components/ItineraryStopCard.tsx`:

Add imports at the top:

```tsx
import {useEffect, useRef, useState} from 'react'
import {ReviewIcon} from './ReviewIcon'
import {reviewHost, reviewLabel} from '../lib/reviewLinks'
```

Add, inside the component body (before `return`):

```tsx
  const links = place.reviewLinks ?? []
  const [reviewOpen, setReviewOpen] = useState(false)
  const reviewRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!reviewOpen) return
    const onDown = (e: MouseEvent) => {
      if (reviewRef.current && !reviewRef.current.contains(e.target as Node)) setReviewOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setReviewOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [reviewOpen])
```

Insert the affordance **between** the `.stop-body` `</button>` and the `navUrl` block (so it is a sibling, never inside the editor button):

```tsx
      {links.length === 1 && (
        <a
          className="stop-review-btn"
          href={links[0].url}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="ดูรีวิว"
          onClick={(e) => e.stopPropagation()}
        >
          <ReviewIcon />
        </a>
      )}
      {links.length >= 2 && (
        <div className="stop-review-wrap" ref={reviewRef}>
          <button
            type="button"
            className="stop-review-btn"
            aria-label={`ดูรีวิว (${links.length})`}
            aria-expanded={reviewOpen}
            onClick={(e) => {
              e.stopPropagation()
              setReviewOpen((v) => !v)
            }}
          >
            <ReviewIcon />
            <span className="rv-count">{links.length}</span>
          </button>
          {reviewOpen && (
            <div className="rv-menu" role="menu">
              <div className="rv-menu-title">รีวิว</div>
              {links.map((l, i) => (
                <a
                  key={l.url + i}
                  href={l.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  role="menuitem"
                  onClick={() => setReviewOpen(false)}
                >
                  <ReviewIcon />
                  <span className="rv-label">{reviewLabel(l, i)}</span>
                  <span className="host">{reviewHost(l.url)}</span>
                </a>
              ))}
            </div>
          )}
        </div>
      )}
```

- [ ] **Step 3: Add the CSS tokens**

In `frontend/src/pages/trips/trips-tokens.css`, add the review colours to the same `:root`/`.trip-detail` token block that defines `--teal` etc.:

```css
  --review: #c2255c;
  --review-bg: #fdeaf1;
```

- [ ] **Step 4: Add the card + popover CSS**

Append to `frontend/src/pages/trips/TripDetailPage.css`:

```css
/* Review link (TikTok) affordance — trailing icon + popover (ADR-052) */
.stop-review-wrap { position: relative; display: flex; }
.stop-review-btn {
  flex: none; width: 42px; display: flex; align-items: center; justify-content: center;
  border: 0; border-left: 1px solid var(--border); background: var(--review-bg);
  color: var(--review); cursor: pointer; position: relative; text-decoration: none;
}
.stop-review-btn svg { width: 18px; height: 18px; }
.rv-count {
  position: absolute; top: 6px; right: 5px; min-width: 15px; height: 15px; padding: 0 3px;
  border-radius: 999px; background: var(--review); color: #fff; font-size: 9px; font-weight: 800;
  line-height: 15px; text-align: center;
}
.rv-menu {
  position: absolute; top: calc(100% + 4px); right: 0; z-index: 20; min-width: 220px; max-width: 300px;
  background: #fff; border: 1px solid #f6cede; border-radius: 11px; padding: 7px;
  box-shadow: 0 8px 24px rgba(194, 37, 92, .14);
}
.rv-menu-title {
  font-size: 10px; font-weight: 800; color: var(--review); text-transform: uppercase;
  letter-spacing: .04em; margin: 2px 6px 6px;
}
.rv-menu a {
  display: flex; align-items: center; gap: 8px; text-decoration: none; color: var(--ink);
  font-size: 12px; font-weight: 600; padding: 7px 8px; border-radius: 8px;
}
.rv-menu a:hover { background: var(--review-bg); }
.rv-menu a svg { width: 15px; height: 15px; flex: none; color: var(--review); }
.rv-menu a .rv-label { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rv-menu a .host { margin-left: auto; font-size: 10px; font-weight: 500; color: var(--muted); }
```

- [ ] **Step 5: Verify the typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: PASS. (DOM behaviour — 0 links → no button, 1 → direct anchor, ≥2 → badge + popover — is exercised in the Task 10 interactive check; the pure label/host logic is already covered by Task 7.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/ReviewIcon.tsx frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/TripDetailPage.css frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): review-link icon + popover on the itinerary Stop card (#33)"
```

---

### Task 9: Stop editor — "ลิงก์รีวิว" section + save wiring

**Files:**
- Modify: `frontend/src/pages/trips/components/StopEditorDialog.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`

**Interfaces:**
- Consumes: `place.reviewLinks` (Task 6); `sanitizeReviewDrafts`, `draftsValid`, `MAX_REVIEW_LINKS`, `type ReviewDraft` (Task 7); `updateTripPlace` (Task 6).
- Produces: the editable review-links UI; `updateTripPlace` now sends `reviewLinks` on every place save.

- [ ] **Step 1: Add review-draft state + helpers**

In `frontend/src/pages/trips/components/StopEditorDialog.tsx`, add imports:

```tsx
import {sanitizeReviewDrafts, draftsValid, MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'
```

Add state (near the other `useState` calls):

```tsx
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraft[]>(
    (place?.reviewLinks ?? []).map((l) => ({url: l.url, label: l.label ?? ''})),
  )
```

- [ ] **Step 2: Send `reviewLinks` on save (full-replace)**

`reviewLinks` is full-replace, so it must be sent whenever the dialog saves a place edit. Replace the `save()` body so `updatePlace` fires when **best-time OR review links** changed, and always includes the current fields:

```tsx
  const save = async () => {
    setSaveError(null)
    if (!draftsValid(reviewDrafts)) {
      setSaveError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    try {
      await updateStop({tripId, stopId, dwellMinutes: dwell, travelModeToReach: mode}).unwrap()
      const cleaned = sanitizeReviewDrafts(reviewDrafts)
      const bestTimeChanged = bestStart !== place?.bestTimeStart || bestEnd !== place?.bestTimeEnd
      const reviewsChanged =
        JSON.stringify(cleaned) !== JSON.stringify(place?.reviewLinks ?? [])
      if (place && (bestTimeChanged || reviewsChanged)) {
        await updatePlace({
          tripId,
          placeId: place.id,
          name: place.name,
          category: place.category,
          address: place.address,
          feeNote: place.feeNote,
          notes: place.notes,
          bestTimeStart: bestStart,
          bestTimeEnd: bestEnd,
          reviewLinks: cleaned,
        }).unwrap()
      }
      onClose()
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }
```

- [ ] **Step 3: Render the "ลิงก์รีวิว" section**

Add a new `<section className="se-sec">` inside the `.stop-editor` div (e.g. after the travel-mode section, before the `{preview && ...}` block):

```tsx
        <section className="se-sec">
          <div className="se-sec-head">
            <ReviewIcon />ลิงก์รีวิว (TikTok ฯลฯ)
          </div>
          {reviewDrafts.map((d, i) => (
            <div className="rv-row" key={i}>
              <input
                className="rv-url"
                type="url"
                placeholder="https://www.tiktok.com/@..."
                value={d.url}
                onChange={(e) =>
                  setReviewDrafts((rows) => rows.map((r, j) => (j === i ? {...r, url: e.target.value} : r)))
                }
              />
              <input
                className="rv-lab"
                placeholder="ป้ายกำกับ (ไม่บังคับ)"
                value={d.label}
                onChange={(e) =>
                  setReviewDrafts((rows) => rows.map((r, j) => (j === i ? {...r, label: e.target.value} : r)))
                }
              />
              <button
                type="button"
                className="rv-del"
                aria-label="ลบลิงก์"
                onClick={() => setReviewDrafts((rows) => rows.filter((_, j) => j !== i))}
              >
                ✕
              </button>
            </div>
          ))}
          {reviewDrafts.length < MAX_REVIEW_LINKS && (
            <button
              type="button"
              className="rv-add"
              onClick={() => setReviewDrafts((rows) => [...rows, {url: '', label: ''}])}
            >
              + เพิ่มลิงก์รีวิว
            </button>
          )}
        </section>
```

Add the `ReviewIcon` import at the top:

```tsx
import {ReviewIcon} from './ReviewIcon'
```

(The `✕` on the delete button is a plain Unicode glyph used elsewhere in this dialog's controls, not an emoji icon; keep it consistent with existing dialog buttons — if the linter/rule flags it, swap for a small inline SVG "×" path.)

- [ ] **Step 4: Add the editor-section CSS**

Append to `frontend/src/pages/trips/TripDetailPage.css`:

```css
/* Stop editor — review-links section */
.se-sec .rv-row { display: flex; gap: 7px; margin-bottom: 7px; }
.se-sec .rv-row .rv-url { flex: 1; min-width: 0; }
.se-sec .rv-row .rv-lab { width: 120px; }
.se-sec .rv-row input { border: 1px solid #e2e8f0; border-radius: 9px; padding: 8px 10px; font: inherit; font-size: 12.5px; }
.se-sec .rv-row .rv-del { flex: none; width: 34px; border: 1px solid #f4d0cc; border-radius: 9px; background: #fff; color: var(--bad); cursor: pointer; }
.se-sec .rv-add { display: inline-flex; align-items: center; gap: 6px; margin-top: 2px; border: 1px dashed #f6cede; background: var(--review-bg); color: var(--review); font-weight: 700; font-size: 12px; border-radius: 9px; padding: 8px 12px; cursor: pointer; }
```

- [ ] **Step 5: Verify the typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/StopEditorDialog.tsx frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): edit review links in the Stop editor; send on updateTripPlace (closes #33)"
```

---

### Task 10: Full-suite verification, migration apply & interactive check

**Files:** none (verification + ops).

- [ ] **Step 1: Run the full backend + frontend suites**

Run: `cd backend && dotnet test` then `cd ../frontend && npx tsc -b && npm run test && npm run build`
Expected: all green (this is what the pre-commit hook runs on every commit anyway).

- [ ] **Step 2: Apply the migration to the prod DB by hand (project rule — CLAUDE.md)**

Preview first, then apply (requires the terminal `az` session to be the personal SQL admin `thodsaphonSP@hotmail.co.th`):

```bash
cd backend
# preview the SQL
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
# apply
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

Expected: the `ReviewLinksJson` column exists on `TripPlaces` (confirm the script shows exactly `ALTER TABLE [TripPlaces] ADD [ReviewLinksJson] nvarchar(max) NULL`).

- [ ] **Step 3: Interactive verification (seeded/authed env)**

In the running SPA, on a Trip's itinerary:
- A Stop whose place has **no** review link shows **no** review button.
- Open the Stop editor → "ลิงก์รีวิว" → add one valid TikTok URL + a label → Save. The card now shows a single pink review icon; tapping it opens the URL in a **new tab**.
- Add a **second** link → Save. The card icon now shows a **count badge (2)**; tapping opens a **popover** listing both (label + host); each opens in a new tab; the popover closes on outside-click and Escape.
- In the editor, paste an invalid URL (e.g. `foo`) → Save is blocked with an inline error; a blank row is dropped silently.
- Confirm a place save (best-time or reviews) triggers one `PUT /places/{id}` and the expected `getItinerary` refetch (Network tab), and reviews persist across reload.
- (Optional) via an MCP client: `list_trip_places` shows `reviewLinks`; `update_trip_place` with the current list preserves them.

- [ ] **Step 4: Confirm the tracking issue is referenced**

Ensure every commit in this branch references the GitHub issue and the final commit closes it (`(closes #33)`).

---
