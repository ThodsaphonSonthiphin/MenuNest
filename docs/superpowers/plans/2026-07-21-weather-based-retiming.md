# Weather-based Retiming (จัดเวลาตามอากาศ) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Issue:** [#46 "show the possible heat"](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/46)
**Spec:** `docs/superpowers/specs/2026-07-21-weather-based-retiming-design.md` · **ADRs:** 112–121 (see *Pre-flight* — renumber before merge)

**Goal:** On a Stop's detail sheet, show a per-hour weather forecast for the Stop's location and let the user tap a target hour (or "coolest daytime / nighttime") to one-tap re-time the plan so the Stop *arrives* at that hour.

**Architecture:** A new `IWeatherService.GetHourlyAsync` returns an ordered hourly series from the **same** Google `forecast/hours:lookup` the On-arrival reading already walks (no new billing SKU, no schema change). A coords-based query serves the web hourly view. Retiming is **display + a suggested, confirmed write** that only shifts existing inputs — the anchor Day's start time (ADR-113), and for a cross-day target the whole `Trip.StartDate` (ADR-114) — then pins the Day by turning off `UseCurrentTimeAsStart` (ADR-115). No per-Stop time is ever stored; arrival stays derived (ADR-008).

**Tech Stack:** .NET 10, martinothamar/**Mediator** (source-gen, NOT MediatR), FluentValidation, EF Core 10; React + Redux Toolkit Query, Vitest (node env, no DOM). Backend tests: xUnit + Moq + FluentAssertions.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **No DB schema change, no EF migration.** The feature reuses `Trips`, `ItineraryDays`, `Stops` and existing write paths. Do **not** add a `DbSet<>` or touch the three `IApplicationDbContext` implementers.
- **Mediator is martinothamar/Mediator, source-generated.** Use `ICommand<T>`/`IQuery<T>` on the record, `ICommandHandler<TCmd,TRes>`/`IQueryHandler<TQry,TRes>` on the handler, `public async ValueTask<TRes> Handle(TReq x, CancellationToken ct)`, and `Unit`/`Unit.Value` for no-payload results. Handlers and validators are **auto-registered** (`AddMediator` + `AddValidatorsFromAssembly`) — no manual DI. In tests, `IMediator.Send` returns `ValueTask<T>` — stub with `new ValueTask<T>(x)`, **not** `.ReturnsAsync`.
- **`IWeatherService` has THREE implementers.** Adding `GetHourlyAsync` to the interface breaks the build (CS0535) until it is implemented in `GoogleWeatherService`, `MissingConfigWeatherService`, **and** the inline `StubWeather` test double in `GetStopWeatherHandlerTests.cs`. All three must land in the **same commit** (pre-commit runs the full suite).
- **Ownership scoping is mandatory** on every DB-touching handler: `var user = await _users.GetOrProvisionCurrentAsync(ct);` then constrain the EF query up the chain with nested `.Any(...)` including `t.UserId == user.Id && t.DeletedAt == null`. Signal not-found / invalid input by **throwing `DomainException`** — there is no `Result<T>` type.
- **Weather never throws** (ADR-030): any Google failure/timeout degrades to no-data (empty list for hourly). Honour real caller cancellation only.
- **Google horizon = 240 h** (`hours=241` → HTTP 400). Clamp/validate `hours` to `[1, 240]`.
- **Commit style:** conventional commit + issue ref. Closing commit ends `(closes #46)`; partials use `(#46)`. Stage narrowly (`git add <explicit paths>`) — never `git add -A`; never sweep `daily-state.md` / `AGENTS.md`.
- **Pre-commit hook runs the FULL suite** (backend `dotnet build`+`test` Release, frontend `tsc --noEmit`+`npm run build`+vitest, ~40s). Every commit must leave the whole suite green. Do **not** `--no-verify`.
- **Git remote is named `main`**, not `origin`. Push with `git push main HEAD:<branch>`.
- **Frontend has no component/visual test harness** (vitest `environment: 'node'`, no jsdom/RTL). Pure logic goes in `lib/` with a `*.test.ts`; components/hooks are **verified interactively** (see *Finishing*). Prod deploys on push to `main` — smoke-test any UI change before pushing.

### Design note — deviation from spec §4.2 (READ THIS)

The spec's §4.2 pseudocode computes the retiming offset **server-side** ("it owns legs/dwell"). Codebase reality (verified): **arrival is computed only on the client** (`useSchedule.ts` `computeSchedule`) — the backend emits raw ingredients (`legToReach.seconds`, `dwellMinutes`, `dayStartTime`) and never accumulates an arrival. Worse, the client's first leg is an **approach leg** derived from the viewer's *live GPS* (`GetItineraryHandler` builds it only when `ViewerLat/Lng` is supplied), which the server cannot reproduce.

Therefore retiming is split:
- **Web path** — the client already holds the exact schedule, so it computes the new day-start (`target − offset`) and the target date, and passes them to a thin apply command. This is authoritative and free.
- **MCP path** — an AI caller has no client schedule and no live location, so its command resolves the offset server-side from **inter-stop legs only** (no approach leg — correct for a location-less caller) and resolves `coolest*` targets via the new hourly read, then delegates to the same apply command.

Both converge on one **apply core**: `RetimeStopToHourCommand(newDayStartTime, newAnchorDate)` → `SetStartTime` + `SetUseCurrentTimeAsStart(false)` (+ cross-day `Trip.Reschedule` + realign), all in one `SaveChanges`. **Update spec §4.2 to reflect this split** when committing the docs (Pre-flight).

---

## File Structure

**Backend — create:**
- `backend/src/MenuNest.Application/UseCases/Trips/GetHourlyForecast/GetHourlyForecastQuery.cs` · `…Handler.cs` · `…Validator.cs` — coords-based hourly read (web).
- `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourCommand.cs` · `…Handler.cs` · `…Validator.cs` — apply core (web + shared).
- `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToWeather/RetimeStopToWeatherCommand.cs` · `…Handler.cs` · `…Validator.cs` · `RetimeTarget.cs` · `WeatherHourSelection.cs` (pure `CoolestHour` helper) — MCP resolver (Phase 1b).
- `backend/src/MenuNest.Application/UseCases/Trips/GetStopHourlyForecast/GetStopHourlyForecastQuery.cs` · `…Handler.cs` · `…Validator.cs` — stop-based hourly read (MCP, Phase 1b).

**Backend — modify:**
- `backend/src/MenuNest.Application/Abstractions/IWeatherService.cs` — add `HourlyReading` record + `GetHourlyAsync`.
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — add `HourlyReadingDto`, `RetimeResultDto`, `RetimeTargetDto`.
- `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs` — implement `GetHourlyAsync` (+ `ParseHourly`, hourly cache key).
- `backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs` — implement `GetHourlyAsync` → empty list.
- `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — `POST api/trips/weather/hourly` + `POST api/trips/{tripId}/days/{dayId}/retime` (+ `RetimeBody` record).
- `backend/src/MenuNest.McpServer/Tools/TripTools.cs` — `get_stop_hourly_forecast`, `retime_stop_to_weather` (Phase 1b).

**Backend — tests:** new `…/Trips/GetHourlyForecastHandlerTests.cs`, `RetimeStopToHourHandlerTests.cs`, `RetimeStopToHourRelationalTests.cs`, `RetimeStopToWeatherHandlerTests.cs`, `WeatherHourSelectionTests.cs`, `GetStopHourlyForecastHandlerTests.cs`; extend `…/Maps/GoogleWeatherServiceTests.cs`, `MissingConfigWeatherServiceTests.cs`, `GetStopWeatherHandlerTests.cs` (StubWeather), `…/Tools/TripToolsTests.cs`.

**Frontend — create:** `frontend/src/pages/trips/lib/retiming.ts` + `retiming.test.ts`; `frontend/src/pages/trips/components/HourlyPlanner.tsx`.
**Frontend — modify:** `frontend/src/shared/api/api.ts` (types + endpoint + mutation + hook exports); `frontend/src/pages/trips/components/WeatherIcons.tsx` (a `ClockIcon` glyph); `frontend/src/pages/trips/components/StopDetailSheet.tsx` (entry button + planner); `frontend/src/pages/trips/components/ItineraryTab.tsx` (prop wiring); `frontend/src/pages/trips/TripDetailPage.css` (`.sd-hourly*` styles).

---

## Pre-flight (setup — not TDD; do once before Task 1)

- [ ] **P1. Reconcile the worktree with `main`.** This branch (`worktree-issue-46-hourly-temperature`, HEAD `18f4ffa`) is ~9 commits behind `main` and its ADRs 112–121 **collide** with `main`'s #41 ADRs 107–111. Fetch and rebase (or merge) `main`:
  ```bash
  cd "C:/Repo2/t/menunest/.claude/worktrees/issue-46-hourly-temperature"
  git fetch main
  git rebase main/main   # or: git merge main/main — pick per your workflow
  ```
  Resolve any conflicts (unlikely in weather/trip code; #41 was version-display). If you prefer to keep the design docs uncommitted through the rebase, `git stash -u` first.
- [ ] **P2. Renumber ADRs to the next free block.** After rebasing, find the highest ADR on `main` (`ls docs/adr/ | sort -n | tail`) and renumber this branch's `107..116` → the next free contiguous block (expected `112..121`). Update the filenames, each ADR's internal cross-refs, and the spec header (`**ADRs:** 112–121`) + the "Design note" reference above.
- [ ] **P3. Add a short ADR for the resolve-split** (the deviation above): "Retiming offset resolved client-side (web, authoritative legs incl. approach) / server-side (MCP, inter-stop legs only)". Amend spec §4.2 to match.
- [ ] **P4. Commit the design docs** (they are currently untracked — SDD commits only code):
  ```bash
  git add docs/adr/ docs/superpowers/specs/2026-07-21-weather-based-retiming-design.md CONTEXT.md docs/superpowers/plans/2026-07-21-weather-based-retiming.md
  git commit -m "docs(trips): ADRs + design spec/plan for weather-based retiming (#46)"
  ```

---

## Task 1: Hourly weather read — `IWeatherService.GetHourlyAsync`

Adds a true hourly series from the existing `forecast/hours:lookup`. Interface change → all three implementers in one commit.

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/IWeatherService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs` (StubWeather)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/MissingConfigWeatherServiceTests.cs`

**Interfaces:**
- Produces: `record HourlyReading(DateTime DisplayLocal, bool IsDaytime, double? TempC, double? FeelsLikeC, string? ConditionType, string? IconBaseUri, int? RainPct, int? UvIndex)` and `Task<IReadOnlyList<HourlyReading>> IWeatherService.GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)` (both in namespace `MenuNest.Application.Abstractions`).

- [ ] **Step 1: Write the failing test** — add to `GoogleWeatherServiceTests.cs` (reuses its `StubHandler`/`StubFactory`/`Build`):

```csharp
// Google forecast/hours buckets carry a per-hour isDaytime flag; the hourly read must surface it
// (plus feels-like), parse in order, and degrade to empty on failure (ADR-030).
private const string HourlyJson =
    "{\"forecastHours\":[" +
    "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13},\"isDaytime\":true," +
    "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\",\"type\":\"CLOUDY\"}," +
    "\"temperature\":{\"degrees\":34.0},\"feelsLikeTemperature\":{\"degrees\":39.4},\"uvIndex\":8,\"precipitation\":{\"probability\":{\"percent\":20}}}," +
    "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":22},\"isDaytime\":false," +
    "\"weatherCondition\":{\"type\":\"CLEAR\"}," +
    "\"temperature\":{\"degrees\":28.0},\"feelsLikeTemperature\":{\"degrees\":30.0},\"uvIndex\":0,\"precipitation\":{\"probability\":{\"percent\":5}}}" +
    "]}";

[Fact]
public async Task Hourly_parses_isDaytime_feelslike_and_orders_buckets()
{
    var svc = Build(new StubHandler(HttpStatusCode.OK, HourlyJson));
    var pt = new WeatherPoint("", 13.7563, 100.5018, null);

    var hours = await svc.GetHourlyAsync(pt, 48, CancellationToken.None);

    hours.Should().HaveCount(2);
    hours[0].DisplayLocal.Should().Be(new DateTime(2026, 7, 12, 13, 0, 0));
    hours[0].IsDaytime.Should().BeTrue();
    hours[0].TempC.Should().Be(34.0);
    hours[0].FeelsLikeC.Should().Be(39.4);
    hours[0].ConditionType.Should().Be("CLOUDY");
    hours[1].IsDaytime.Should().BeFalse();
    hours[1].FeelsLikeC.Should().Be(30.0);
}

[Fact]
public async Task Hourly_failure_degrades_to_empty_list()
{
    var svc = Build(new StubHandler(HttpStatusCode.InternalServerError));
    (await svc.GetHourlyAsync(new WeatherPoint("", 13.75, 100.50, null), 48, CancellationToken.None))
        .Should().BeEmpty();
}
```

- [ ] **Step 2: Run test to verify it fails** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GoogleWeatherServiceTests.Hourly"` · Expected: **compile error** — `GetHourlyAsync`/`HourlyReading` do not exist.

- [ ] **Step 3a: Add the interface member + record** in `IWeatherService.cs` (next to `WeatherReading`):

```csharp
/// <summary>One hour of a location's forecast. DisplayLocal is the Google bucket's local wall-clock hour;
/// IsDaytime is Google's per-hour flag (sunrise-inclusive → sunset-exclusive).</summary>
public sealed record HourlyReading(
    DateTime DisplayLocal, bool IsDaytime,
    double? TempC, double? FeelsLikeC,
    string? ConditionType, string? IconBaseUri,
    int? RainPct, int? UvIndex);

public interface IWeatherService
{
    Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct);

    /// <summary>Ordered hourly forecast for a single point, up to min(hours, 240). Reuses the same
    /// forecast/hours:lookup walk as On-arrival (no new billing SKU). Degrades to an empty list, never throws (ADR-030).</summary>
    Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct);
}
```

- [ ] **Step 3b: Implement in `GoogleWeatherService.cs`.** Reuse `GetJsonAsync`, the page-walk shape, and `BucketHour`; add a `ParseHourly` and an hourly cache key. Insert the method and helper:

```csharp
public async Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
{
    var want = Math.Clamp(hours, 1, 240);
    var cacheKey = $"wx:Hourly:{point.Lat:F5},{point.Lng:F5}:{want}";
    if (_cache.TryGetValue(cacheKey, out IReadOnlyList<HourlyReading>? cached) && cached is not null) return cached;

    var client = _http.CreateClient();
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
    var result = new List<HourlyReading>();
    try
    {
        string? pageToken = null;
        for (var page = 0; page < MaxForecastPages && result.Count < want; page++)
        {
            var url = $"https://weather.googleapis.com/v1/forecast/hours:lookup?location.latitude={point.Lat}&location.longitude={point.Lng}&hours=240&unitsSystem=METRIC&languageCode=th";
            if (pageToken is not null) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var doc = await GetJsonAsync(client, url, timeoutCts.Token);
            if (doc.RootElement.TryGetProperty("forecastHours", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucket in arr.EnumerateArray())
                {
                    var reading = ParseHourly(bucket);
                    if (reading is not null) result.Add(reading);
                    if (result.Count >= want) break;
                }
            }
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var tok) ? tok.GetString() : null;
            if (string.IsNullOrEmpty(pageToken)) break;
        }
    }
    catch (Exception ex)
    {
        ct.ThrowIfCancellationRequested();
        _log.LogWarning(ex, "Hourly forecast lookup failed for {Lat},{Lng}; returning empty.", point.Lat, point.Lng);
        return Array.Empty<HourlyReading>();
    }

    IReadOnlyList<HourlyReading> ordered = result.OrderBy(h => h.DisplayLocal).ToList();
    if (ordered.Count > 0) _cache.Set(cacheKey, ordered, TimeSpan.FromHours(3));
    return ordered;
}

private static HourlyReading? ParseHourly(JsonElement el)
{
    var when = BucketHour(el);
    if (when is not { } local) return null;
    string? type = null, icon = null;
    if (el.TryGetProperty("weatherCondition", out var wc))
    {
        if (wc.TryGetProperty("type", out var t)) type = t.GetString();
        if (wc.TryGetProperty("iconBaseUri", out var ib)) icon = ib.GetString();
    }
    double? temp = el.TryGetProperty("temperature", out var tp) && tp.TryGetProperty("degrees", out var dg) ? dg.GetDouble() : null;
    double? feels = el.TryGetProperty("feelsLikeTemperature", out var fl) && fl.TryGetProperty("degrees", out var fd) ? fd.GetDouble() : null;
    int? rain = el.TryGetProperty("precipitation", out var pr) && pr.TryGetProperty("probability", out var pb) && pb.TryGetProperty("percent", out var pc) ? pc.GetInt32() : null;
    int? uv = el.TryGetProperty("uvIndex", out var uvi) && uvi.ValueKind == JsonValueKind.Number ? uvi.GetInt32() : null;
    bool day = el.TryGetProperty("isDaytime", out var idt) && idt.ValueKind == JsonValueKind.True;
    return new HourlyReading(local, day, temp, feels, type, icon, rain, uv);
}
```

- [ ] **Step 3c: Implement in `MissingConfigWeatherService.cs`:**

```csharp
public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
    => Task.FromResult<IReadOnlyList<HourlyReading>>(Array.Empty<HourlyReading>());
```

- [ ] **Step 3d: Implement in the `StubWeather` test double** (`GetStopWeatherHandlerTests.cs`) so the project compiles:

```csharp
public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
    => Task.FromResult<IReadOnlyList<HourlyReading>>(Array.Empty<HourlyReading>());
```

- [ ] **Step 4: Add the MissingConfig assertion** to `MissingConfigWeatherServiceTests.cs`:

```csharp
[Fact]
public async Task GetHourlyAsync_returns_empty_without_a_key()
    => (await new MissingConfigWeatherService()
        .GetHourlyAsync(new WeatherPoint("", 13.75, 100.50, null), 48, CancellationToken.None))
        .Should().BeEmpty();
```

- [ ] **Step 5: Run tests to verify they pass** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~Weather"` · Expected: **PASS** (all weather tests, incl. existing ones).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/IWeatherService.cs \
        backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs \
        backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/Maps/MissingConfigWeatherServiceTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs
git commit -m "feat(weather): add IWeatherService.GetHourlyAsync hourly series incl. isDaytime (#46)"
```

---

## Task 2: `GetHourlyForecast` query + `POST /api/trips/weather/hourly`

Coords-based hourly read for the web planner. Pure (no DbContext), mirrors `GetStopWeather`.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/GetHourlyForecast/GetHourlyForecastQuery.cs`, `…Handler.cs`, `…Validator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (add `HourlyReadingDto`)
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetHourlyForecastHandlerTests.cs`

**Interfaces:**
- Consumes: `IWeatherService.GetHourlyAsync` + `HourlyReading` (Task 1).
- Produces: `record HourlyReadingDto(DateTime DisplayLocal, bool IsDaytime, double? TempC, double? FeelsLikeC, string? ConditionType, string? IconBaseUri, int? RainPct, int? UvIndex)`; `GetHourlyForecastQuery(double Lat, double Lng, int Hours) : IQuery<IReadOnlyList<HourlyReadingDto>>`.

- [ ] **Step 1: Write the failing test** — `GetHourlyForecastHandlerTests.cs` (pure, no DbContext; local stub implements both interface methods):

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.GetHourlyForecast;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetHourlyForecastHandlerTests
{
    private sealed class StubWeather : IWeatherService
    {
        public WeatherPoint? ReceivedPoint;
        public int ReceivedHours;
        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
        {
            ReceivedPoint = point; ReceivedHours = hours;
            IReadOnlyList<HourlyReading> list = new List<HourlyReading>
            {
                new(new DateTime(2026,7,12,13,0,0), true,  34.0, 39.4, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 20, 8),
                new(new DateTime(2026,7,12,22,0,0), false, 28.0, 30.0, "CLEAR",  null, 5, 0),
            };
            return Task.FromResult(list);
        }
    }

    [Fact]
    public async Task Forwards_lat_lng_hours_and_maps_readings()
    {
        var stub = new StubWeather();
        var handler = new GetHourlyForecastHandler(stub, new GetHourlyForecastValidator());

        var dtos = await handler.Handle(new GetHourlyForecastQuery(13.7563, 100.5018, 48), CancellationToken.None);

        stub.ReceivedPoint!.Lat.Should().Be(13.7563);
        stub.ReceivedPoint!.Lng.Should().Be(100.5018);
        stub.ReceivedHours.Should().Be(48);
        dtos.Should().HaveCount(2);
        dtos[0].IsDaytime.Should().BeTrue();
        dtos[0].FeelsLikeC.Should().Be(39.4);
        dtos[1].IsDaytime.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_hours_over_the_240h_horizon()
    {
        var handler = new GetHourlyForecastHandler(new StubWeather(), new GetHourlyForecastValidator());
        await FluentActions.Awaiting(() => handler.Handle(new GetHourlyForecastQuery(13.75, 100.50, 999), CancellationToken.None).AsTask())
            .Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GetHourlyForecastHandlerTests"` · Expected: **compile error** (types don't exist yet).

- [ ] **Step 3a: Add `HourlyReadingDto`** to `TripDtos.cs` (below `WeatherReadingDto`):

```csharp
public sealed record HourlyReadingDto(
    DateTime DisplayLocal, bool IsDaytime,
    double? TempC, double? FeelsLikeC,
    string? ConditionType, string? IconBaseUri,
    int? RainPct, int? UvIndex);
```

- [ ] **Step 3b: Create `GetHourlyForecastQuery.cs`:**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed record GetHourlyForecastQuery(double Lat, double Lng, int Hours)
    : IQuery<IReadOnlyList<HourlyReadingDto>>;
```

- [ ] **Step 3c: Create `GetHourlyForecastValidator.cs`** (auto-registered):

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed class GetHourlyForecastValidator : AbstractValidator<GetHourlyForecastQuery>
{
    public GetHourlyForecastValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        RuleFor(x => x.Hours).InclusiveBetween(1, 240);
    }
}
```

- [ ] **Step 3d: Create `GetHourlyForecastHandler.cs`:**

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed class GetHourlyForecastHandler : IQueryHandler<GetHourlyForecastQuery, IReadOnlyList<HourlyReadingDto>>
{
    private readonly IWeatherService _weather;
    private readonly IValidator<GetHourlyForecastQuery> _validator;
    public GetHourlyForecastHandler(IWeatherService weather, IValidator<GetHourlyForecastQuery> validator)
    { _weather = weather; _validator = validator; }

    public async ValueTask<IReadOnlyList<HourlyReadingDto>> Handle(GetHourlyForecastQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);
        var hours = await _weather.GetHourlyAsync(new WeatherPoint("", q.Lat, q.Lng, null), q.Hours, ct);
        return hours
            .Select(h => new HourlyReadingDto(h.DisplayLocal, h.IsDaytime, h.TempC, h.FeelsLikeC, h.ConditionType, h.IconBaseUri, h.RainPct, h.UvIndex))
            .ToList();
    }
}
```

- [ ] **Step 3e: Add the endpoint** to `TripsController.cs` — add `using MenuNest.Application.UseCases.Trips.GetHourlyForecast;` at the top and, right after the `Weather` action:

```csharp
[HttpPost("api/trips/weather/hourly")]
public async Task<ActionResult<IReadOnlyList<HourlyReadingDto>>> HourlyWeather([FromBody] GetHourlyForecastQuery q, CancellationToken ct)
    => Ok(await _mediator.Send(q, ct));
```

- [ ] **Step 4: Run tests to verify they pass** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GetHourlyForecastHandlerTests"` · Expected: **PASS** (both).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/GetHourlyForecast/ \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetHourlyForecastHandlerTests.cs
git commit -m "feat(trips): add GetHourlyForecast query + POST /api/trips/weather/hourly (#46)"
```

---

## Task 3: `RetimeStopToHour` apply core — same-day shift + pin

The one write command both web and MCP converge on. Receives the **already-resolved** new day-start and anchor date; applies atomically. This task does the same-day case.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourCommand.cs`, `…Handler.cs`, `…Validator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (add `RetimeResultDto`)
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (endpoint + `RetimeBody`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/RetimeStopToHourHandlerTests.cs`

**Interfaces:**
- Produces: `record RetimeResultDto(bool MovedTrip, DateOnly TripStartBefore, DateOnly TripStartAfter, DateOnly AnchorDate, TimeOnly NewDayStartTime)`; `RetimeStopToHourCommand(Guid TripId, Guid DayId, Guid StopId, TimeOnly NewDayStartTime, DateOnly NewAnchorDate) : ICommand<RetimeResultDto>`.
- Consumes (later tasks): the same command is `mediator.Send(...)` from `RetimeStopToWeatherHandler` (Task 6).

- [ ] **Step 1: Write the failing test** — `RetimeStopToHourHandlerTests.cs` (InMemory via `HandlerTestFixture`):

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class RetimeStopToHourHandlerTests
{
    private static RetimeStopToHourHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new RetimeStopToHourValidator());

    [Fact]
    public async Task Same_day_sets_day_start_pins_the_day_and_leaves_the_date()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);              // apply must turn this OFF (ADR-115)
        fx.Db.ItineraryDays.Add(day);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        fx.Db.TripPlaces.Add(pA);
        var s0 = Stop.Create(day.Id, pA.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(s0);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new RetimeStopToHourCommand(trip.Id, day.Id, s0.Id, new TimeOnly(10, 45), new DateOnly(2026, 7, 12)),
            CancellationToken.None);

        var reloaded = await fx.Db.ItineraryDays.FirstAsync(d => d.Id == day.Id);
        reloaded.DayStartTime.Should().Be(new TimeOnly(10, 45));
        reloaded.UseCurrentTimeAsStart.Should().BeFalse();
        reloaded.Date.Should().Be(new DateOnly(2026, 7, 12));
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 12));
        result.MovedTrip.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_day_throws_not_found()
    {
        using var fx = new HandlerTestFixture();
        await FluentActions.Awaiting(() => Build(fx).Handle(
                new RetimeStopToHourCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new TimeOnly(9, 0), new DateOnly(2026, 7, 12)),
                CancellationToken.None).AsTask())
            .Should().ThrowAsync<MenuNest.Domain.Exceptions.DomainException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~RetimeStopToHourHandlerTests"` · Expected: **compile error**.

- [ ] **Step 3a: Add `RetimeResultDto`** to `TripDtos.cs`:

```csharp
public sealed record RetimeResultDto(
    bool MovedTrip, DateOnly TripStartBefore, DateOnly TripStartAfter,
    DateOnly AnchorDate, TimeOnly NewDayStartTime);
```

- [ ] **Step 3b: Create `RetimeStopToHourCommand.cs`:**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

/// <summary>Apply core (web + shared): re-time the anchor Day so the anchor Stop arrives at a
/// client-resolved hour. Shifts the Day start (always) and, for a cross-day target, the whole
/// Trip.StartDate + realign (ADR-113/114); pins the Day by turning off current-time-start (ADR-115).</summary>
public sealed record RetimeStopToHourCommand(
    Guid TripId, Guid DayId, Guid StopId, TimeOnly NewDayStartTime, DateOnly NewAnchorDate)
    : ICommand<RetimeResultDto>;
```

- [ ] **Step 3c: Create `RetimeStopToHourValidator.cs`:**

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

public sealed class RetimeStopToHourValidator : AbstractValidator<RetimeStopToHourCommand>
{
    public RetimeStopToHourValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.DayId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
    }
}
```

- [ ] **Step 3d: Create `RetimeStopToHourHandler.cs`** (same-day branch now; cross-day added in Task 4):

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

public sealed class RetimeStopToHourHandler : ICommandHandler<RetimeStopToHourCommand, RetimeResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<RetimeStopToHourCommand> _validator;

    public RetimeStopToHourHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<RetimeStopToHourCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<RetimeResultDto> Handle(RetimeStopToHourCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var trip = await _db.Trips.FirstOrDefaultAsync(
            t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId && d.TripId == trip.Id, ct)
            ?? throw new DomainException("Itinerary day not found.");

        // The anchor stop must belong to the day (defense; the client resolves the actual timing).
        var anchorExists = await _db.Stops.AnyAsync(s => s.Id == c.StopId && s.ItineraryDayId == day.Id, ct);
        if (!anchorExists) throw new DomainException("Stop not found.");

        var startBefore = trip.StartDate;
        var deltaDays = c.NewAnchorDate.DayNumber - day.Date.DayNumber;
        var moved = deltaDays != 0;

        // (cross-day realign added in Task 4)

        day.SetStartTime(c.NewDayStartTime);
        day.SetUseCurrentTimeAsStart(false);            // pin (ADR-115)

        await _db.SaveChangesAsync(ct);
        return new RetimeResultDto(moved, startBefore, trip.StartDate, day.Date, day.DayStartTime);
    }
}
```

- [ ] **Step 3e: Add the endpoint + body** to `TripsController.cs` — `using MenuNest.Application.UseCases.Trips.RetimeStopToHour;`, an action, and a `RetimeBody` record at the bottom with the other body records:

```csharp
[HttpPost("api/trips/{tripId:guid}/days/{dayId:guid}/retime")]
public async Task<ActionResult<RetimeResultDto>> Retime(Guid tripId, Guid dayId, [FromBody] RetimeBody b, CancellationToken ct)
    => Ok(await _mediator.Send(new RetimeStopToHourCommand(tripId, dayId, b.StopId, b.NewDayStartTime, b.NewAnchorDate), ct));

// with the other body records at the bottom of the file:
public sealed record RetimeBody(Guid StopId, TimeOnly NewDayStartTime, DateOnly NewAnchorDate);
```

- [ ] **Step 4: Run tests to verify they pass** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~RetimeStopToHourHandlerTests"` · Expected: **PASS** (both).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/ \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/RetimeStopToHourHandlerTests.cs
git commit -m "feat(trips): add RetimeStopToHour apply command (same-day shift + pin) + endpoint (#46)"
```

---

## Task 4: `RetimeStopToHour` — cross-day whole-trip realign

A later-date target shifts `Trip.StartDate` by ΔD and realigns every Day, reusing `UpdateTripHandler`'s collision-safe pattern (one `SaveChanges`; SQLite test proves no unique-index violation).

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/RetimeStopToHourRelationalTests.cs`

**Interfaces:** unchanged (same command; the ΔD≠0 branch is filled in).

- [ ] **Step 1: Write the failing test** — `RetimeStopToHourRelationalTests.cs` (SQLite, real unique `(TripId, Date)` index):

```csharp
using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class RetimeStopToHourRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<MenuNest.Application.Abstractions.IUserProvisioner> _users;

    public RetimeStopToHourRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:"); _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options); _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user); _db.SaveChanges();
        _users = new Mock<MenuNest.Application.Abstractions.IUserProvisioner>();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    [Fact]
    public async Task Cross_day_target_shifts_whole_trip_without_unique_collision()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 7, 12), 3, TravelMode.Drive);
        _db.Trips.Add(trip);
        var day0 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        var day1 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 13), new TimeOnly(9, 0));
        var day2 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 14), new TimeOnly(9, 0));
        _db.ItineraryDays.AddRange(day0, day1, day2);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        _db.TripPlaces.Add(pA);
        var anchor = Stop.Create(day0.Id, pA.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(anchor);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var handler = new RetimeStopToHourHandler(_db, _users.Object, new RetimeStopToHourValidator());
        // target on Jul 13 => deltaDays=+1 => StartDate Jul13, days realign to 13/14/15
        var result = await handler.Handle(
            new RetimeStopToHourCommand(trip.Id, day0.Id, anchor.Id, new TimeOnly(10, 45), new DateOnly(2026, 7, 13)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.TripStartBefore.Should().Be(new DateOnly(2026, 7, 12));
        result.TripStartAfter.Should().Be(new DateOnly(2026, 7, 13));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 13));
        var dates = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync();
        dates.Should().Equal(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 15));
        var anchorDay = await _db.ItineraryDays.OrderBy(d => d.Date).FirstAsync(); // was day0, now Jul 13
        anchorDay.DayStartTime.Should().Be(new TimeOnly(10, 45));
        anchorDay.UseCurrentTimeAsStart.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run test to verify it fails** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~RetimeStopToHourRelationalTests"` · Expected: **FAIL** — trip date unchanged / days not realigned (ΔD branch is a no-op today).

- [ ] **Step 3: Fill in the cross-day branch** — replace the `// (cross-day realign added in Task 4)` comment in `RetimeStopToHourHandler.cs` with:

```csharp
if (moved)
{
    var newStart = trip.StartDate.AddDays(deltaDays);
    trip.Reschedule(newStart, trip.DayCount);
    // Realign every kept Day in ONE SaveChanges — EF orders the per-row UPDATEs so the unique
    // (TripId, Date) index is never transiently violated (see UpdateTripHandler + reference_ef_relational_testing).
    var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);
    for (var i = 0; i < days.Count; i++)
        days[i].SetDate(newStart.AddDays(i));
    // `day` is one of those tracked entities; its Date is now the target date.
}
```

*(The subsequent `day.SetStartTime(...)` / `SetUseCurrentTimeAsStart(false)` / `SaveChangesAsync` from Task 3 still run and cover the anchor Day.)*

- [ ] **Step 4: Run tests to verify they pass** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~RetimeStopToHour"` · Expected: **PASS** (same-day + cross-day + not-found).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToHour/RetimeStopToHourHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/RetimeStopToHourRelationalTests.cs
git commit -m "feat(trips): RetimeStopToHour cross-day whole-trip realign (#46)"
```

---

## Task 5: Frontend RTK — hourly query, retime mutation, types, hooks

Wires the two backend endpoints into the single app-wide RTK slice. No unit test (RTK wiring) — gated by `tsc --noEmit` + `npm run build`.

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Produces: `interface HourlyReadingDto`, `interface RetimeResultDto`; hooks `useGetHourlyForecastQuery`, `useRetimeStopMutation`.

- [ ] **Step 1: Add the client types** near the other trip/weather DTOs (after `WeatherReadingDto`, ~line 557):

```ts
export interface HourlyReadingDto {
  displayLocal: string; isDaytime: boolean
  tempC: number | null; feelsLikeC: number | null
  conditionType: string | null; iconBaseUri: string | null
  rainPct: number | null; uvIndex: number | null
}
export interface RetimeResultDto {
  movedTrip: boolean
  tripStartBefore: string; tripStartAfter: string
  anchorDate: string; newDayStartTime: string
}
```

- [ ] **Step 2: Add the endpoint + mutation** inside the `endpoints: (build) => ({ ... })` block, next to `getStopWeather` (~line 1506):

```ts
getHourlyForecast: build.query<HourlyReadingDto[], {lat: number; lng: number; hours: number}>({
    query: (body) => ({url: '/api/trips/weather/hourly', method: 'POST', body}),
    keepUnusedDataFor: 600, // ephemeral like getStopWeather; no providesTags
}),
retimeStop: build.mutation<
    RetimeResultDto,
    {tripId: string; dayId: string; stopId: string; newDayStartTime: string; newAnchorDate: string}
>({
    query: ({tripId, dayId, ...b}) => ({url: `/api/trips/${tripId}/days/${dayId}/retime`, method: 'POST', body: b}),
    // A retime reschedules the day (and maybe the whole trip) — the itinerary must refetch to
    // show the new cascade, exactly like setDayStartTime.
    invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
}),
```

- [ ] **Step 3: Export the generated hooks** — add to the Trips destructuring block (after `useGetStopWeatherQuery,`, ~line 1654):

```ts
    useGetHourlyForecastQuery,
    useRetimeStopMutation,
```

- [ ] **Step 4: Verify it compiles** — Run: `cd frontend && npx tsc --noEmit` · Expected: **no errors**.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(web): add hourly-forecast query + retimeStop mutation to the API slice (#46)"
```

---

## Task 6: Frontend `lib/retiming.ts` — pure planner logic (vitest)

The unit-testable core: offset, suggested start, shift classification, coolest-hour, horizon. Mirrors `lib/weather.ts` / `useSchedule.ts`.

**Files:**
- Create: `frontend/src/pages/trips/lib/retiming.ts`
- Test: `frontend/src/pages/trips/lib/retiming.test.ts`

**Interfaces:**
- Consumes: `ItineraryDayDto`, `HourlyReadingDto` from `shared/api/api`; `weatherWindow` from `./weather`.
- Produces: `offsetMinutes(day, stopId)`, `suggestedStartMinutes(targetMinuteOfDay, offsetMin)`, `classifyShift(targetDate, anchorDate)`, `coolestHour(hours, daytime)`, `withinHorizon(targetMs, nowMs)`, `minutesToHHMMSS(min)`.

- [ ] **Step 1: Write the failing tests** — `retiming.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS} from './retiming'
import type {ItineraryDayDto, HourlyReadingDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, isVisited: false,
  legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated' as const},
})
const day = (stops: ReturnType<typeof stop>[]): ItineraryDayDto =>
  ({id: 'd1', date: '2026-07-12', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops})

const hr = (h: number, daytime: boolean, feels: number): HourlyReadingDto => ({
  displayLocal: `2026-07-12T${String(h).padStart(2, '0')}:00:00`, isDaytime: daytime,
  tempC: feels - 5, feelsLikeC: feels, conditionType: 'CLEAR', iconBaseUri: null, rainPct: 0, uvIndex: 0,
})

describe('offsetMinutes', () => {
  it('sums legs (rounded) + dwell up to the anchor, dayStart-independent', () => {
    // stop0 approach leg null; stop1 leg 900s(15m); anchor = stop1. offset = leg[0](0)+leg[1](15) + dwell[0](60) = 75
    const d = day([stop('0', 0, 60, null), stop('1', 1, 45, 900)])
    expect(offsetMinutes(d, '1')).toBe(75)
  })
  it('is 0 for the first stop with no approach leg', () => {
    expect(offsetMinutes(day([stop('0', 0, 60, null)]), '0')).toBe(0)
  })
  it('returns null for an unknown stop', () => {
    expect(offsetMinutes(day([stop('0', 0, 60, null)]), 'zzz')).toBeNull()
  })
})

describe('suggestedStartMinutes', () => {
  it('is target − offset', () => expect(suggestedStartMinutes(12 * 60, 75)).toBe(10 * 60 + 45))
  it('is negative when the target is unreachably early', () => expect(suggestedStartMinutes(30, 75)).toBe(-45))
})

describe('classifyShift', () => {
  it('same day when dates match', () =>
    expect(classifyShift('2026-07-12', '2026-07-12')).toEqual({sameDay: true, deltaDays: 0, movesTrip: false}))
  it('cross day moves the trip', () =>
    expect(classifyShift('2026-07-13', '2026-07-12')).toEqual({sameDay: false, deltaDays: 1, movesTrip: true}))
})

describe('coolestHour', () => {
  const hours = [hr(13, true, 39), hr(15, true, 41), hr(22, false, 30), hr(2, false, 28)]
  it('picks min feels-like daytime hour', () => expect(coolestHour(hours, true)?.displayLocal).toContain('T13:'))
  it('picks min feels-like nighttime hour', () => expect(coolestHour(hours, false)?.displayLocal).toContain('T02:'))
  it('is null when the half has no candidates', () => expect(coolestHour([hr(13, true, 39)], false)).toBeNull())
})

describe('minutesToHHMMSS', () => {
  it('formats to HH:mm:ss', () => expect(minutesToHHMMSS(10 * 60 + 45)).toBe('10:45:00'))
})
```

- [ ] **Step 2: Run tests to verify they fail** — Run: `cd frontend && npx vitest run src/pages/trips/lib/retiming.test.ts` · Expected: **FAIL** (module not found).

- [ ] **Step 3: Implement `retiming.ts`:**

```ts
import type {ItineraryDayDto, HourlyReadingDto} from '../../../shared/api/api'

// HH:MM(:SS) <-> minutes-past-midnight. (Matches useSchedule.ts; that module keeps these private.)
const toMin = (hhmm: string) => {
  const [h, m] = hhmm.slice(0, 5).split(':').map(Number)
  return h * 60 + m
}
/** Minutes-past-midnight -> "HH:mm:ss" (the wire shape TimeOnly binds). Wraps at 24h. */
export function minutesToHHMMSS(min: number): string {
  const wrapped = ((min % 1440) + 1440) % 1440
  return `${String(Math.floor(wrapped / 60)).padStart(2, '0')}:${String(wrapped % 60).padStart(2, '0')}:00`
}

/** Anchor arrival minus day start, in minutes: Σ legs(rounded) + Σ dwell up to (and incl. legs of) the anchor.
 *  Independent of dayStart — mirrors useSchedule.computeSchedule but unwrapped (overnight-safe). null if not found. */
export function offsetMinutes(day: ItineraryDayDto, stopId: string): number | null {
  const ordered = [...day.stops].sort((a, b) => a.sequence - b.sequence)
  let acc = 0
  for (const s of ordered) {
    acc += s.legToReach ? Math.round(s.legToReach.seconds / 60) : 0
    if (s.id === stopId) return acc
    acc += s.dwellMinutes
  }
  return null
}

/** New day-start (minutes) so the anchor arrives at targetMinuteOfDay. Negative => unreachably early. */
export function suggestedStartMinutes(targetMinuteOfDay: number, offsetMin: number): number {
  return targetMinuteOfDay - offsetMin
}

export interface ShiftKind {sameDay: boolean; deltaDays: number; movesTrip: boolean}
/** dates are 'yyyy-MM-dd'. Any cross-day target shifts the whole trip (ADR-114). */
export function classifyShift(targetDate: string, anchorDayDate: string): ShiftKind {
  const d = Math.round((Date.parse(targetDate.slice(0, 10)) - Date.parse(anchorDayDate.slice(0, 10))) / 86_400_000)
  return {sameDay: d === 0, deltaDays: d, movesTrip: d !== 0}
}

/** Min feels-like hour of the requested half (isDaytime), earliest on ties; null if the half is empty. */
export function coolestHour(hours: HourlyReadingDto[], daytime: boolean): HourlyReadingDto | null {
  const half = hours
    .filter((h) => h.isDaytime === daytime && h.feelsLikeC != null)
    .sort((a, b) => Date.parse(a.displayLocal) - Date.parse(b.displayLocal))
  let best: HourlyReadingDto | null = null
  for (const h of half) if (best == null || (h.feelsLikeC as number) < (best.feelsLikeC as number)) best = h
  return best
}

/** Reuses the 240h forecast horizon check. */
export {weatherWindow as _weatherWindow} from './weather'
export function withinHorizon(targetMs: number, nowMs: number): boolean {
  // duplicate of weather.weatherWindow's 'ok' band, kept explicit for the planner's guard
  const HORIZON_MS = 240 * 60 * 60 * 1000
  return targetMs >= nowMs && targetMs <= nowMs + HORIZON_MS
}
```

- [ ] **Step 4: Run tests to verify they pass** — Run: `cd frontend && npx vitest run src/pages/trips/lib/retiming.test.ts` · Expected: **PASS** (all).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/retiming.ts frontend/src/pages/trips/lib/retiming.test.ts
git commit -m "feat(web): add pure retiming lib (offset, suggested start, shift class, coolest hour) (#46)"
```

---

## Task 7: `HourlyPlanner` component + planner CSS + clock glyph

The horizontal hourly strip, two quick actions, and apply-preview card. No unit test (no RTL) — verified interactively (Finishing). Uses `useGetHourlyForecastQuery`, `useRetimeStopMutation`, and `lib/retiming`.

**Files:**
- Create: `frontend/src/pages/trips/components/HourlyPlanner.tsx`
- Modify: `frontend/src/pages/trips/components/WeatherIcons.tsx` (add `ClockIcon`)
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (`.sd-hourly*`)

**Interfaces:**
- Consumes: `useGetHourlyForecastQuery`, `useRetimeStopMutation`, `HourlyReadingDto`, `iconUrl` (from `lib/weather`), `offsetMinutes`/`suggestedStartMinutes`/`classifyShift`/`coolestHour`/`minutesToHHMMSS`/`withinHorizon` (from `lib/retiming`).
- Produces: `<HourlyPlanner day={ItineraryDayDto} stopId={string} place={TripPlaceDto} tripId={string} tripStartDate={string} onClose={() => void} />`.

- [ ] **Step 1: Add the `ClockIcon` glyph** to `WeatherIcons.tsx` (reuse the shared `base`; the strip's header/entry affordance):

```tsx
/** Clock — leads the hourly-planner entry/header. */
export function ClockIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  )
}
```

- [ ] **Step 2: Create `HourlyPlanner.tsx`** — renders the strip (feels-like headline per cell via `Math.round(h.feelsLikeC)°`, condition icon via `<img src={iconUrl(h.iconBaseUri, false)}/>`, current-plan cell ringed, day/night tint from `h.isDaytime`, a "พรุ่งนี้" divider when the date rolls), two quick-action buttons (coolest daytime/nighttime via `coolestHour`), and an apply-preview card (resulting start via `minutesToHHMMSS(suggestedStartMinutes(...))`, the cross-day/whole-trip warning from `classifyShift`, and the "จะปิด 'ใช้เวลาปัจจุบันเสมอ'" note). Apply calls `retimeStop({tripId, dayId: day.id, stopId, newDayStartTime, newAnchorDate})` then `onClose()`:

```tsx
import {useMemo, useState} from 'react'
import type {ItineraryDayDto, TripPlaceDto, HourlyReadingDto} from '../../../shared/api/api'
import {useGetHourlyForecastQuery, useRetimeStopMutation} from '../../../shared/api/api'
import {iconUrl} from '../lib/weather'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS} from '../lib/retiming'
import {ClockIcon} from './WeatherIcons'

const WINDOW_HOURS = 48

export function HourlyPlanner({
  day, stopId, place, tripId, onClose,
}: {
  day: ItineraryDayDto; stopId: string; place: TripPlaceDto; tripId: string; onClose: () => void
}) {
  const {data: hours = [], isLoading} = useGetHourlyForecastQuery({lat: place.lat, lng: place.lng, hours: WINDOW_HOURS})
  const [retime, {isLoading: applying}] = useRetimeStopMutation()
  const [picked, setPicked] = useState<HourlyReadingDto | null>(null)

  const offset = useMemo(() => offsetMinutes(day, stopId), [day, stopId])

  if (isLoading) return <div className="sd-hourly sd-hourly--loading">กำลังโหลดพยากรณ์รายชั่วโมง…</div>
  if (hours.length === 0) return <div className="sd-hourly sd-hourly--empty">ไม่มีข้อมูลอากาศรายชั่วโมง</div>

  const dayA = coolestHour(hours, true)
  const dayN = coolestHour(hours, false)

  const preview = (() => {
    if (!picked || offset == null) return null
    const t = new Date(picked.displayLocal)
    const targetMin = t.getHours() * 60 + t.getMinutes()
    const startMin = suggestedStartMinutes(targetMin, offset)
    if (startMin < 0) return {unreachable: true as const}
    const targetDate = picked.displayLocal.slice(0, 10)
    const shift = classifyShift(targetDate, day.date)
    return {
      unreachable: false as const,
      newDayStartTime: minutesToHHMMSS(startMin),
      newAnchorDate: targetDate,
      shift,
    }
  })()

  const apply = async () => {
    if (!preview || preview.unreachable) return
    await retime({tripId, dayId: day.id, stopId, newDayStartTime: preview.newDayStartTime, newAnchorDate: preview.newAnchorDate})
    onClose()
  }

  const todayDate = day.date.slice(0, 10)
  return (
    <div className="sd-hourly">
      <div className="sd-hourly-quick">
        {dayA && <button type="button" className="btn-text" onClick={() => setPicked(dayA)}>กลางวันเย็นสุด รู้สึก {Math.round(dayA.feelsLikeC as number)}°</button>}
        {dayN && <button type="button" className="btn-text" onClick={() => setPicked(dayN)}>กลางคืนเย็นสุด รู้สึก {Math.round(dayN.feelsLikeC as number)}°</button>}
      </div>
      <div className="sd-hourly-strip">
        {hours.map((h) => {
          const isNextDay = h.displayLocal.slice(0, 10) !== todayDate
          const isPicked = picked?.displayLocal === h.displayLocal
          return (
            <div key={h.displayLocal} className={`sd-hr${h.isDaytime ? ' day' : ' night'}${isPicked ? ' picked' : ''}`}>
              {isNextDay && <span className="sd-hr-div">พรุ่งนี้</span>}
              <button type="button" onClick={() => setPicked(h)}>
                <span className="sd-hr-time">{h.displayLocal.slice(11, 16)}</span>
                {h.iconBaseUri && <img src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={20} height={20} />}
                {h.feelsLikeC != null && <span className="sd-hr-feels">รู้สึก {Math.round(h.feelsLikeC)}°</span>}
              </button>
            </div>
          )
        })}
      </div>
      {preview && (
        <div className="sd-hourly-preview">
          {preview.unreachable ? (
            <p>ช่วงเวลานี้ไปถึงไม่ทัน — จุดนี้อยู่ลึกในวันเกินไป</p>
          ) : (
            <>
              <p>เริ่มวันใหม่ {preview.newDayStartTime.slice(0, 5)} → ถึงตอน {picked!.displayLocal.slice(11, 16)}</p>
              {preview.shift.movesTrip && <p className="warn">ทั้งทริปจะเลื่อนไป {preview.shift.deltaDays} วัน วันอื่นขยับตาม</p>}
              <p className="note">จะปิด “ใช้เวลาปัจจุบันเสมอ” ของวันนี้</p>
              <button type="button" className="btn-primary" disabled={applying} onClick={apply}>ปรับเลย</button>
            </>
          )}
        </div>
      )}
      <button type="button" className="btn-text sd-hourly-close" onClick={onClose}>ปิด</button>
    </div>
  )
}
```

- [ ] **Step 3: Add planner CSS** to `TripDetailPage.css` under the `.stop-detail-sheet` block (sibling of `.sd-weather`): `.sd-hourly` (column layout), `.sd-hourly-strip` (horizontal `overflow-x:auto`), `.sd-hr.day`/`.sd-hr.night` tints, `.sd-hr.picked` ring, `.sd-hr-div` divider, `.sd-hourly-preview .warn`/`.note`. Use the existing `--sd-*` tokens; add a `:root` fallback for any token consumed here (the sheet is a body-portaled Syncfusion Dialog — page-scoped `--sd-*` custom properties do not resolve inside the portal; see the CSS-portal gotcha).

- [ ] **Step 4: Verify it compiles + builds** — Run: `cd frontend && npx tsc --noEmit && npm run build` · Expected: **success**.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/HourlyPlanner.tsx \
        frontend/src/pages/trips/components/WeatherIcons.tsx \
        frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(web): add HourlyPlanner (hourly strip + quick actions + apply preview) (#46)"
```

---

## Task 8: Entry button in `StopDetailSheet` + wire props in `ItineraryTab`

Mount the planner under the two weather chips (ADR-121), revealed on tap. No unit test — verified interactively.

**Files:**
- Modify: `frontend/src/pages/trips/components/StopDetailSheet.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`

**Interfaces:**
- Consumes: `HourlyPlanner` (Task 7).
- StopDetailSheet gains an optional prop `retiming?: {tripId: string; dayId: string; dayDate: string}` (place, arrival, day.stops arrive via existing props / a small addition).

- [ ] **Step 1: Add the entry button + planner to `StopDetailSheet.tsx`.** Add a `useState` toggle and a new optional prop carrying the itinerary context the planner needs (`tripId`, `day: ItineraryDayDto`). Insert immediately after the `.sd-weather` `</div>` (line 99) and before the season block (line 101):

```tsx
// new prop (in the destructure + type): planner?: {tripId: string; day: ItineraryDayDto}
const [showHourly, setShowHourly] = useState(false)
// ...
</div> {/* .sd-weather */}
{planner && (
  <>
    <button type="button" className="sd-act btn-text" onClick={() => setShowHourly((v) => !v)}>
      <ClockIcon /> ดูอุณหภูมิรายชั่วโมง — เลือกเวลาไปถึงตอนอากาศที่ต้องการ
    </button>
    {showHourly && (
      <HourlyPlanner
        day={planner.day}
        stopId={/* the anchor stop id */ stopId}
        place={place}
        tripId={planner.tripId}
        onClose={() => setShowHourly(false)}
      />
    )}
  </>
)}
```

  *(StopDetailSheet currently has no `stopId` prop — add `stopId: string` to its props, or reuse the id already available where the sheet is rendered. `ClockIcon`/`HourlyPlanner` need imports.)*

- [ ] **Step 2: Wire the new props in `ItineraryTab.tsx`** at the `<StopDetailSheet …>` render (~lines 487–500). `resolvedDay` (the `ItineraryDayDto`), `trip.id`, and `detailStop.stop.id` are all in scope:

```tsx
stopId={detailStop.stop.id}
planner={{tripId: trip.id, day: resolvedDay}}
```

  *(Confirm the in-scope names: the day variable used for `resolvedDay.date` at line 204, and the trip id. Use the exact identifiers present in `ItineraryTab`.)*

- [ ] **Step 3: Verify it compiles + builds** — Run: `cd frontend && npx tsc --noEmit && npm run build` · Expected: **success**.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/trips/components/StopDetailSheet.tsx frontend/src/pages/trips/components/ItineraryTab.tsx
git commit -m "feat(web): add hourly-planner entry to the stop detail sheet (#46)"
```

- [ ] **Step 5: Interactive smoke test (REQUIRED before continuing/pushing).** See *Finishing* — open a Stop sheet in a seeded/authed run, open the planner, confirm the strip / current-plan marker / day-night tint / "พรุ่งนี้" divider render; do one same-day apply and confirm the schedule shifts; do one cross-day apply on a multi-day trip and confirm the whole-trip warning + all days move. **This gate cannot be met by `tsc`/`build`/vitest** (no visual harness). Prod deploys on push.

---

## Task 9 (Phase 1b — separable): MCP tools + stop-based hourly + weather-target resolver

ADR-120 exposes both over MCP. This is cleanly separable from the web feature — if scope needs trimming, defer Task 9 to Phase 2 (the web feature above is complete without it). Server-side offset here uses **inter-stop legs only** (no approach leg — an AI caller has no live location); it delegates the write to `RetimeStopToHourCommand`.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/GetStopHourlyForecast/GetStopHourlyForecastQuery.cs`, `…Handler.cs`, `…Validator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToWeather/RetimeTarget.cs`, `WeatherHourSelection.cs`, `RetimeStopToWeatherCommand.cs`, `…Handler.cs`, `…Validator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (add `RetimeTargetDto`)
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetStopHourlyForecastHandlerTests.cs`, `WeatherHourSelectionTests.cs`, `RetimeStopToWeatherHandlerTests.cs`; extend `…/Tools/TripToolsTests.cs`

**Interfaces:**
- Produces: `GetStopHourlyForecastQuery(Guid TripId, Guid StopId, int Hours) : IQuery<IReadOnlyList<HourlyReadingDto>>`; `record RetimeTarget(string Kind, DateTime? LocalDateTime, int? WindowHours)` (`Kind` ∈ `hour|coolestDaytime|coolestNighttime`); `RetimeStopToWeatherCommand(Guid TripId, Guid DayId, Guid StopId, RetimeTarget Target) : ICommand<RetimeResultDto>`; static `WeatherHourSelection.CoolestHour(IReadOnlyList<HourlyReading> hours, bool daytime) : HourlyReading?`.
- Consumes: `IWeatherService.GetHourlyAsync`, `IRouteService.GetLegTimesAsync`, `RetimeStopToHourCommand` (Task 3/4).

- [ ] **Step 1a: `WeatherHourSelectionTests.cs`** (pure helper — the server twin of `coolestHour`):

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherHourSelectionTests
{
    private static HourlyReading H(int hour, bool day, double feels)
        => new(new DateTime(2026, 7, 12, hour, 0, 0), day, feels - 5, feels, "CLEAR", null, 0, 0);

    [Fact]
    public void Picks_min_feelslike_of_the_requested_half_earliest_on_ties()
    {
        var hours = new List<HourlyReading> { H(13, true, 39), H(15, true, 39), H(22, false, 30), H(2, false, 28) };
        WeatherHourSelection.CoolestHour(hours, true)!.DisplayLocal.Hour.Should().Be(13);
        WeatherHourSelection.CoolestHour(hours, false)!.DisplayLocal.Hour.Should().Be(2);
    }

    [Fact]
    public void Returns_null_when_the_half_is_empty()
        => WeatherHourSelection.CoolestHour(new List<HourlyReading> { H(13, true, 39) }, false).Should().BeNull();
}
```

- [ ] **Step 1b: `RetimeStopToWeatherHandlerTests.cs`** (InMemory; StubWeather returns an hourly list; StubRoute returns a leg; assert it resolves and delegates so the day is retimed). *Model the arrange on `RetimeStopToHourHandlerTests` + the `IRouteService` mock (`GetItineraryHandlerTests` pattern) and the `StubWeather` returning `HourlyReading`s.* Assert: a `coolestDaytime` target picks the min-feels-like daytime hour, computes `newStart = coolestHour − offset`, and the anchor Day's `DayStartTime`/`UseCurrentTimeAsStart` end as expected.

- [ ] **Step 1c: `GetStopHourlyForecastHandlerTests.cs`** — owner-scoped: seed a Trip→Day→Stop→TripPlace; assert the handler resolves the stop's `TripPlace` coords and forwards them + `hours` to `IWeatherService.GetHourlyAsync` (StubWeather captures the point). Assert unknown stop → `DomainException`.

- [ ] **Step 2: Run tests to verify they fail** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~RetimeStopToWeather|FullyQualifiedName~WeatherHourSelection|FullyQualifiedName~GetStopHourlyForecast"` · Expected: **compile errors**.

- [ ] **Step 3a: `RetimeTarget.cs` + `RetimeTargetDto`** — the record + a wire DTO in `TripDtos.cs` (`RetimeTargetDto(string Kind, DateTime? LocalDateTime, int? WindowHours)`).

- [ ] **Step 3b: `WeatherHourSelection.cs`** — `public static HourlyReading? CoolestHour(IReadOnlyList<HourlyReading> hours, bool daytime)` (min `FeelsLikeC` where `IsDaytime == daytime && FeelsLikeC is not null`, earliest `DisplayLocal` on ties).

- [ ] **Step 3c: `GetStopHourlyForecastQuery/Handler/Validator`** — handler injects `IApplicationDbContext`, `IUserProvisioner`, `IWeatherService`, `IValidator<>`; owner-scoped resolve of the stop → its `TripPlace` (Lat/Lng); `GetHourlyAsync(new WeatherPoint("", lat, lng, null), hours, ct)`; map to `HourlyReadingDto`. Validator: `Hours` in `[1,240]`, ids `NotEmpty`.

- [ ] **Step 3d: `RetimeStopToWeatherCommand/Handler/Validator`** — handler injects `IApplicationDbContext`, `IUserProvisioner`, `IRouteService`, `IWeatherService`, `IMediator`, `IValidator<>`:
  1. Owner-scoped resolve stop → day → trip; load the day's stops ordered by `Sequence` + their `TripPlace` coords.
  2. Compute `offsetMin` = Σ inter-stop leg minutes (`IRouteService.GetLegTimesAsync` per consecutive pair, `Math.Round(sec/60)`) + Σ dwell up to the anchor index. **No approach leg.**
  3. Resolve target → `(DateOnly date, TimeOnly time)`: `hour` → from `Target.LocalDateTime`; `coolestDaytime`/`coolestNighttime` → `GetHourlyAsync(anchor coords, WindowHours ?? 48)` then `WeatherHourSelection.CoolestHour(..., daytime)` (throw `DomainException` if none).
  4. `startMin = time − offsetMin`; if `< 0` → `DomainException` ("ไปถึงไม่ทัน"). `newStart = TimeOnly` from `startMin`.
  5. `mediator.Send(new RetimeStopToHourCommand(TripId, DayId, StopId, newStart, date), ct)` and return its `RetimeResultDto`.
  Validator: ids `NotEmpty`; `Target.Kind` ∈ the three; `hour` requires `LocalDateTime`; `coolest*` `WindowHours` in `[1,240]` when present.

- [ ] **Step 3e: MCP tools** in `TripTools.cs` (add `using` for the two use-case namespaces; snake_case; thin `mediator.Send`):

```csharp
[McpServerTool, Description("Hourly weather forecast for a stop's location (up to 240h). Each hour: local time, feels-like, temp, condition, isDaytime.")]
public async Task<IReadOnlyList<HourlyReadingDto>> get_stop_hourly_forecast(
    [Description("Trip ID")] Guid tripId,
    [Description("Stop ID")] Guid stopId,
    [Description("Forecast hours to return (1-240)")] int hours,
    CancellationToken ct)
    => await mediator.Send(new GetStopHourlyForecastQuery(tripId, stopId, hours), ct);

[McpServerTool, Description("Re-time the plan so a stop arrives at a target hour, or the coolest daytime/nighttime hour. Shifts the day start (and whole trip StartDate for a cross-day target); turns off the day's current-time-start. Returns whether the whole trip moved.")]
public async Task<RetimeResultDto> retime_stop_to_weather(
    [Description("Trip ID")] Guid tripId,
    [Description("Itinerary day ID of the anchor stop")] Guid dayId,
    [Description("Anchor stop ID")] Guid stopId,
    [Description("Target: { kind: 'hour'|'coolestDaytime'|'coolestNighttime', localDateTime?, windowHours? }")] RetimeTarget target,
    CancellationToken ct)
    => await mediator.Send(new RetimeStopToWeatherCommand(tripId, dayId, stopId, target), ct);
```

- [ ] **Step 3f: MCP tool tests** in `TripToolsTests.cs` — for each tool, `Setup(m => m.Send(It.Is<TQ/TCmd>(x => …ids match…), It.IsAny<CancellationToken>())).Returns<…>((_, _) => new ValueTask<T>(expected))`, invoke the tool, `Verify(..., Times.Once)`.

- [ ] **Step 4: Run tests to verify they pass** — Run: `dotnet test backend/tests/MenuNest.Application.UnitTests backend/tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~RetimeStopToWeather|FullyQualifiedName~WeatherHourSelection|FullyQualifiedName~GetStopHourlyForecast|FullyQualifiedName~TripTools"` · Expected: **PASS**.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/GetStopHourlyForecast/ \
        backend/src/MenuNest.Application/UseCases/Trips/RetimeStopToWeather/ \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/GetStopHourlyForecastHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourSelectionTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/RetimeStopToWeatherHandlerTests.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs
git commit -m "feat(mcp): expose get_stop_hourly_forecast + retime_stop_to_weather (closes #46)"
```

---

## Finishing

- [ ] **Full suite green:** `cd backend && dotnet test` and `cd frontend && npx tsc --noEmit && npm run build && npx vitest run` — all pass (the pre-commit hook enforces this per commit, but confirm once more).
- [ ] **Interactive verification (required — no visual harness).** In a seeded, authenticated run of the app: open a Stop's detail sheet → tap "ดูอุณหภูมิรายชั่วโมง" → confirm the strip renders (feels-like headline, condition icon, current-plan cell ringed, day/night tint, "พรุ่งนี้" divider across midnight); the two quick actions select the right hours; a **same-day** apply shifts the day and closes; a **cross-day** apply on a multi-day trip shows the whole-trip warning and moves all days; a Stop whose window is beyond the 240h horizon shows the no-data state and no planner. Watch for the portal CSS trap (Task 7 step 3) — a `--sd-*` token that doesn't resolve inside the Syncfusion Dialog paints nothing.
- [ ] **Do NOT push until interactive verification passes** — prod deploys on push to `main`, and a broken render passes every automated gate (learned on #36).
- [ ] **Reconcile before push** (parallel sessions): `git fetch main && git rebase main/main` (re-check ADR numbering), then `git push main HEAD:worktree-issue-46-hourly-temperature` (or merge to `main` per your workflow). Use `superpowers:finishing-a-development-branch` to decide merge/PR.

---

## Self-Review

**Spec coverage** (spec §1–8): §1 scope — hourly view (Tasks 2/5/7) + retiming (Tasks 3/4/6/7/8) ✅. §2 retiming model — day-start lever (Task 3), cross-day StartDate realign (Task 4), pin (Task 3) ✅. §3 target/selection — feels-like metric + tap-hour + coolest day/night via isDaytime (Tasks 6/7 client; Task 9 server) ✅; rolling window/cross-midnight (Task 7 strip + "พรุ่งนี้") ✅. §4.1 hourly read — `GetHourlyAsync` reusing forecast/hours (Task 1), query+endpoint (Task 2) ✅. §4.2 apply — `RetimeStopToHour` composing existing setters (Tasks 3/4) ✅ **with the documented deviation** (offset resolved client-side for web / server-side for MCP — Design note + Pre-flight P3). §4.3 MCP — both tools (Task 9) ✅. §5 frontend — lib (Task 6), planner + entry (Tasks 7/8), RTK (Task 5) ✅. §6 edge cases — beyond-horizon (Task 7 empty state), unreachable-early (Task 6 negative + Task 7 preview + Task 9 guard), overnight (offsetMinutes unwrapped) ✅. §7 testing — GoogleWeatherService hourly parse, handler tests, SQLite cross-day realign, MCP tool tests, retiming lib tests, interactive gate ✅. §8 rollout — no migration, #46 refs, ADR renumber, smoke-before-push (Pre-flight + Finishing) ✅.

**Placeholder scan:** Tasks 1–6 carry complete, copy-paste code. Tasks 7–9 mix full code (Task 7 component, Task 9 MCP tools + full test blocks) with **spec-style prose for the parts that are house-pattern clones** (Task 8 wiring uses in-scope identifiers that must be confirmed against `ItineraryTab`; Task 9 handlers describe the exact clone of documented patterns). These are intentional: the wiring identifiers and the `RetimeStopToWeather` handler body depend on names an executor must read in-file, and every referenced pattern is cited to a verified location. Flagged inline for the executor to confirm, not invent.

**Type consistency:** `HourlyReading`/`HourlyReadingDto` field order is identical across Tasks 1/2/5/9. `RetimeStopToHourCommand(TripId, DayId, StopId, NewDayStartTime, NewAnchorDate)` and `RetimeResultDto(MovedTrip, TripStartBefore, TripStartAfter, AnchorDate, NewDayStartTime)` are used identically in Tasks 3/4/5/9. Real method names confirmed: `ItineraryDay.SetStartTime` (NOT `SetDayStartTime`), `SetUseCurrentTimeAsStart`, `SetDate`; `Trip.Reschedule`. Mediator `ValueTask`/`Unit` conventions honoured throughout.
