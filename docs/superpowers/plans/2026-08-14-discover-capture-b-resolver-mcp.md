# Discover capture — Plan B: four-input resolver & MCP

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `resolve_place` accepts a Google Maps URL, a `"lat, lng"` pair or a Plus Code, tells the caller how much to trust the answer, warns about places the user already saved nearby, and the whole saved-place library becomes readable over MCP.

**Architecture:** One pure classifier turns the free-form input into a discriminated kind, and `ResolvePlaceHandler` dispatches on it: a URL keeps going to `IPlaceResolver` (a live Google call), a coordinate is passed through verbatim ($0, no call), a Plus Code is decoded offline by the `OpenLocationCode` package ($0, no call). Whatever comes back is then annotated by a server-side scan of the caller's own library — the exact place if it is already saved, plus up to three within 100 m — so both the SPA and an MCP agent get duplicate awareness before a capture form is ever filled in. The parameter rename `url` → `input` is a breaking wire change and lands with every caller in one commit.

**Tech Stack:** .NET 10, Mediator (`ICommandHandler`), FluentValidation, EF Core, xUnit + Moq + FluentAssertions, `OpenLocationCode` 2.1.1 (netstandard2.0, no transitive dependencies, Apache-2.0 via google/open-location-code), `ModelContextProtocol` server tools, React 19 + RTK Query.

**Spec:** `docs/superpowers/specs/2026-08-13-discover-capture-design.md` (requirements R3.1, R3.5, R5.1, R5.2, R6.1, R6.2, R7.2, R12.1–R12.6)

**Depends on:** Plan A — complete, deployed, verified on prod. `TripPlace.OriginTripPlaceId` and its migration already exist.

## Global Constraints

Copied from the spec's own Global constraints section. Every task below inherits these.

- **UI copy is Thai.** Backend error messages stay English and are not translated (ADR-145); the SPA words user-facing failures in Thai itself.
- **Icons are inline-SVG components. Never emoji.** `@syncfusion/react-icons` is not installed.
- **Three classes implement `IApplicationDbContext`** — `AppDbContext` (prod), `SqliteAppDbContext` and `InMemoryAppDbContext` (tests). A new `DbSet<>` must be added to all three or the build fails `CS0535`. **Plan B adds no `DbSet<>` and no migration** — it only reads existing tables.
- **Backend tests use xUnit + Moq + FluentAssertions.** `Substitute.For<>` (NSubstitute) will not compile. Put a test beside the layer it exercises: `MenuNest.Application.UnitTests`, `MenuNest.Infrastructure.IntegrationTests`, `MenuNest.McpServer.UnitTests`, `MenuNest.WebApi.UnitTests`.
- **`git add <explicit paths>` only** — never `-A` or `.`. `daily-state.md` and `AGENTS.md` must never enter a feature commit.
- **Every commit references the tracking issue** — `(#48)` on each, `(closes #48)` only on the very last commit of Plan D.
- **The pre-commit hook runs the whole suite** (backend `dotnet build` + `dotnet test` Release, frontend `tsc --noEmit` + `npm run build`, ~40s+). Every commit must leave the entire suite green. Never `--no-verify`.
- **Prod deploys on push to `main`.** Do not push mid-plan; push once the plan's commits are complete and reviewed.
- **`ResolvedPlaceDto` is a positional record.** Before changing it, grep every construction site — production and test — and update them all in the same commit. This bit Plan A twice (`AddPlaceBody` and `TripPlaceDto`).

---

## File Structure

**Create**

| file | responsibility |
|---|---|
| `backend/src/MenuNest.Application/Places/PlaceInput.cs` | Pure classifier: free-form string → `PlaceInputKind` + normalized payload. No I/O. |
| `backend/src/MenuNest.Application/Places/PlusCodeDecoder.cs` | Thin seam over the `OpenLocationCode` package: full code → lat/lng; short code + reference → lat/lng. |
| `backend/src/MenuNest.Application/Abstractions/GeoDistance.cs` | `MetersBetween(lat1, lng1, lat2, lng2)`. Application-layer Haversine — `HaversineRouteService` lives in Infrastructure and returns route legs, so it is not reusable here without inverting the dependency. |
| `backend/src/MenuNest.McpServer/Tools/PlaceTools.cs` | The `list_my_places` MCP tool. A new tool type, registered alongside the existing seven. |
| `backend/tests/MenuNest.Application.UnitTests/Places/PlaceInputTests.cs` | Classifier truth table. |
| `backend/tests/MenuNest.Application.UnitTests/Places/PlusCodeDecoderTests.cs` | Decode of a full code, a short code with a reference, and rejection. |
| `backend/tests/MenuNest.Application.UnitTests/Places/GeoDistanceTests.cs` | Known-distance assertions. |
| `backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs` | Dispatch, `derivedFrom`, `alreadySaved`, `nearMatches`. |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/PlaceToolsTests.cs` | `list_my_places` delegates to `ListMyPlacesQuery`. |

**Modify**

| file | change |
|---|---|
| `backend/src/MenuNest.Application/Abstractions/GoogleMapsHosts.cs:11-16` | Accept Google ccTLD hosts (R6.2). |
| `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs:39-41` | `ResolvedPlaceDto` gains `DerivedFrom`, `AlreadySaved`, `NearMatches`. |
| `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceCommand.cs` | `Url` → `Input` (R12.1). |
| `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceValidator.cs:7-8` | Accept all three shapes, not only allowed hosts. |
| `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs` | Dispatch on kind; gain `IApplicationDbContext`; annotate the result. |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs:81-85` | `resolve_place` parameter rename + the read-back instruction (R12.4). |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs:94-107` | `add_trip_place` exposes `originTripPlaceId` (R12.6). |
| `backend/src/MenuNest.McpServer/McpServerRegistration.cs:17` | Register `PlaceTools`. |
| `backend/src/MenuNest.Application/MenuNest.Application.csproj` | Add `OpenLocationCode` 2.1.1. |
| `frontend/src/shared/api/api.ts:1412` | `{url}` → `{input}`; `ResolvedPlaceDto` gains the three members. |
| `frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx:24` | Send `{input}`. |

---

## Task 1: Google ccTLD short links reach the resolver

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/GoogleMapsHosts.cs:11-16`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleMapsHostsTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `GoogleMapsHosts.IsAllowedHost(string host) -> bool` and `IsAllowedUrl(string? url) -> bool`, unchanged signatures with widened behaviour. Task 3's validator calls `IsAllowedUrl`.

**Why:** R6.2 — the allowlist is `{maps.app.goo.gl, goo.gl, maps.google.com, www.google.com, google.com, g.co}` plus any `.google.com` suffix. A Thai user's share sheet produces `google.co.th` / `maps.google.co.th`, which matches none of them, so **every Google ccTLD link is rejected in prod** and CI never sees it because the fixtures stub `.com`. This is the single worst live defect in the resolver.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleMapsHostsTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GoogleMapsHostsTests
{
    [Theory]
    // Already allowed before this change — these must not regress.
    [InlineData("maps.app.goo.gl")]
    [InlineData("goo.gl")]
    [InlineData("g.co")]
    [InlineData("google.com")]
    [InlineData("www.google.com")]
    [InlineData("maps.google.com")]
    // R6.2: the ccTLD forms a Thai share sheet actually produces.
    [InlineData("google.co.th")]
    [InlineData("maps.google.co.th")]
    [InlineData("www.google.co.th")]
    [InlineData("google.de")]
    [InlineData("google.com.au")]
    [InlineData("maps.google.co.uk")]
    public void Allows(string host) => GoogleMapsHosts.IsAllowedHost(host).Should().BeTrue();

    [Theory]
    // A widened allowlist widens the SSRF surface, so the look-alikes matter more
    // than the happy path. Each of these must stay rejected.
    [InlineData("evilgoogle.com")]
    [InlineData("google.co.th.evil.com")]
    [InlineData("googlexcom")]
    [InlineData("notgoogle.de")]
    [InlineData("google.evil")]
    [InlineData("localhost")]
    [InlineData("169.254.169.254")]
    public void Rejects(string host) => GoogleMapsHosts.IsAllowedHost(host).Should().BeFalse();

    [Fact]
    public void RejectsNonHttpSchemes() =>
        GoogleMapsHosts.IsAllowedUrl("file:///etc/passwd").Should().BeFalse();

    [Fact]
    public void AllowsACcTldUrlEndToEnd() =>
        GoogleMapsHosts.IsAllowedUrl("https://maps.google.co.th/maps/place/Wat+Phra+Kaew/").Should().BeTrue();
}
```

- [ ] **Step 2: Run it and watch the ccTLD cases fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GoogleMapsHostsTests`
Expected: FAIL — the six ccTLD rows in `Allows` return false; every `Rejects` row already passes.

- [ ] **Step 3: Widen the allowlist**

Replace lines 11-16 of `backend/src/MenuNest.Application/Abstractions/GoogleMapsHosts.cs`:

```csharp
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        { "maps.app.goo.gl", "goo.gl", "g.co" };

    // A Google ccTLD host: optional sub-labels, then "google.", then a public-suffix
    // shape we accept — a bare TLD (google.de), or a two-level one (google.co.th,
    // google.com.au). Anchored at both ends so "google.co.th.evil.com" cannot match,
    // and the label before "google" must end with a dot so "evilgoogle.com" cannot
    // either. This is deliberately not a full public-suffix list: the set below is
    // the shape Google actually publishes, and every additional character we admit
    // is SSRF surface (ADR-007's two-layer defence).
    //
    // Accepted residual: `[a-z]{2,3}` admits google.<any 2-3 letter TLD>, so a host
    // like google.zip passes even though this app has no reason to fetch it. The
    // exposure requires an attacker to CONTROL google.<tld>, which Google registers
    // defensively across the TLD space — and the alternative, embedding a real
    // public-suffix list, is a dependency and an update treadmill for a link
    // parser. Revisit only if this ever fetches something other than Maps links.
    private static readonly System.Text.RegularExpressions.Regex GoogleCcTld =
        new(@"^(?:[a-z0-9-]+\.)*google\.(?:[a-z]{2,3}|co\.[a-z]{2}|com\.[a-z]{2})$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsAllowedHost(string host) =>
        Allowed.Contains(host) || GoogleCcTld.IsMatch(host);
```

- [ ] **Step 4: Run the test and the two suites that depend on this allowlist**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GoogleMapsHostsTests|FullyQualifiedName~GooglePlaceResolverTests"`
Expected: PASS. `GooglePlaceResolverTests` re-checks the final URL after redirects through the same helper, so a regression there means the regex is too narrow.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/GoogleMapsHosts.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleMapsHostsTests.cs
git commit -m "fix(trips): Google ccTLD share links are no longer rejected (#48)"
```

---

## Task 2: Offline Plus Code decode

**Files:**
- Modify: `backend/src/MenuNest.Application/MenuNest.Application.csproj`
- Create: `backend/src/MenuNest.Application/Places/PlusCodeDecoder.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/PlusCodeDecoderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum PlusCodeKind { Invalid, Full, Short }`
  - `static PlusCodeKind PlusCodeDecoder.Classify(string? code)`
  - `static (double Lat, double Lng)? PlusCodeDecoder.DecodeFull(string? code)`
  - `static (double Lat, double Lng)? PlusCodeDecoder.DecodeShort(string? code, double refLat, double refLng)`

  Task 3 calls `Classify`; Task 4 calls both `Decode*`.

**Why:** R5.1 — the Trips `searchText` resolver returns zero results for every Plus Code, so it cannot be reused. R5.2 — decode offline for $0. R7.2 — Geocoding would cost $5/1k, and on a wrong locality a short code is confidently ~500 km off, which is why a short code takes an explicit reference point rather than guessing from the map camera.

Package verified live on the NuGet registry before naming it (do not substitute another): `OpenLocationCode` **2.1.1**, by Jon McPherson, `lib/netstandard2.0` only, **zero dependencies**, licensed under google/open-location-code's LICENSE. Public API confirmed from the shipped XML docs: `Google.OpenLocationCode.OpenLocationCode.IsValid/IsShort/IsFull(string)`, `Decode(string) -> CodeArea` (with `CenterLatitude` / `CenterLongitude`), and `OpenLocationCode.ShortCode.RecoverNearest(string, double, double)`.

- [ ] **Step 1: Add the package**

```bash
cd backend && dotnet add src/MenuNest.Application/MenuNest.Application.csproj package OpenLocationCode --version 2.1.1
```

- [ ] **Step 2: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Places/PlusCodeDecoderTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Places;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

public class PlusCodeDecoderTests
{
    [Theory]
    [InlineData("7P52PJ88+8G", PlusCodeKind.Full)]
    [InlineData("7p52pj88+8g", PlusCodeKind.Full)]   // case-insensitive
    [InlineData("PJ88+8G", PlusCodeKind.Short)]
    [InlineData("not a code", PlusCodeKind.Invalid)]
    [InlineData("", PlusCodeKind.Invalid)]
    [InlineData("13.7563, 100.5018", PlusCodeKind.Invalid)]
    public void ClassifiesTheThreeCases(string code, PlusCodeKind expected) =>
        PlusCodeDecoder.Classify(code).Should().Be(expected);

    [Fact]
    public void DecodesAFullCodeToItsCentre()
    {
        // 7P52PJ88+8G is central Bangkok. A full code is a deterministic offline
        // decode — no reference point, no network, no cost.
        var p = PlusCodeDecoder.DecodeFull("7P52PJ88+8G");

        p.Should().NotBeNull();
        p!.Value.Lat.Should().BeApproximately(13.7563, 0.01);
        p.Value.Lng.Should().BeApproximately(100.5018, 0.01);
    }

    [Fact]
    public void DecodesAShortCodeAgainstItsReferencePoint()
    {
        var p = PlusCodeDecoder.DecodeShort("PJ88+8G", 13.75, 100.50);

        p.Should().NotBeNull();
        p!.Value.Lat.Should().BeApproximately(13.7563, 0.01);
        p.Value.Lng.Should().BeApproximately(100.5018, 0.01);
    }

    [Fact]
    public void TheSameShortCodeResolvesElsewhereFromAnotherReference()
    {
        // The reason R5.2 refuses to guess the locality: the identical short code
        // recovers to a completely different place from a different reference.
        var bangkok = PlusCodeDecoder.DecodeShort("PJ88+8G", 13.75, 100.50)!.Value;
        var chiangmai = PlusCodeDecoder.DecodeShort("PJ88+8G", 18.79, 98.98)!.Value;

        GeoDistanceForTest(bangkok, chiangmai).Should().BeGreaterThan(100_000);
    }

    [Fact]
    public void ReturnsNullRatherThanThrowingOnGarbage()
    {
        PlusCodeDecoder.DecodeFull("not a code").Should().BeNull();
        PlusCodeDecoder.DecodeShort("not a code", 13.75, 100.50).Should().BeNull();
        PlusCodeDecoder.DecodeFull("PJ88+8G").Should().BeNull(); // short passed to full
    }

    private static double GeoDistanceForTest((double Lat, double Lng) a, (double Lat, double Lng) b)
    {
        var dLat = (a.Lat - b.Lat) * 111_000;
        var dLng = (a.Lng - b.Lng) * 111_000 * Math.Cos(a.Lat * Math.PI / 180);
        return Math.Sqrt(dLat * dLat + dLng * dLng);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlusCodeDecoderTests`
Expected: FAIL to compile — `MenuNest.Application.Places.PlusCodeDecoder` does not exist.

- [ ] **Step 4: Implement the decoder**

Create `backend/src/MenuNest.Application/Places/PlusCodeDecoder.cs`:

```csharp
using Olc = Google.OpenLocationCode.OpenLocationCode;

namespace MenuNest.Application.Places;

public enum PlusCodeKind { Invalid = 0, Full, Short }

/// <summary>
/// Offline Plus Code decode (spec R5.2, ticket #57). Costs nothing and makes no
/// network call: the Trips searchText resolver returns zero results for every Plus
/// Code (R5.1), and Geocoding — which does work — is $5/1k and, on the wrong
/// locality, confidently ~500 km off. A SHORT code therefore requires an explicit
/// reference point from the caller; it is never guessed from the map camera.
///
/// Every entry point returns null rather than throwing: the package throws
/// ArgumentException for anything unparseable, and a bad paste is normal user
/// input, not an exceptional condition.
/// </summary>
public static class PlusCodeDecoder
{
    public static PlusCodeKind Classify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return PlusCodeKind.Invalid;
        var c = code.Trim();
        if (Olc.IsFull(c)) return PlusCodeKind.Full;
        if (Olc.IsShort(c)) return PlusCodeKind.Short;
        return PlusCodeKind.Invalid;
    }

    public static (double Lat, double Lng)? DecodeFull(string? code)
    {
        if (Classify(code) != PlusCodeKind.Full) return null;
        try
        {
            var area = Olc.Decode(code!.Trim());
            return (area.CenterLatitude, area.CenterLongitude);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static (double Lat, double Lng)? DecodeShort(string? code, double refLat, double refLng)
    {
        if (Classify(code) != PlusCodeKind.Short) return null;
        try
        {
            var full = Olc.ShortCode.RecoverNearest(code!.Trim(), refLat, refLng);
            var area = Olc.Decode(full.Code);
            return (area.CenterLatitude, area.CenterLongitude);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlusCodeDecoderTests`
Expected: PASS (6 test methods).

If `RecoverNearest` returns something without a `.Code` property, read the shipped XML docs rather than guessing: `dotnet nuget locals global-packages --list`, then open `openlocationcode/2.1.1/lib/netstandard2.0/OpenLocationCode.xml`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/MenuNest.Application.csproj \
        backend/src/MenuNest.Application/Places/PlusCodeDecoder.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/PlusCodeDecoderTests.cs
git commit -m "feat(trips): decode Plus Codes offline with no Google call (#48)"
```

---

## Task 3: One discriminated input, renamed url → input

**Files:**
- Create: `backend/src/MenuNest.Application/Places/PlaceInput.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/PlaceInputTests.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Trips/ResolvePlaceBindingTests.cs` (create)
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceValidator.cs:7-8`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs:18`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:81-85`
- Modify: `frontend/src/shared/api/api.ts:1412`
- Modify: `frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx:24`

**Interfaces:**
- Consumes: `PlusCodeDecoder.Classify` (Task 2).
- Produces:
  - `enum PlaceInputKind { Unknown, MapsUrl, Coordinate, PlusCodeFull, PlusCodeShort }`
  - `readonly record struct PlaceInputParse(PlaceInputKind Kind, double Lat, double Lng)`
  - `static PlaceInputParse PlaceInput.Parse(string? input)` — for `Coordinate` the struct carries the parsed pair; for every other kind `Lat`/`Lng` are `0`.
  - `ResolvePlaceCommand(string Input)`

  Task 4's handler switches on `PlaceInput.Parse(c.Input).Kind`.

**Why:** R12.1 — one parameter accepts all three shapes, and `ResolvePlaceValidator` currently rejects everything that is not an allowed Google host, so it would reject both new shapes outright.

**This is a breaking wire change.** `ResolvePlaceCommand` is bound straight from the HTTP body at `TripsController.cs:65-67`, so renaming the member changes the JSON contract. The SPA (`api.ts:1412`, `PlaceLinkFallbackDialog.tsx:24`) and the MCP tool parameter must move in the **same commit** — a split leaves prod silently sending `{url}` into a member that no longer exists, and System.Text.Json binds the missing `Input` to null without complaint. This is the identical failure mode that made Plan A inert in production (`c472df5`).

- [ ] **Step 1: Write the failing classifier test**

Create `backend/tests/MenuNest.Application.UnitTests/Places/PlaceInputTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Places;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

public class PlaceInputTests
{
    [Theory]
    [InlineData("https://maps.app.goo.gl/abc123")]
    [InlineData("https://www.google.com/maps/place/Wat+Pho/")]
    [InlineData("https://maps.google.co.th/maps/place/Wat+Pho/")]
    public void RecognisesAMapsUrl(string input) =>
        PlaceInput.Parse(input).Kind.Should().Be(PlaceInputKind.MapsUrl);

    [Theory]
    [InlineData("13.7563, 100.5018")]
    [InlineData("13.7563,100.5018")]
    [InlineData("  13.7563 , 100.5018  ")]
    [InlineData("-33.8688, 151.2093")]
    public void RecognisesACoordinatePair(string input) =>
        PlaceInput.Parse(input).Kind.Should().Be(PlaceInputKind.Coordinate);

    [Fact]
    public void CarriesTheParsedCoordinate()
    {
        var p = PlaceInput.Parse("13.7563, 100.5018");

        p.Lat.Should().BeApproximately(13.7563, 1e-6);
        p.Lng.Should().BeApproximately(100.5018, 1e-6);
    }

    [Fact]
    public void ParsesCoordinatesInvariantlyRegardlessOfServerCulture()
    {
        // A server running under a comma-decimal culture must not read "13.7563"
        // as 137563. Parsing is pinned to InvariantCulture.
        var prior = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            PlaceInput.Parse("13.7563, 100.5018").Lat.Should().BeApproximately(13.7563, 1e-6);
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = prior; }
    }

    [Theory]
    [InlineData("91.0, 100.0")]     // latitude out of range
    [InlineData("13.75, 181.0")]    // longitude out of range
    public void RejectsAnOutOfRangeCoordinate(string input) =>
        PlaceInput.Parse(input).Kind.Should().Be(PlaceInputKind.Unknown);

    [Fact]
    public void RecognisesAFullPlusCode() =>
        PlaceInput.Parse("7P52PJ88+8G").Kind.Should().Be(PlaceInputKind.PlusCodeFull);

    [Fact]
    public void RecognisesAShortPlusCode() =>
        PlaceInput.Parse("PJ88+8G").Kind.Should().Be(PlaceInputKind.PlusCodeShort);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://evil.example.com/maps/place/x/")]
    [InlineData("just some words")]
    public void RejectsEverythingElse(string? input) =>
        PlaceInput.Parse(input).Kind.Should().Be(PlaceInputKind.Unknown);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceInputTests`
Expected: FAIL to compile — `PlaceInput` does not exist.

- [ ] **Step 3: Implement the classifier**

Create `backend/src/MenuNest.Application/Places/PlaceInput.cs`:

```csharp
using System.Globalization;
using MenuNest.Application.Abstractions;

namespace MenuNest.Application.Places;

public enum PlaceInputKind { Unknown = 0, MapsUrl, Coordinate, PlusCodeFull, PlusCodeShort }

public readonly record struct PlaceInputParse(PlaceInputKind Kind, double Lat, double Lng);

/// <summary>
/// The one discriminator behind resolve_place's single `input` parameter (R12.1).
/// Pure and I/O-free so the validator, the handler and the tests all read the same
/// verdict; nothing here decides cost or reaches Google.
///
/// Order matters: a Google Maps URL is checked first because a URL can contain a
/// "+" that would otherwise tempt the Plus Code matcher, and the coordinate check
/// precedes Plus Codes because "13.7563, 100.5018" is unambiguous.
/// </summary>
public static class PlaceInput
{
    public static PlaceInputParse Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(PlaceInputKind.Unknown, 0, 0);
        var s = input.Trim();

        if (GoogleMapsHosts.IsAllowedUrl(s)) return new(PlaceInputKind.MapsUrl, 0, 0);

        if (TryParseCoordinate(s, out var lat, out var lng))
            return new(PlaceInputKind.Coordinate, lat, lng);

        return PlusCodeDecoder.Classify(s) switch
        {
            PlusCodeKind.Full => new(PlaceInputKind.PlusCodeFull, 0, 0),
            PlusCodeKind.Short => new(PlaceInputKind.PlusCodeShort, 0, 0),
            _ => new(PlaceInputKind.Unknown, 0, 0),
        };
    }

    private static bool TryParseCoordinate(string s, out double lat, out double lng)
    {
        lat = lng = 0;
        var parts = s.Split(',');
        if (parts.Length != 2) return false;

        // InvariantCulture, always: a comma-decimal server culture would read
        // "13.7563" as 137563 and silently place the pin in the Arctic.
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng)) return false;

        return lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
    }
}
```

- [ ] **Step 4: Run the classifier test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PlaceInputTests`
Expected: PASS.

- [ ] **Step 5: Rename the command member and widen the validator**

Replace `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceCommand.cs` entirely:

```csharp
using Mediator;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;

/// <summary>
/// R12.1: one discriminated input — a Google Maps URL, "lat, lng", or a Plus Code.
/// Renamed from `Url`; this record is bound straight from the HTTP body at
/// TripsController.cs:65, so the SPA and the MCP tool move with it.
/// </summary>
public sealed record ResolvePlaceCommand(string Input) : ICommand<ResolvedPlaceDto>;
```

Replace lines 7-8 of `ResolvePlaceValidator.cs` and add `using MenuNest.Application.Places;`:

```csharp
    public ResolvePlaceValidator() => RuleFor(x => x.Input)
        .NotEmpty()
        .Must(v => PlaceInput.Parse(v).Kind != PlaceInputKind.Unknown)
        .WithMessage("Provide a Google Maps link, a \"lat, lng\" pair, or a Plus Code.");
```

Change `ResolvePlaceHandler.cs:18` from `c.Url` to `c.Input`.

- [ ] **Step 6: Move every caller in the same commit**

`backend/src/MenuNest.McpServer/Tools/TripTools.cs:81-85` — rename the parameter only (the description rewrite is Task 7):

```csharp
    public async Task<ResolvedPlaceDto> resolve_place(
        [Description("A Google Maps URL, a \"lat, lng\" pair such as \"13.7563, 100.5018\", or a Plus Code. To search by name, use https://www.google.com/maps/place/<url-encoded name and city>/")] string input,
        CancellationToken ct)
        => await mediator.Send(new ResolvePlaceCommand(input), ct);
```

`frontend/src/shared/api/api.ts:1412`:

```ts
        resolvePlace: build.mutation<ResolvedPlaceDto, {input: string}>({
```

`frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx:24`:

```ts
      const dto = await resolvePlace({input: url}).unwrap()
```

- [ ] **Step 7: Prove the wire, not just the compile**

Create `backend/tests/MenuNest.WebApi.UnitTests/Trips/ResolvePlaceBindingTests.cs` — the controller binds `ResolvePlaceCommand` directly, and a rename that compiles can still bind to null:

```csharp
using System.Text.Json;
using FluentAssertions;
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Trips;

public class ResolvePlaceBindingTests
{
    [Fact]
    public void BindsTheInputMemberFromTheWireBody()
    {
        // The SPA and MCP both post {"input": "..."} — if the member were still
        // named Url this deserializes to null and every resolve silently fails
        // validation instead of resolving. Plan A shipped exactly this defect.
        var cmd = JsonSerializer.Deserialize<ResolvePlaceCommand>(
            """{"input":"https://maps.app.goo.gl/abc123"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        cmd.Should().NotBeNull();
        cmd!.Input.Should().Be("https://maps.app.goo.gl/abc123");
    }
}
```

- [ ] **Step 8: Run the whole backend suite plus the frontend gates**

Run: `cd backend && dotnet test`
Then: `cd frontend && npx tsc --noEmit && npx vitest run`
Expected: all green. Any remaining `.Url` reference fails the build — that is the point of doing the rename in one step.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/Places/PlaceInput.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceCommand.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceValidator.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/PlaceInputTests.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Trips/ResolvePlaceBindingTests.cs \
        frontend/src/shared/api/api.ts \
        frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx
git commit -m "feat(trips): resolve_place takes one discriminated input, url renamed to input (#48)"
```

---

## Task 4: Resolve all three inputs and report how much to trust the answer

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs:39-41`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GooglePlaceResolver.cs` (construction sites)
- Modify: `frontend/src/shared/api/api.ts` (`ResolvedPlaceDto` type)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs`

**Interfaces:**
- Consumes: `PlaceInput.Parse` (Task 3), `PlusCodeDecoder.DecodeFull` (Task 2). **Not** `DecodeShort` — this plan refuses short codes; `DecodeShort` is built and tested in Task 2 for Plan C, which is the first surface with a locality to pass it.
- Produces: `ResolvedPlaceDto` with three new trailing members, plus `PlaceDerivedFrom`, `AlreadySavedDto`, `NearMatchDto`. Task 5 fills `AlreadySaved` and `NearMatches`; Plan C's capture form reads `DerivedFrom`.

**Why:** R12.4 — a caller cannot tell a `place_id` hit from a name search that returned a different branch of a chain, or a full Plus Code from a short one recovered against a possibly-wrong locality. `derivedFrom` makes the difference explicit, and `resolve_place`'s description turns it into an instruction.

**Before editing the DTO, enumerate its construction sites** — it is a positional record and the compiler is the only thing that will find them:

```bash
grep -rn "new ResolvedPlaceDto\|ResolvedPlaceDto(" backend/ --include=*.cs | grep -v "/obj/\|/bin/"
```

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class ResolvePlaceHandlerTests
{
    private static ResolvePlaceHandler Build(Mock<IPlaceResolver> resolver, IApplicationDbContext db)
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new User { Id = Guid.NewGuid() });
        return new ResolvePlaceHandler(resolver.Object, users.Object, new ResolvePlaceValidator(), db);
    }

    [Fact]
    public async Task ACoordinateIsPassedThroughVerbatimAndCostsNoGoogleCall()
    {
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict); // strict: any call fails the test
        using var db = new InMemoryAppDbContext();

        var dto = await Build(resolver, db).Handle(new ResolvePlaceCommand("13.7563, 100.5018"), default);

        dto.Lat.Should().BeApproximately(13.7563, 1e-6);
        dto.Lng.Should().BeApproximately(100.5018, 1e-6);
        dto.GooglePlaceId.Should().BeNull();
        dto.DerivedFrom.Should().Be(PlaceDerivedFrom.CoordinateVerbatim);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AFullPlusCodeDecodesOfflineAndIsTrustworthy()
    {
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);
        using var db = new InMemoryAppDbContext();

        var dto = await Build(resolver, db).Handle(new ResolvePlaceCommand("7P52PJ88+8G"), default);

        dto.DerivedFrom.Should().Be(PlaceDerivedFrom.PlusCodeFull);
        dto.Lat.Should().BeApproximately(13.7563, 0.01);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AShortPlusCodeIsRefusedRatherThanGuessed()
    {
        // R5.2's whole rationale: a short code recovered against the wrong
        // locality decodes SUCCESSFULLY to a point that can be hundreds of km
        // out — "PJ88+8G" against (0,0) lands in the Gulf of Guinea, ~7,000 km
        // from the Bangkok the user meant. derivedFrom would only LABEL that;
        // it never blocks a save. So this layer refuses until a reference point
        // exists (Plan C), because a refusal the user can act on beats a wrong
        // pin they have to notice.
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);
        using var db = new InMemoryAppDbContext();

        var act = () => Build(resolver, db).Handle(new ResolvePlaceCommand("PJ88+8G"), default).AsTask();

        await act.Should().ThrowAsync<ValidationException>();
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AUrlStillGoesToTheLiveResolver()
    {
        var resolver = new Mock<IPlaceResolver>();
        resolver.Setup(r => r.ResolveFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlaceDto(
                "place-1", "Wat Pho", 13.7465, 100.4927, "Bangkok",
                PlaceCategory.Other, null, null, null, PlaceDerivedFrom.ExactPlaceId));
        using var db = new InMemoryAppDbContext();

        var dto = await Build(resolver, db)
            .Handle(new ResolvePlaceCommand("https://maps.app.goo.gl/abc123"), default);

        dto.GooglePlaceId.Should().Be("place-1");
        resolver.Verify(r => r.ResolveFromUrlAsync(
            "https://maps.app.goo.gl/abc123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUnknownInputIsRejectedByValidation()
    {
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);
        using var db = new InMemoryAppDbContext();

        var act = () => Build(resolver, db).Handle(new ResolvePlaceCommand("just some words"), default).AsTask();

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ResolvePlaceHandlerTests`
Expected: FAIL to compile — `PlaceDerivedFrom` and the four-argument handler constructor do not exist.

- [ ] **Step 3: Extend the DTO**

Replace `ResolvedPlaceDto` at `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs:39-41`:

```csharp
/// <summary>How the coordinates were arrived at, and therefore how much to trust
/// them (R12.4). Anything other than ExactPlaceId must be read back to the user
/// before it is committed with add_trip_place.</summary>
public enum PlaceDerivedFrom
{
    ExactPlaceId = 0,     // trustworthy
    NameSearch,           // NOT trustworthy — may be a different branch of a chain
    CoordinateVerbatim,   // exactly what the caller supplied
    PlusCodeFull,         // trustworthy — a deterministic offline decode
    // Reserved for Plan C, which is the first surface able to supply a locality.
    // Plan B never emits it: a short code is refused, not recovered against a
    // default reference (see the handler's dispatch).
    PlusCodeShort,        // NOT trustworthy — recovered against a caller-supplied reference
}

/// <summary>A saved place of the caller's within 100 m of the resolved point (R3.5).
/// Advisory only — it never blocks the capture, and the name is shown for the user's
/// judgement but takes no part in the predicate.</summary>
public sealed record NearMatchDto(
    Guid TripPlaceId, string Name, double Lat, double Lng, int DistanceMeters);

/// <summary>The place this input already resolves to in the caller's library (R12.3).
/// Library-level, not per-trip: at resolve time neither surface knows the target Trip.</summary>
public sealed record AlreadySavedDto(
    Guid TripPlaceId, string Name, IReadOnlyList<string> TripNames);

public sealed record ResolvedPlaceDto(
    string? GooglePlaceId, string Name, double Lat, double Lng, string? Address,
    PlaceCategory Category, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson,
    PlaceDerivedFrom DerivedFrom = PlaceDerivedFrom.ExactPlaceId,
    AlreadySavedDto? AlreadySaved = null,
    IReadOnlyList<NearMatchDto>? NearMatches = null);
```

The three new members are **defaulted** so the existing construction sites in `GooglePlaceResolver` and `MissingConfigPlaceResolver` keep compiling. Set them explicitly where they matter — Step 4.

- [ ] **Step 4: Mark the URL path's own derivation**

`GooglePlaceResolver` re-finds the place by name when the URL carries no `place_id`, which R6.3 keeps out of scope to *fix* but R12.4 requires us to *disclose*. At each `new ResolvedPlaceDto(...)` in `backend/src/MenuNest.Infrastructure/Maps/GooglePlaceResolver.cs`, pass the derivation explicitly:

```csharp
// A place_id lifted straight from the URL is authoritative; anything we had to
// Text-Search by name may be a different branch of a chain (R6.3, R12.4).
DerivedFrom: placeIdFromUrl is not null
    ? PlaceDerivedFrom.ExactPlaceId
    : PlaceDerivedFrom.NameSearch,
```

Use the local variable that actually holds the URL's `place_id` in that method — read the file and substitute the real name; do not assume `placeIdFromUrl` exists.

- [ ] **Step 5: Dispatch in the handler**

Replace `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs` (the `IApplicationDbContext` field is added here and *used* in Task 5):

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.Places;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;

public sealed class ResolvePlaceHandler : ICommandHandler<ResolvePlaceCommand, ResolvedPlaceDto>
{
    private readonly IPlaceResolver _resolver;
    private readonly IUserProvisioner _users;
    private readonly IValidator<ResolvePlaceCommand> _validator;
    private readonly IApplicationDbContext _db;

    public ResolvePlaceHandler(
        IPlaceResolver resolver, IUserProvisioner users,
        IValidator<ResolvePlaceCommand> validator, IApplicationDbContext db)
    { _resolver = resolver; _users = users; _validator = validator; _db = db; }

    public async ValueTask<ResolvedPlaceDto> Handle(ResolvePlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var parsed = PlaceInput.Parse(c.Input);
        var resolved = parsed.Kind switch
        {
            // The URL path is the only one that costs a Google call (R7.2).
            PlaceInputKind.MapsUrl => await _resolver.ResolveFromUrlAsync(c.Input.Trim(), ct),
            PlaceInputKind.Coordinate => Bare(parsed.Lat, parsed.Lng, PlaceDerivedFrom.CoordinateVerbatim),
            PlaceInputKind.PlusCodeFull => FromFullPlusCode(c.Input),
            // A SHORT code cannot be resolved here and must NOT be guessed. This
            // handler has no locality, and recovering against any default — (0,0),
            // the map camera, anything — is precisely the failure R5.2 exists to
            // prevent: the decode succeeds and returns a confidently wrong point
            // hundreds or thousands of km away, which `derivedFrom` only labels,
            // never blocks. Refusing is the correct answer until the capture
            // surface supplies a reference point (Plan C).
            PlaceInputKind.PlusCodeShort => throw new ValidationException(
                "A short Plus Code needs a locality. Include the town or city, or paste the full code."),
            _ => throw new ValidationException("Unsupported input."),
        };

        return resolved; // Task 5 annotates alreadySaved / nearMatches here.
    }

    /// <summary>
    /// A coordinate/Plus Code place has no Google identity and no name of its own —
    /// R4.1 makes Name a required FORM field, so the capture surface supplies it.
    /// The empty name here is the signal that the form must ask.
    /// </summary>
    private static ResolvedPlaceDto Bare(double lat, double lng, PlaceDerivedFrom from) =>
        new(null, string.Empty, lat, lng, null, PlaceCategory.Other, null, null, null, from);

    private static ResolvedPlaceDto FromFullPlusCode(string input)
    {
        var p = PlusCodeDecoder.DecodeFull(input);
        if (p is null) throw new ValidationException("That Plus Code could not be decoded.");
        return Bare(p.Value.Lat, p.Value.Lng, PlaceDerivedFrom.PlusCodeFull);
    }
}
```

- [ ] **Step 6: Mirror the DTO in the SPA type**

In `frontend/src/shared/api/api.ts`, beside the existing `ResolvedPlaceDto` interface:

```ts
export type PlaceDerivedFrom =
    | 'ExactPlaceId' | 'NameSearch' | 'CoordinateVerbatim' | 'PlusCodeFull' | 'PlusCodeShort'

export interface NearMatchDto {
    tripPlaceId: string; name: string; lat: number; lng: number; distanceMeters: number
}
export interface AlreadySavedDto { tripPlaceId: string; name: string; tripNames: string[] }
```

and add to `ResolvedPlaceDto`:

```ts
    derivedFrom: PlaceDerivedFrom
    alreadySaved: AlreadySavedDto | null
    nearMatches: NearMatchDto[] | null
```

- [ ] **Step 7: Run the tests**

Run: `cd backend && dotnet test` then `cd frontend && npx tsc --noEmit`
Expected: all green. `MissingConfigPlaceResolver` and every test constructing `ResolvedPlaceDto` still compile because the new members are defaulted.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs \
        backend/src/MenuNest.Infrastructure/Maps/GooglePlaceResolver.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs \
        frontend/src/shared/api/api.ts
git commit -m "feat(trips): resolve coordinates and Plus Codes, and report derivedFrom (#48)"
```

---

## Task 5: Duplicate awareness at resolve time

**Files:**
- Create: `backend/src/MenuNest.Application/Abstractions/GeoDistance.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Places/GeoDistanceTests.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `ResolvedPlaceDto`, `AlreadySavedDto`, `NearMatchDto` (Task 4).
- Produces: `static double GeoDistance.MetersBetween(double lat1, double lng1, double lat2, double lng2)`.

**Why:** R3.1 — an exact `place_id` match must be detected **at resolve time**, before the capture form renders, so the form opens "already saved" and the user never types a category or review links that would be discarded. R3.5 — a `place_id`-less near match warns and never blocks: scan the caller's whole library for places within **100 m**, show the **nearest 3**, and keep the primary button enabled. The name is displayed for the user's judgement but **takes no part in the predicate** — no fuzzy matching over freeform Thai names. R12.3 — both fire for **all three** inputs and are library-level, because at resolve time neither surface knows the target Trip.

- [ ] **Step 1: Write the failing distance test**

Create `backend/tests/MenuNest.Application.UnitTests/Places/GeoDistanceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

public class GeoDistanceTests
{
    [Fact]
    public void IsZeroForTheSamePoint() =>
        GeoDistance.MetersBetween(13.7563, 100.5018, 13.7563, 100.5018).Should().BeApproximately(0, 0.001);

    [Fact]
    public void MatchesAKnownShortDistance() =>
        // 0.001° of latitude ≈ 111 m anywhere on the globe.
        GeoDistance.MetersBetween(13.7563, 100.5018, 13.7573, 100.5018).Should().BeApproximately(111, 2);

    [Fact]
    public void MatchesAKnownLongDistance() =>
        // Bangkok → Chiang Mai is ~580 km great-circle.
        GeoDistance.MetersBetween(13.7563, 100.5018, 18.7883, 98.9853).Should().BeApproximately(580_000, 10_000);

    [Fact]
    public void IsSymmetric() =>
        GeoDistance.MetersBetween(13.75, 100.50, 18.78, 98.98)
            .Should().BeApproximately(GeoDistance.MetersBetween(18.78, 98.98, 13.75, 100.50), 0.001);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GeoDistanceTests`
Expected: FAIL to compile — `GeoDistance` does not exist.

- [ ] **Step 3: Implement it**

Create `backend/src/MenuNest.Application/Abstractions/GeoDistance.cs`:

```csharp
namespace MenuNest.Application.Abstractions;

/// <summary>
/// Great-circle distance in metres. Application-layer on purpose: the existing
/// HaversineRouteService lives in Infrastructure and returns route legs behind
/// IRouteService, so reusing it here would invert the dependency for one formula.
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double MetersBetween(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GeoDistanceTests`
Expected: PASS.

- [ ] **Step 5: Write the failing annotation tests**

Append to `ResolvePlaceHandlerTests.cs`. First read `backend/tests/MenuNest.Application.UnitTests/Places/ListMyPlacesHandlerTests.cs` and mirror its `SqliteAppDbContext` + `Trip` + `TripPlace` seeding shape — write `SeedUserWithPlaces(db, userId, params (string Name, double Lat, double Lng, string? GooglePlaceId, string TripName)[])` and a `BuildFor(userId, resolver, db)` that stubs `IUserProvisioner` to return that user. Mirroring rather than inventing is what keeps the two suites from drifting.

```csharp
    [Fact]
    public async Task AnExactPlaceIdAlreadyInTheLibraryComesBackAsAlreadySaved()
    {
        using var db = new SqliteAppDbContext();
        var me = Guid.NewGuid();
        SeedUserWithPlaces(db, me, ("Wat Pho", 13.7465, 100.4927, "place-1", "เที่ยวกรุงเทพ"));

        var resolver = new Mock<IPlaceResolver>();
        resolver.Setup(r => r.ResolveFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlaceDto("place-1", "Wat Pho", 13.7465, 100.4927, null,
                PlaceCategory.Other, null, null, null, PlaceDerivedFrom.ExactPlaceId));

        var dto = await BuildFor(me, resolver, db)
            .Handle(new ResolvePlaceCommand("https://maps.app.goo.gl/abc"), default);

        dto.AlreadySaved.Should().NotBeNull();
        dto.AlreadySaved!.Name.Should().Be("Wat Pho");
        // Library-level: the Trips it sits on, because the target Trip is unknown here.
        dto.AlreadySaved.TripNames.Should().Contain("เที่ยวกรุงเทพ");
    }

    [Fact]
    public async Task ThePlaceItselfNeverAlsoAppearsAsANearMatch()
    {
        // The same real-world place normally has one TripPlace per Trip. All of
        // them sit at identical coordinates, so any that are not excluded come
        // back as 0 m "near matches" — telling the user the place is already
        // saved AND warning them about copies of that very place.
        using var db = new SqliteAppDbContext();
        var me = Guid.NewGuid();
        SeedUserWithPlaces(db, me,
            ("Wat Pho", 13.7465, 100.4927, "place-1", "เที่ยวกรุงเทพ"),
            ("Wat Pho", 13.7465, 100.4927, "place-1", "ไหว้พระ"),
            ("Wat Pho", 13.7465, 100.4927, "place-1", "ทริปกิน"));

        var resolver = new Mock<IPlaceResolver>();
        resolver.Setup(r => r.ResolveFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlaceDto("place-1", "Wat Pho", 13.7465, 100.4927, null,
                PlaceCategory.Other, null, null, null, PlaceDerivedFrom.ExactPlaceId));

        var dto = await BuildFor(me, resolver, db)
            .Handle(new ResolvePlaceCommand("https://maps.app.goo.gl/abc"), default);

        dto.AlreadySaved!.TripNames.Should().HaveCount(3);
        dto.NearMatches.Should().BeEmpty();
    }

    [Fact]
    public async Task NearMatchesAreTheNearestThreeWithin100mAndNeverBlock()
    {
        using var db = new SqliteAppDbContext();
        var me = Guid.NewGuid();
        // 4 places inside 100 m and 1 well outside it.
        SeedUserWithPlaces(db, me,
            ("A", 13.75600, 100.50180, null, "t"), ("B", 13.75610, 100.50180, null, "t"),
            ("C", 13.75620, 100.50180, null, "t"), ("D", 13.75630, 100.50180, null, "t"),
            ("Far", 13.80000, 100.50180, null, "t"));
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);

        var dto = await BuildFor(me, resolver, db)
            .Handle(new ResolvePlaceCommand("13.7560, 100.5018"), default);

        dto.NearMatches.Should().HaveCount(3);
        dto.NearMatches!.Select(n => n.Name).Should().ContainInOrder("A", "B", "C");
        dto.NearMatches.Should().NotContain(n => n.Name == "Far");
        dto.NearMatches.Should().BeInAscendingOrder(n => n.DistanceMeters);
    }

    [Fact]
    public async Task NearMatchesIgnoreNamesEntirely()
    {
        using var db = new SqliteAppDbContext();
        var me = Guid.NewGuid();
        // Same name, 5 km away: it must NOT be reported. No fuzzy name matching (R3.5).
        SeedUserWithPlaces(db, me, ("ร้านเดิม", 13.80000, 100.50180, null, "t"));
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);

        var dto = await BuildFor(me, resolver, db)
            .Handle(new ResolvePlaceCommand("13.7560, 100.5018"), default);

        dto.NearMatches.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherUsersPlaceIsNeverScanned()
    {
        using var db = new SqliteAppDbContext();
        SeedUserWithPlaces(db, Guid.NewGuid(), ("Someone else's", 13.75600, 100.50180, "place-1", "t"));
        var me = Guid.NewGuid(); // empty library
        var resolver = new Mock<IPlaceResolver>(MockBehavior.Strict);

        var dto = await BuildFor(me, resolver, db)
            .Handle(new ResolvePlaceCommand("13.7560, 100.5018"), default);

        dto.NearMatches.Should().BeEmpty();
        dto.AlreadySaved.Should().BeNull();
    }
```

- [ ] **Step 6: Run them to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ResolvePlaceHandlerTests`
Expected: FAIL — `AlreadySaved` and `NearMatches` are null because the handler still returns `resolved` unannotated.

- [ ] **Step 7: Annotate in the handler**

In `ResolvePlaceHandler.Handle`, replace `return resolved;` with `return await AnnotateAsync(resolved, user.Id, ct);`, add `using Microsoft.EntityFrameworkCore;`, and add:

```csharp
    private const int NearMatchRadiusMeters = 100;
    private const int NearMatchLimit = 3;

    /// <summary>
    /// R3.1 / R3.5 / R12.3 — duplicate awareness fires at RESOLVE time, before the
    /// capture form renders, so the user never types enrichment that would then be
    /// discarded. Library-level (all of the caller's live Trips), never per-trip:
    /// at resolve time neither surface knows the target Trip yet.
    /// </summary>
    private async ValueTask<ResolvedPlaceDto> AnnotateAsync(
        ResolvedPlaceDto dto, Guid userId, CancellationToken ct)
    {
        var mine = await (from p in _db.TripPlaces
                          join t in _db.Trips on p.TripId equals t.Id
                          where t.UserId == userId && t.DeletedAt == null
                          select new { p.Id, p.Name, p.Lat, p.Lng, p.GooglePlaceId, TripName = t.Name })
                         .ToListAsync(ct);

        AlreadySavedDto? already = null;
        // EVERY row for this place_id, not just the first: one real-world place
        // normally has one TripPlace per Trip it sits on — that is the whole
        // reason ADR-156 exists. Excluding only hits[0] below would leave the
        // siblings to reappear as "near matches" at 0 m, so the user is told the
        // place is already saved AND warned about N copies of that same place.
        var alreadyIds = new HashSet<Guid>();
        if (!string.IsNullOrEmpty(dto.GooglePlaceId))
        {
            var hits = mine.Where(m => m.GooglePlaceId == dto.GooglePlaceId).ToList();
            if (hits.Count > 0)
            {
                foreach (var h in hits) alreadyIds.Add(h.Id);
                already = new AlreadySavedDto(
                    hits[0].Id, hits[0].Name, hits.Select(h => h.TripName).Distinct().ToList());
            }
        }

        // Distance only — the name is displayed so the user can judge, but it takes
        // no part in the predicate. No fuzzy matching over freeform Thai names.
        var near = mine
            .Select(m => new
            {
                m.Id, m.Name, m.Lat, m.Lng,
                D = GeoDistance.MetersBetween(dto.Lat, dto.Lng, m.Lat, m.Lng),
            })
            .Where(m => m.D <= NearMatchRadiusMeters && !alreadyIds.Contains(m.Id))
            .OrderBy(m => m.D)
            .Take(NearMatchLimit)
            .Select(m => new NearMatchDto(m.Id, m.Name, m.Lat, m.Lng, (int)Math.Round(m.D)))
            .ToList();

        return dto with { AlreadySaved = already, NearMatches = near };
    }
```

- [ ] **Step 8: Run the whole suite**

Run: `cd backend && dotnet test`
Expected: all green. If DI fails to construct `ResolvePlaceHandler`, confirm `IApplicationDbContext` is registered in the McpServer host too — it resolves the same handlers as the WebApi.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/GeoDistance.cs \
        backend/src/MenuNest.Application/UseCases/Trips/ResolvePlace/ResolvePlaceHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Places/GeoDistanceTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/ResolvePlaceHandlerTests.cs
git commit -m "feat(trips): resolve_place reports already-saved and near matches (#48)"
```

---

## Task 6: list_my_places over MCP

**Files:**
- Create: `backend/src/MenuNest.McpServer/Tools/PlaceTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs:17`
- Test: `backend/tests/MenuNest.McpServer.UnitTests/Tools/PlaceToolsTests.cs`

**Interfaces:**
- Consumes: `ListMyPlacesQuery()` (parameterless, already exists) → `IReadOnlyList<DiscoverPlaceDto>`.
- Produces: MCP tool `list_my_places`.

**Why:** R12.5 — `list_my_places` returns the same grouped `DiscoverPlaceDto` the SPA reads, with the already-flattened `OriginTripPlaceId`, added as a **new `PlaceTools` type** registered alongside the existing seven. Bounding the payload (a `search`/`near` parameter, or paging) is explicitly out of scope — `ListMyPlacesQuery` is parameterless and Discover scopes by viewport client-side.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.McpServer.UnitTests/Tools/PlaceToolsTests.cs`, mirroring the delegation style of the existing `TripToolsTests.cs` (read it first):

```csharp
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Places;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using MenuNest.McpServer.Tools;
using Moq;
using Xunit;

namespace MenuNest.McpServer.UnitTests.Tools;

public class PlaceToolsTests
{
    [Fact]
    public async Task list_my_places_delegates_to_the_query()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ListMyPlacesQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DiscoverPlaceDto>)Array.Empty<DiscoverPlaceDto>());

        var result = await new PlaceTools(mediator.Object).list_my_places(default);

        result.Should().BeEmpty();
        mediator.Verify(m => m.Send(It.IsAny<ListMyPlacesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests --filter FullyQualifiedName~PlaceToolsTests`
Expected: FAIL to compile — `PlaceTools` does not exist.

- [ ] **Step 3: Write the tool**

Create `backend/src/MenuNest.McpServer/Tools/PlaceTools.cs`. Copy the `using`/attribute header of `TripTools.cs` verbatim so the implicit-usings assumptions match:

```csharp
using MenuNest.Application.UseCases.Places;
using MenuNest.Application.UseCases.Places.ListMyPlaces;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class PlaceTools(IMediator mediator)
{
    [McpServerTool, Description("List every place the current user has saved, across all of their trips, grouped so one real-world place is one entry no matter how many trips reference it. This is the same library the 'ไปไหนดี' (Discover) screen reads. Use it to check whether a place is already saved before resolving or adding it. Returns each grouped place with its coordinates, category, opening hours, best-time windows, season periods, review links, notes, and the trips it appears on.")]
    public async Task<IReadOnlyList<DiscoverPlaceDto>> list_my_places(CancellationToken ct)
        => await mediator.Send(new ListMyPlacesQuery(), ct);
}
```

- [ ] **Step 4: Register it**

In `backend/src/MenuNest.McpServer/McpServerRegistration.cs`, after line 17:

```csharp
            .WithTools<Tools.TripTools>()
            .WithTools<Tools.PlaceTools>()
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/PlaceTools.cs \
        backend/src/MenuNest.McpServer/McpServerRegistration.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/PlaceToolsTests.cs
git commit -m "feat(mcp): expose the saved-place library as list_my_places (#48)"
```

---

## Task 7: Close the third wire — originTripPlaceId over MCP, and the read-back instruction

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:81-85` (description)
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:94-107` (`add_trip_place`)
- Test: `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs` (extend)

**Interfaces:**
- Consumes: `AddTripPlaceCommand` (Plan A), `PlaceDerivedFrom` (Task 4).
- Produces: nothing downstream.

**Why:** R12.6 — `add_trip_place` must expose `originTripPlaceId` and pass it straight through. **It does not today.** `AddTripPlaceCommand`'s five trailing members are all defaulted — deliberately, because its own doc comment records 10 positional construction sites that must not break — so `TripTools.add_trip_place` compiles while silently sending `null`, and the compiler cannot flag it. The consequence is exactly the defect ADR-156 exists to prevent: a place captured through MCP splits into a second Discover card instead of grouping with its origin.

This is the same class Plan A shipped and its final review caught — a new command member with no wire behind it. Plan A fixed the HTTP body (`c472df5`); the MCP tool is the third wire nobody checked.

R12.4 also needs `resolve_place`'s description to carry the read-back instruction, since a tool description is the only place an agent reads policy.

- [ ] **Step 1: Write the failing test**

Append to `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs`:

```csharp
    [Fact]
    public async Task add_trip_place_passes_originTripPlaceId_through()
    {
        // R12.6. Without this the MCP capture path splits a copied place into a
        // second Discover card — the exact defect ADR-156 exists to prevent. The
        // command's trailing members are all defaulted, so nothing but this test
        // can catch the omission.
        var origin = Guid.NewGuid();
        AddTripPlaceCommand? sent = null;
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<AddTripPlaceCommand>(), It.IsAny<CancellationToken>()))
                .Callback<AddTripPlaceCommand, CancellationToken>((c, _) => sent = c)
                .ReturnsAsync(default(TripPlaceDto)!);

        await new TripTools(mediator.Object).add_trip_place(
            tripId: Guid.NewGuid(), name: "Wat Pho", lat: 13.7465, lng: 100.4927,
            category: PlaceCategory.See, googlePlaceId: "place-1", address: null,
            priceLevel: null, photoUrl: null, openingHoursJson: null,
            originTripPlaceId: origin, ct: default);

        sent.Should().NotBeNull();
        sent!.OriginTripPlaceId.Should().Be(origin);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests --filter FullyQualifiedName~TripToolsTests`
Expected: FAIL to compile — `add_trip_place` has no `originTripPlaceId` parameter.

- [ ] **Step 3: Add the parameter and pass it through**

In `TripTools.cs`, add before `CancellationToken ct` in `add_trip_place`:

```csharp
        [Description("When this place is being copied from one the user already saved (e.g. an entry returned by list_my_places), pass THAT place's id here so both stay ONE entry in Discover instead of splitting into two cards. Omit for a brand-new place.")] Guid? originTripPlaceId,
```

and name the member in the `Send` call — the record's doc comment requires by-name construction for the trailing five:

```csharp
        => await mediator.Send(new AddTripPlaceCommand(
            tripId, name, lat, lng, category, googlePlaceId, address, priceLevel, photoUrl, openingHoursJson,
            OriginTripPlaceId: originTripPlaceId), ct);
```

- [ ] **Step 4: Rewrite the resolve_place description (R12.4)**

Replace the `[Description(...)]` on `resolve_place`:

```csharp
    [McpServerTool, Description("Resolve one input to a place snapshot. Accepts a Google Maps URL (resolved live against Google), a \"lat, lng\" pair such as \"13.7563, 100.5018\" (used verbatim, no lookup), or a Plus Code (decoded offline). To search by name, build the URL as https://www.google.com/maps/place/<url-encoded name and city>/. The result carries derivedFrom: ExactPlaceId and PlusCodeFull are trustworthy; NameSearch may be a DIFFERENT BRANCH of a chain, PlusCodeShort may be far from the intended place, and CoordinateVerbatim is exactly what you supplied. Whenever derivedFrom is not ExactPlaceId you MUST read the resolved name and address back to the user and get their reply before calling add_trip_place. The result also carries alreadySaved (this place is already in the user's library — say so instead of adding it again) and nearMatches (up to 3 saved places within 100 m — mention them; they do not block). Feed the result into add_trip_place; never fabricate coordinates yourself.")]
```

- [ ] **Step 5: Run the whole backend suite**

Run: `cd backend && dotnet test`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs
git commit -m "fix(mcp): add_trip_place carries originTripPlaceId, resolve_place states its trust level (#48)"
```

---

## Task 8: Whole-plan review and manual verification

**Files:** none — this task produces evidence, not code.

- [ ] **Step 1: Run every gate**

```bash
cd backend && dotnet test
cd ../frontend && npx tsc --noEmit && npx vitest run && npm run build
```

- [ ] **Step 2: Prove the ccTLD fix against the real resolver path**

The unit test asserts the allowlist; this asserts the whole chain. With the API running locally and a bearer token in `$TOKEN`:

```bash
curl -s -X POST http://localhost:5000/api/trips/resolve-place \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $TOKEN" \
  -d '{"input":"https://maps.google.co.th/maps/place/Wat+Pho/"}'
```

Expected: a resolved place, not a 400. Before this plan the same request was rejected by validation.

- [ ] **Step 3: Prove the two no-cost paths make no Google call**

```bash
curl -s -X POST http://localhost:5000/api/trips/resolve-place -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" -d '{"input":"13.7563, 100.5018"}'
curl -s -X POST http://localhost:5000/api/trips/resolve-place -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" -d '{"input":"7P52PJ88+8G"}'
```

Expected: both return coordinates with `derivedFrom` of `CoordinateVerbatim` / `PlusCodeFull` and an empty `name`. Then confirm **no** outbound Google dependency was recorded for either (R7.2):

```bash
az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 \
  --analytics-query "AppDependencies | where TimeGenerated > ago(15m) | where Target has 'googleapis' | project TimeGenerated, Name, Target" -o json
```

- [ ] **Step 4: Interactively verify the one SPA caller before pushing**

Plan B adds no new UI — the capture surface that consumes `derivedFrom` / `alreadySaved` / `nearMatches` is Plan C. But Task 3 changes the request body of `PlaceLinkFallbackDialog`, and the SPA has no DOM test harness: `tsc` cannot see a wrong body. Paste a Google Maps link into that dialog and confirm it still resolves before pushing.

- [ ] **Step 5: Request review**

Use `superpowers:requesting-code-review` for a whole-plan review, then `dev-workflows:scrutinize`. On Plan A, `/scrutinize` found a real spec defect that the per-task reviews, the whole-branch review and 659 backend tests all passed.

---

## Self-Review

**Spec coverage**

| requirement | task |
|---|---|
| R3.1 exact `place_id` detected at resolve time | 5 |
| R3.5 near match ≤100 m, nearest 3, name not in the predicate, non-blocking | 5 |
| R5.1 searchText cannot resolve Plus Codes | 2 (motivation; no code) |
| R5.2 offline `open-location-code` decode, short code needs a locality | 2 (decode), 4 (a short code is **refused** here — see below) |
| R6.1 URL shapes | out of scope by R6.3 — untouched, and Task 4 discloses it via `NameSearch` |
| R6.2 Google ccTLD short links rejected in prod | 1 |
| R7.2 coordinates/Plus Codes cost nothing | 2, 4, 8 (verified) |
| R12.1 `url` → `input`, three shapes, validator widened | 3 |
| R12.2 no capture tool added | honoured — this plan adds no commit tool anywhere |
| R12.3 `alreadySaved` + `nearMatches`, handler gains `IApplicationDbContext` | 4 (field), 5 (behaviour) |
| R12.4 `derivedFrom` + the read-back instruction | 4 (enum), 7 (instruction) |
| R12.5 `list_my_places` as a new `PlaceTools` | 6 |
| R12.6 `add_trip_place` exposes `originTripPlaceId` | 7 |

**Two things Plan C must pick up, flagged so neither plan assumes the other did:**

1. **R4.2's best-effort reverse geocode** — it prefills the capture *form*, which is Plan C's surface, and R4.3 requires it never block capture.
2. **The short Plus Code reference point.** Plan B deliberately **refuses** a short code rather than recovering it against a default, because a wrong-locality recovery succeeds and returns a confidently wrong point that `derivedFrom` can only label, never block — the exact failure R5.2 exists to prevent. Plan C is the first surface that knows a locality, so it must add an optional reference to `ResolvePlaceCommand` and only then may `PlaceDerivedFrom.PlusCodeShort` ever be emitted. Until it does, a short code is a validation error with a Thai-worded message from the SPA.

**Corrections applied after a `/scrutinize` pass over the first draft of this plan** (all three were caught before any of it was executed):

- Task 4 originally decoded a short Plus Code against `(0, 0)` and merely tagged it `PlusCodeShort`. `"PJ88+8G"` recovered that way lands in the Gulf of Guinea — ~7,000 km from the Bangkok a Thai user meant — and the label does not stop a save. Now refused.
- Task 5's near-match filter excluded only `hits[0]`, but one real-world place normally has one `TripPlace` per Trip it sits on, so the siblings returned as 0 m "near matches" beside the already-saved banner. Now excludes every matching row, with a test that seeds one place across three trips.
- Task 1's ccTLD regex admits `google.<any 2-3 letter TLD>`; the residual is now written down in the code comment as an accepted trade rather than left implicit.

**Placeholder scan** — every step carries the literal code or command to run; no "add error handling", no "similar to Task N", no undefined types. Two places deliberately tell the executor to read an existing file and mirror it rather than supplying code: Task 4 Step 4 (the real `place_id` local's name inside `GooglePlaceResolver`) and Task 5 Step 5 (the seeding helpers, mirrored from `ListMyPlacesHandlerTests.cs`). Both are cases where inventing a name would be worse than reading the one that exists.

**Type consistency** — `PlaceInputKind` / `PlaceInputParse` / `PlaceInput.Parse` (Task 3) are used unchanged in Task 4. `PlusCodeKind` / `Classify` (Task 2) are used unchanged in Task 3, and `DecodeFull` in Task 4; `DecodeShort` is built and tested in Task 2 but has **no caller inside this plan** — it exists for Plan C, which supplies the reference point a short code needs. `PlaceDerivedFrom` / `AlreadySavedDto` / `NearMatchDto` (Task 4) are used unchanged in Tasks 5 and 7. `GeoDistance.MetersBetween` (Task 5) has one caller. `ResolvePlaceHandler`'s constructor gains its fourth argument in Task 4, and Task 5's `BuildFor` passes four.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-14-discover-capture-b-resolver-mcp.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.
