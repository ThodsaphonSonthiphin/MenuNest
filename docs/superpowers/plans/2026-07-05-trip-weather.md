# Trip Weather (per-Stop, two readings) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show two weather readings on every itinerary Stop — **Now** (current conditions at the stop) and **On-arrival** (forecast at the scheduled arrival) — via a backend proxy to the Google Weather API, with an honest "no weather data" chip beyond the 10-day horizon.

**Architecture:** A new `IWeatherService` seam (Application/Abstractions) with a Google implementation + no-op fallback (Infrastructure/Maps), fronted by a batch CQRS query use-case and a `POST /api/trips/weather` endpoint — mirroring the existing `IRouteService`/`GoogleRouteService` pattern. The SPA gathers stop coordinates + `useSchedule` arrival times, calls the batch endpoint twice (one per reading kind) through the single shared RTK Query `api`, and renders two `WeatherChip`s per stop. Nothing is persisted; `IMemoryCache` is the only store.

**Tech Stack:** .NET 10 (Clean Architecture, **Mediator** source-gen library — *not* MediatR, FluentValidation, xUnit + FluentAssertions), React 19 + TypeScript + Vite, Redux Toolkit Query, Vitest (node env), Google Weather API (`weather.googleapis.com/v1`).

**Spec:** [docs/superpowers/specs/2026-07-05-trip-weather-design.md](../specs/2026-07-05-trip-weather-design.md) · **ADRs:** [027](../../adr/027-weather-display-only-no-push.md)–[032](../../adr/032-weather-backend-batch-endpoint-no-persistence.md) · **Mock:** [docs/mocks/trip-weather-mock.html](../../mocks/trip-weather-mock.html)

## Global Constraints

Every task's requirements implicitly include these:

- **Languages:** code/comments/commits/docs → **English**; user-visible UI strings → **Thai** (frontend-guidelines §5).
- **Icons:** UI icons are inline-SVG components, **never emoji** (frontend-guidelines §2). The **only** exception is the weather *condition* icon, sourced from Google's `iconBaseUri` as `.svg` / `_dark.svg` (ADR-031). The rain-drop and slashed-cloud glyphs are hand-authored inline SVG.
- **Frontend API:** exactly **one authenticated** `createApi` instance `api` at `frontend/src/shared/api/api.ts`. Add endpoints inside its single `endpoints: (build) => ({...})` block; **never** `injectEndpoints` from a feature folder. Export the generated hook in the `= api` destructure block.
- **Backend CQRS:** use the **Mediator** library (`using Mediator;`): `IQuery<T>`/`IQueryHandler<Q,T>`, handler method `public async ValueTask<T> Handle(Q q, CancellationToken ct)`. Controllers inject `IMediator` and `await _mediator.Send(x, ct)`. Cancellation param is always named `ct`.
- **No persistence:** no entity, no `DbSet`, no EF migration — so the manual prod-DB migration step in the project instructions does **not** apply here.
- **Weather API rules:** 10-day / 240-hour horizon (`hours=241`/`days=11` → HTTP 400); GET with `location.latitude`/`location.longitude`, `unitsSystem=METRIC`, `languageCode=th`; key via `X-Goog-Api-Key` **header**; **do NOT send `X-Goog-FieldMask`** (the Weather API returns the full document without one — a wrong mask 400s; verified live). On any failure a point degrades to `HasData=false`, never throws (ADR-030).
- **Tests:** backend = xUnit + FluentAssertions, Maps services faked with hand-rolled `StubHandler : HttpMessageHandler` + `StubFactory : IHttpClientFactory` (no Moq). Frontend = **Vitest, `environment: 'node'`** — pure-logic tests only, no DOM/component rendering; component & CSS tasks are gated by `tsc -b` + visual check against the mock.
- **Commits:** conventional `type(scope): summary`, each referencing the ticket — `(#10)` for partial work, `(closes #10)` on the final task (CLAUDE.md commit rule).

## File Structure

**Backend (create):**
- `backend/src/MenuNest.Domain/Enums/WeatherReadingKind.cs` — `Now` | `OnArrival` enum.
- `backend/src/MenuNest.Application/Abstractions/IWeatherService.cs` — interface + `WeatherPoint` / `WeatherReading` records.
- `backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs` — no-op fallback (all No-data).
- `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs` — Google impl.
- `backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/GetStopWeatherQuery.cs` · `GetStopWeatherHandler.cs` · `GetStopWeatherValidator.cs`.
- Tests: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs` · `MissingConfigWeatherServiceTests.cs`; `backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs` · `GetStopWeatherValidatorTests.cs` · `WeatherServiceRegistrationTests.cs`.

**Backend (modify):**
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — add `WeatherPointDto`, `WeatherReadingDto`.
- `backend/src/MenuNest.Infrastructure/DependencyInjection.cs` — add `IWeatherService` branch.
- `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — add `POST api/trips/weather`.

**Frontend (create):**
- `frontend/src/pages/trips/lib/weather.ts` (+ `weather.test.ts`) — pure helpers.
- `frontend/src/pages/trips/components/WeatherIcons.tsx` — `RainDropIcon`, `NoWeatherIcon`.
- `frontend/src/pages/trips/components/WeatherChip.tsx` — one chip.
- `frontend/src/pages/trips/hooks/useStopWeather.ts` — batches + queries.

**Frontend (modify):**
- `frontend/src/shared/api/api.ts` — `WeatherPointDto`/`WeatherReadingDto` types, `getStopWeather` endpoint, hook export.
- `frontend/src/pages/trips/trips-tokens.css` — weather chip tokens + classes.
- `frontend/src/pages/trips/components/ItineraryStopCard.tsx` — render the two chips.
- `frontend/src/pages/trips/components/ItineraryTab.tsx` — call `useStopWeather`, pass readings down.

---

## Task B1: Weather service seam + no-op fallback

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/WeatherReadingKind.cs`
- Create: `backend/src/MenuNest.Application/Abstractions/IWeatherService.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (append)
- Create: `backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/MissingConfigWeatherServiceTests.cs`

**Interfaces:**
- Produces: `enum WeatherReadingKind { Now, OnArrival }`; `record WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal)`; `record WeatherReading(string StopId, bool HasData, string? ConditionType, string? IconBaseUri, double? TempC, int? RainPct, string? Description)`; `interface IWeatherService { Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct); }`; DTOs `WeatherPointDto(string StopId, double Lat, double Lng, DateTime? ArrivalIso)`, `WeatherReadingDto(string StopId, bool HasData, string? ConditionType, string? IconBaseUri, double? TempC, int? RainPct, string? Description)`.

- [ ] **Step 1: Create the enum**

`backend/src/MenuNest.Domain/Enums/WeatherReadingKind.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>Which weather reading a request wants for a Stop: Now = current conditions at the
/// coordinates (real present moment); OnArrival = forecast at the Stop's scheduled arrival time.</summary>
public enum WeatherReadingKind
{
    Now,
    OnArrival,
}
```

- [ ] **Step 2: Create the service abstraction + records**

`backend/src/MenuNest.Application/Abstractions/IWeatherService.cs` (mirrors `IRouteService.cs`: file-scoped namespace, single-line sealed positional records above the interface, XML-doc'd method, trailing `CancellationToken ct`):

```csharp
using MenuNest.Domain.Enums;
namespace MenuNest.Application.Abstractions;

public sealed record WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal);
public sealed record WeatherReading(string StopId, bool HasData, string? ConditionType, string? IconBaseUri, double? TempC, int? RainPct, string? Description);

public interface IWeatherService
{
    /// <summary>Resolve a weather reading of the given kind for each point. Any failure degrades a
    /// single point to HasData=false rather than throwing (ADR-030).</summary>
    Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct);
}
```

- [ ] **Step 3: Add the wire DTOs**

Append to `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (same sealed-record style as the existing DTOs; `MenuNest.Domain.Enums` is already imported at the top):

```csharp
public sealed record WeatherPointDto(string StopId, double Lat, double Lng, DateTime? ArrivalIso);
public sealed record WeatherReadingDto(
    string StopId, bool HasData, string? ConditionType, string? IconBaseUri,
    double? TempC, int? RainPct, string? Description);
```

- [ ] **Step 4: Write the failing test for the no-op fallback**

`backend/tests/MenuNest.Application.UnitTests/Trips/Maps/MissingConfigWeatherServiceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class MissingConfigWeatherServiceTests
{
    [Fact]
    public async Task Returns_no_data_for_every_point_and_never_throws()
    {
        var svc = new MissingConfigWeatherService();
        var points = new List<WeatherPoint> { new("s1", 13.7, 100.5, null), new("s2", 18.8, 98.9, null) };

        var readings = await svc.GetReadingsAsync(points, WeatherReadingKind.Now, CancellationToken.None);

        readings.Should().HaveCount(2);
        readings.Should().OnlyContain(r => r.HasData == false && r.ConditionType == null && r.TempC == null);
        readings.Select(r => r.StopId).Should().Equal("s1", "s2");
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~MissingConfigWeatherServiceTests"`
Expected: FAIL — `MissingConfigWeatherService` does not exist (compile error).

- [ ] **Step 6: Implement the no-op fallback**

`backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs` (mirrors `MissingConfigPlaceResolver`, but **returns** No-data instead of throwing — ADR-030/032):

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
namespace MenuNest.Infrastructure.Maps;

/// <summary>Registered when no Maps API key is configured — every point degrades to No-data
/// (never throws), so the itinerary still renders (ADR-030 / ADR-032).</summary>
public sealed class MissingConfigWeatherService : IWeatherService
{
    public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(
        IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WeatherReading>>(
            points.Select(p => new WeatherReading(p.StopId, false, null, null, null, null, null)).ToList());
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~MissingConfigWeatherServiceTests"`
Expected: PASS (1 test).

- [ ] **Step 8: Commit**

```bash
cd c:/Repo2/t/menunest
git add backend/src/MenuNest.Domain/Enums/WeatherReadingKind.cs \
  backend/src/MenuNest.Application/Abstractions/IWeatherService.cs \
  backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
  backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/Maps/MissingConfigWeatherServiceTests.cs
git commit -m "feat(trips): add IWeatherService seam + no-op fallback (#10)"
```

---

## Task B2: GoogleWeatherService — Now (currentConditions)

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs`

**Interfaces:**
- Consumes: `IWeatherService`, `WeatherPoint`, `WeatherReading`, `WeatherReadingKind` (Task B1); `GoogleMapsOptions` (existing).
- Produces: `GoogleWeatherService(IHttpClientFactory, IOptions<GoogleMapsOptions>, IMemoryCache, ILogger<GoogleWeatherService>)` implementing `IWeatherService`.

- [ ] **Step 1: Write the failing tests (Now: parse + failure)**

`backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs` (same `StubHandler`/`StubFactory`/`Build` scaffolding as `GoogleRouteServiceTests`):

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GoogleWeatherServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public StubHandler(HttpStatusCode status, string json = "") { _status = status; _json = json; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status)
            { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static GoogleWeatherService Build(HttpMessageHandler handler) => new(
        new StubFactory(handler),
        Options.Create(new GoogleMapsOptions { ApiKey = "test-key" }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<GoogleWeatherService>.Instance);

    private static readonly List<WeatherPoint> OnePoint = new() { new("s1", 13.7563, 100.5018, null) };

    [Fact]
    public async Task Now_parses_condition_temperature_and_rain()
    {
        const string json =
            "{\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\"," +
            "\"description\":{\"text\":\"มีเมฆมาก\",\"languageCode\":\"th\"},\"type\":\"CLOUDY\"}," +
            "\"temperature\":{\"unit\":\"CELSIUS\",\"degrees\":29.1}," +
            "\"precipitation\":{\"probability\":{\"type\":\"RAIN\",\"percent\":20}}}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));

        var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.StopId.Should().Be("s1");
        r.ConditionType.Should().Be("CLOUDY");
        r.IconBaseUri.Should().Be("https://maps.gstatic.com/weather/v1/cloudy");
        r.TempC.Should().Be(29.1);
        r.RainPct.Should().Be(20);
        r.Description.Should().Be("มีเมฆมาก");
    }

    [Fact]
    public async Task Now_failure_degrades_to_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));

        var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
        r.StopId.Should().Be("s1");
        r.ConditionType.Should().BeNull();
        r.TempC.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleWeatherServiceTests"`
Expected: FAIL — `GoogleWeatherService` does not exist (compile error).

- [ ] **Step 3: Implement GoogleWeatherService (Now path; forecast path stubbed for B3)**

`backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Google Weather API (weather.googleapis.com/v1). Now -> GET currentConditions:lookup;
// OnArrival -> GET forecast/hours:lookup?hours=240 then pick the hour bucket matching arrival.
// No X-Goog-FieldMask: the Weather API returns the full document without one (verified live);
// a wrong mask 400s. Key via X-Goog-Api-Key header. On ANY failure a point degrades to
// HasData=false (ADR-030) — never throws. Cache: Now 30 min, OnArrival 3 h.
namespace MenuNest.Infrastructure.Maps;

public sealed class GoogleWeatherService : IWeatherService
{
    private readonly IHttpClientFactory _http;
    private readonly GoogleMapsOptions _opts;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GoogleWeatherService> _log;

    public GoogleWeatherService(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts, IMemoryCache cache, ILogger<GoogleWeatherService> log)
    { _http = http; _opts = opts.Value; _cache = cache; _log = log; }

    public async Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
    {
        var result = new WeatherReading[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var key = CacheKey(p, kind);
            if (_cache.TryGetValue(key, out WeatherReading? hit) && hit is not null) { result[i] = hit; continue; }
            var reading = await FetchAsync(p, kind, ct);
            if (reading.HasData)
                _cache.Set(key, reading, kind == WeatherReadingKind.Now ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(3));
            result[i] = reading;
        }
        return result;
    }

    private async Task<WeatherReading> FetchAsync(WeatherPoint p, WeatherReadingKind kind, CancellationToken ct)
    {
        try
        {
            var lat = p.Lat.ToString(CultureInfo.InvariantCulture);
            var lng = p.Lng.ToString(CultureInfo.InvariantCulture);
            var url = kind == WeatherReadingKind.Now
                ? $"https://weather.googleapis.com/v1/currentConditions:lookup?location.latitude={lat}&location.longitude={lng}&unitsSystem=METRIC&languageCode=th"
                : $"https://weather.googleapis.com/v1/forecast/hours:lookup?location.latitude={lat}&location.longitude={lng}&hours=240&unitsSystem=METRIC&languageCode=th";

            var client = _http.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
            req.Headers.Add("X-Goog-Maps-Solution-ID", "gmp_git_agentskills_v1");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            var resp = await client.SendAsync(req, timeoutCts.Token);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeoutCts.Token));

            var el = kind == WeatherReadingKind.Now ? doc.RootElement : PickHour(doc.RootElement, p.ArrivalLocal);
            return el is null ? NoData(p.StopId) : ParseReading(el.Value, p.StopId);
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested(); // honour real caller cancellation; degrade only on Google failures/timeouts
            _log.LogWarning(ex, "Weather lookup failed for {StopId}; returning No-data.", p.StopId);
            return NoData(p.StopId);
        }
    }

    // OnArrival: pick the forecast hour whose location-local displayDateTime matches the arrival
    // wall-clock hour. Implemented fully in Task B3; Now never reaches this.
    private static JsonElement? PickHour(JsonElement root, DateTime? arrival) => null;

    private static WeatherReading ParseReading(JsonElement el, string stopId)
    {
        string? type = null, icon = null, desc = null;
        if (el.TryGetProperty("weatherCondition", out var wc))
        {
            if (wc.TryGetProperty("type", out var t)) type = t.GetString();
            if (wc.TryGetProperty("iconBaseUri", out var ib)) icon = ib.GetString();
            if (wc.TryGetProperty("description", out var de) && de.TryGetProperty("text", out var dtx)) desc = dtx.GetString();
        }
        double? temp = el.TryGetProperty("temperature", out var tp) && tp.TryGetProperty("degrees", out var dg) ? dg.GetDouble() : null;
        int? rain = el.TryGetProperty("precipitation", out var pr) && pr.TryGetProperty("probability", out var pb)
            && pb.TryGetProperty("percent", out var pc) ? pc.GetInt32() : null;
        var hasData = type is not null || temp is not null;
        return new WeatherReading(stopId, hasData, type, icon, temp, rain, desc);
    }

    private static WeatherReading NoData(string stopId) => new(stopId, false, null, null, null, null, null);

    private static string CacheKey(WeatherPoint p, WeatherReadingKind kind)
    {
        var baseKey = $"wx:{kind}:{p.Lat:F5},{p.Lng:F5}"; // ~5 dp, matching GoogleRouteService's leg key
        return kind == WeatherReadingKind.OnArrival && p.ArrivalLocal is { } a ? $"{baseKey}:{a:yyyyMMddHH}" : baseKey;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleWeatherServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
cd c:/Repo2/t/menunest
git add backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs
git commit -m "feat(trips): GoogleWeatherService current-conditions lookup (#10)"
```

---

## Task B3: GoogleWeatherService — On-arrival hour-bucket

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs:PickHour`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task B2.
- Produces: working `WeatherReadingKind.OnArrival` path (bucket selection by arrival hour).

- [ ] **Step 1: Write the failing tests (bucket match / no match / failure)**

Append to `GoogleWeatherServiceTests.cs` (inside the class). The forecast JSON carries two hours; arrival `2026-07-12T14:30` must select the `day:12, hours:14` bucket:

```csharp
    private const string ForecastJson =
        "{\"forecastHours\":[" +
        "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13}," +
        "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\",\"description\":{\"text\":\"มีเมฆมาก\"},\"type\":\"CLOUDY\"}," +
        "\"temperature\":{\"degrees\":31.0},\"precipitation\":{\"probability\":{\"percent\":30}}}," +
        "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14}," +
        "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/drizzle\",\"description\":{\"text\":\"ฝนตกเบาบาง\"},\"type\":\"LIGHT_RAIN\"}," +
        "\"temperature\":{\"degrees\":30.0},\"precipitation\":{\"probability\":{\"percent\":55}}}" +
        "]}";

    [Fact]
    public async Task OnArrival_picks_the_hour_bucket_matching_the_arrival_hour()
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 30, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.ConditionType.Should().Be("LIGHT_RAIN"); // the 14:00 bucket, not the 13:00 one
        r.TempC.Should().Be(30.0);
        r.RainPct.Should().Be(55);
    }

    [Fact]
    public async Task OnArrival_with_no_matching_bucket_is_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 20, 0, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }

    [Fact]
    public async Task OnArrival_failure_degrades_to_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.InternalServerError));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 0, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }

    [Fact]
    public async Task OnArrival_with_null_arrival_is_no_data() // tolerance: missing/late arrival ⇒ No-data, not an error (ADR-032)
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, null) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleWeatherServiceTests"`
Expected: `OnArrival_picks_the_hour_bucket_matching_the_arrival_hour` FAILS (returns No-data because `PickHour` is stubbed to `null`); the other two already pass.

- [ ] **Step 3: Implement PickHour**

Replace the stub `PickHour` in `GoogleWeatherService.cs` with:

```csharp
    // OnArrival: pick the forecast hour whose location-local displayDateTime matches the arrival
    // wall-clock hour (arrival is day.date + scheduled HH:MM). No match (out of the 240h horizon
    // that slipped past the client gate, or a gap) => null => No-data.
    private static JsonElement? PickHour(JsonElement root, DateTime? arrival)
    {
        if (arrival is not { } a || !root.TryGetProperty("forecastHours", out var hours)) return null;
        foreach (var h in hours.EnumerateArray())
        {
            if (!h.TryGetProperty("displayDateTime", out var dt)) continue;
            var y = dt.TryGetProperty("year", out var yy) ? yy.GetInt32() : 0;
            var mo = dt.TryGetProperty("month", out var mm) ? mm.GetInt32() : 0;
            var d = dt.TryGetProperty("day", out var dd) ? dd.GetInt32() : 0;
            var hr = dt.TryGetProperty("hours", out var hh) ? hh.GetInt32() : -1;
            if (y == a.Year && mo == a.Month && d == a.Day && hr == a.Hour) return h;
        }
        return null;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GoogleWeatherServiceTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
cd c:/Repo2/t/menunest
git add backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs
git commit -m "feat(trips): GoogleWeatherService on-arrival hourly bucket (#10)"
```

---

## Task B4: GetStopWeather use-case (Query + Handler + Validator)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/GetStopWeatherQuery.cs` · `GetStopWeatherHandler.cs` · `GetStopWeatherValidator.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs` · `GetStopWeatherValidatorTests.cs`

**Interfaces:**
- Consumes: `IWeatherService`, `WeatherPoint`, `WeatherReading`, `WeatherReadingKind` (B1); `WeatherPointDto`, `WeatherReadingDto` (B1).
- Produces: `record GetStopWeatherQuery(WeatherReadingKind Kind, IReadOnlyList<WeatherPointDto> Points) : IQuery<IReadOnlyList<WeatherReadingDto>>`; `GetStopWeatherHandler`; `GetStopWeatherValidator`.

- [ ] **Step 1: Create the query**

`backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/GetStopWeatherQuery.cs` (references `WeatherPointDto`/`WeatherReadingDto` from the parent `...Trips` namespace via C# outward name resolution — no `using` needed, exactly as `ResolvePlaceCommand` references `ResolvedPlaceDto`):

```csharp
using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed record GetStopWeatherQuery(WeatherReadingKind Kind, IReadOnlyList<WeatherPointDto> Points)
    : IQuery<IReadOnlyList<WeatherReadingDto>>;
```

- [ ] **Step 2: Write the failing validator test**

`backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetStopWeatherValidatorTests
{
    private readonly GetStopWeatherValidator _v = new();

    [Fact]
    public void Rejects_empty_points()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now, new List<WeatherPointDto>());
        _v.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Points);
    }

    [Fact]
    public void Rejects_out_of_range_latitude()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 999, 100, null) });
        _v.TestValidate(q).ShouldHaveValidationErrorFor("Points[0].Lat");
    }

    [Fact]
    public void Accepts_valid_points()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });
        _v.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Accepts_null_arrivalIso_on_on_arrival_points() // tolerance: arrivalIso is optional; a null yields No-data downstream, not a validation error (ADR-032)
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.OnArrival,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });
        _v.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetStopWeatherValidatorTests"`
Expected: FAIL — `GetStopWeatherValidator` does not exist (compile error).

- [ ] **Step 4: Implement the validator**

`backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/GetStopWeatherValidator.cs`:

```csharp
using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed class GetStopWeatherValidator : AbstractValidator<GetStopWeatherQuery>
{
    public GetStopWeatherValidator()
    {
        RuleFor(x => x.Points).NotEmpty();
        RuleForEach(x => x.Points).ChildRules(p =>
        {
            p.RuleFor(x => x.StopId).NotEmpty();
            p.RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
            p.RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        });
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetStopWeatherValidatorTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Write the failing handler test**

`backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs` (a hand-rolled stub `IWeatherService` captures the points it was handed, so we assert both the DTO mapping and that arrival is nulled for `Now`):

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetStopWeatherHandlerTests
{
    private sealed class StubWeather : IWeatherService
    {
        public IReadOnlyList<WeatherPoint>? Received;
        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
        {
            Received = points;
            IReadOnlyList<WeatherReading> readings = points
                .Select(p => new WeatherReading(p.StopId, true, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 29.1, 20, "มีเมฆมาก"))
                .ToList();
            return Task.FromResult(readings);
        }
    }

    private static GetStopWeatherHandler Build(StubWeather w) => new(w, new GetStopWeatherValidator());

    [Fact]
    public async Task Maps_service_readings_to_dtos()
    {
        var handler = Build(new StubWeather());
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });

        var dtos = await handler.Handle(q, CancellationToken.None);

        dtos.Should().HaveCount(1);
        dtos[0].StopId.Should().Be("s1");
        dtos[0].HasData.Should().BeTrue();
        dtos[0].ConditionType.Should().Be("CLOUDY");
        dtos[0].TempC.Should().Be(29.1);
        dtos[0].RainPct.Should().Be(20);
        dtos[0].Description.Should().Be("มีเมฆมาก");
    }

    [Fact]
    public async Task Now_ignores_arrivalIso_when_building_points()
    {
        var stub = new StubWeather();
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, new DateTime(2026, 7, 12, 14, 0, 0)) });

        await Build(stub).Handle(q, CancellationToken.None);

        stub.Received![0].ArrivalLocal.Should().BeNull(); // Now => arrival dropped
    }

    [Fact]
    public async Task OnArrival_forwards_arrivalIso_to_the_service()
    {
        var stub = new StubWeather();
        var arrival = new DateTime(2026, 7, 12, 14, 0, 0);
        var q = new GetStopWeatherQuery(WeatherReadingKind.OnArrival,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, arrival) });

        await Build(stub).Handle(q, CancellationToken.None);

        stub.Received![0].ArrivalLocal.Should().Be(arrival);
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetStopWeatherHandlerTests"`
Expected: FAIL — `GetStopWeatherHandler` does not exist (compile error).

- [ ] **Step 8: Implement the handler**

`backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/GetStopWeatherHandler.cs` (mirrors `ResolvePlaceHandler`: manual `ValidateAndThrowAsync`, `ValueTask` return; maps `WeatherReading → WeatherReadingDto` field-for-field like `GetItineraryHandler` maps `LegTime → LegDto`; **no** `IUserProvisioner`/DbContext — weather needs only the supplied coordinates):

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed class GetStopWeatherHandler : IQueryHandler<GetStopWeatherQuery, IReadOnlyList<WeatherReadingDto>>
{
    private readonly IWeatherService _weather;
    private readonly IValidator<GetStopWeatherQuery> _validator;
    public GetStopWeatherHandler(IWeatherService weather, IValidator<GetStopWeatherQuery> validator)
    { _weather = weather; _validator = validator; }

    public async ValueTask<IReadOnlyList<WeatherReadingDto>> Handle(GetStopWeatherQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);
        var points = q.Points
            .Select(p => new WeatherPoint(p.StopId, p.Lat, p.Lng,
                q.Kind == WeatherReadingKind.OnArrival ? p.ArrivalIso : null))
            .ToList();
        var readings = await _weather.GetReadingsAsync(points, q.Kind, ct);
        return readings
            .Select(r => new WeatherReadingDto(r.StopId, r.HasData, r.ConditionType, r.IconBaseUri, r.TempC, r.RainPct, r.Description))
            .ToList();
    }
}
```

- [ ] **Step 9: Run both test classes to verify they pass**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetStopWeather"`
Expected: PASS (7 tests: 4 validator + 3 handler).

- [ ] **Step 10: Commit**

```bash
cd c:/Repo2/t/menunest
git add backend/src/MenuNest.Application/UseCases/Trips/GetStopWeather/ \
  backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherValidatorTests.cs
git commit -m "feat(trips): GetStopWeather query+handler+validator (#10)"
```

---

## Task B5: DI wiring + WebApi endpoint

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/DependencyInjection.cs` (Maps block)
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/WeatherServiceRegistrationTests.cs`

**Interfaces:**
- Consumes: `GoogleWeatherService` (B2/B3), `MissingConfigWeatherService` (B1), `GetStopWeatherQuery` (B4).
- Produces: `IWeatherService` DI registration (Google when `GoogleMaps:ApiKey` present, no-op otherwise); `POST api/trips/weather`.

- [ ] **Step 1: Write the failing DI-registration test**

`backend/tests/MenuNest.Application.UnitTests/Trips/Maps/WeatherServiceRegistrationTests.cs` (inspects the registered `ServiceDescriptor` — no service is instantiated; `AddInfrastructure` reads `ConnectionStrings:DefaultConnection` synchronously at registration, so the test seeds it — `AddDbContext`/`UseSqlServer` never opens a connection at registration):

```csharp
using System.Linq;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Infrastructure;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class WeatherServiceRegistrationTests
{
    private static Type ResolveWeatherImpl(string? mapsKey)
    {
        // AddInfrastructure reads ConnectionStrings:DefaultConnection at registration and throws
        // if it is missing; seed any non-empty value (UseSqlServer does not connect here). This is
        // the ONLY config key required at registration time.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=test;Database=Test;Trusted_Connection=True;",
        };
        if (mapsKey is not null) settings["GoogleMaps:ApiKey"] = mapsKey;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(config);

        return services.Single(d => d.ServiceType == typeof(IWeatherService)).ImplementationType!;
    }

    [Fact]
    public void Uses_Google_when_maps_key_present()
        => ResolveWeatherImpl("test-key").Should().Be<GoogleWeatherService>();

    [Fact]
    public void Uses_noop_when_maps_key_absent()
        => ResolveWeatherImpl(null).Should().Be<MissingConfigWeatherService>();
}
```

> Note: `AddInfrastructure(this IServiceCollection, IConfiguration)` (namespace `MenuNest.Infrastructure`) reads `ConnectionStrings:DefaultConnection` synchronously at registration and throws `InvalidOperationException` if it is absent — hence the seeded value above (the only key required at registration; the Storage/Maps/share-token guards run at construction/resolution or are optional). The test then reaches the `Single(...)` assertion and inspects only the `IWeatherService` descriptor (no service is resolved).

- [ ] **Step 2: Run it to verify it fails**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~WeatherServiceRegistrationTests"`
Expected: FAIL — no `IWeatherService` descriptor is registered (`Single` throws).

- [ ] **Step 3: Add the DI branch**

In `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`, immediately **after** the existing `IRouteService` branch (the `AddMemoryCache()` call above it is shared), add:

```csharp
        // Weather service — Google Weather API when the key is present; otherwise a no-op that
        // returns No-data for every point (weather degrades honestly, never blocks the page). ADR-032.
        if (!string.IsNullOrWhiteSpace(mapsKey))
            services.AddScoped<IWeatherService, GoogleWeatherService>();
        else
            services.AddScoped<IWeatherService, MissingConfigWeatherService>();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd c:/Repo2/t/menunest/backend && dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~WeatherServiceRegistrationTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Add the controller endpoint**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, add the `using` and the action (binds the query directly with `[FromBody]`, exactly like `Resolve`):

```csharp
using MenuNest.Application.UseCases.Trips.GetStopWeather;
```

```csharp
    [HttpPost("api/trips/weather")]
    public async Task<ActionResult<IReadOnlyList<WeatherReadingDto>>> Weather([FromBody] GetStopWeatherQuery q, CancellationToken ct)
        => Ok(await _mediator.Send(q, ct));
```

- [ ] **Step 6: Build the solution + run the full backend suite**

Run: `cd c:/Repo2/t/menunest/backend && dotnet build MenuNest.sln`
Expected: Build succeeded, 0 errors.

Run: `cd c:/Repo2/t/menunest/backend && dotnet test MenuNest.sln`
Expected: PASS — all tests green (existing + the new weather tests).

- [ ] **Step 7: Commit**

```bash
cd c:/Repo2/t/menunest
git add backend/src/MenuNest.Infrastructure/DependencyInjection.cs \
  backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/Maps/WeatherServiceRegistrationTests.cs
git commit -m "feat(trips): wire IWeatherService + POST api/trips/weather (#10)"
```

---

## Task F1: RTK Query endpoint + DTO types + hook

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Produces: `interface WeatherPointDto { stopId: string; lat: number; lng: number; arrivalIso?: string }`; `interface WeatherReadingDto { stopId: string; hasData: boolean; conditionType: string | null; iconBaseUri: string | null; tempC: number | null; rainPct: number | null; description: string | null }`; endpoint `getStopWeather` and hook `useGetStopWeatherQuery`.

- [ ] **Step 1: Add the DTO interfaces**

In `frontend/src/shared/api/api.ts`, next to the other Trip DTO interfaces (`TripPlaceDto`, `StopDto`, `ItineraryDayDto`, …), add:

```ts
export interface WeatherPointDto { stopId: string; lat: number; lng: number; arrivalIso?: string }
export interface WeatherReadingDto {
    stopId: string; hasData: boolean; conditionType: string | null; iconBaseUri: string | null
    tempC: number | null; rainPct: number | null; description: string | null
}
```

- [ ] **Step 2: Add the endpoint**

Inside the single `endpoints: (build) => ({ ... })` block of the **authenticated `api`** slice, after the Trips endpoints, add a new section. Model it as a POST-body `build.query` with an order-insensitive cache key (the `stockCheckBatch` pattern). No `providesTags` — weather is ephemeral:

```ts
        // -------------------- Trip weather --------------------
        getStopWeather: build.query<
            WeatherReadingDto[],
            {kind: 'Now' | 'OnArrival'; points: WeatherPointDto[]}
        >({
            query: ({kind, points}) => ({
                url: '/api/trips/weather',
                method: 'POST',
                body: {kind, points: [...points].sort((a, b) => a.stopId.localeCompare(b.stopId))},
            }),
            // Cache key must not depend on point order (the schedule can produce the same set
            // in any order). Weather is ephemeral, so no providesTags and a short retention.
            serializeQueryArgs: ({endpointName, queryArgs}) => ({
                endpointName,
                kind: queryArgs.kind,
                points: [...queryArgs.points].sort((a, b) => a.stopId.localeCompare(b.stopId)),
            }),
            keepUnusedDataFor: 300,
        }),
```

- [ ] **Step 3: Export the hook**

In the `export const { ... } = api` destructure block, add the Trips-section hook:

```ts
    useGetStopWeatherQuery,
```

- [ ] **Step 4: Typecheck**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc -b`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/shared/api/api.ts
git commit -m "feat(trips): add getStopWeather RTK endpoint + weather DTOs (#10)"
```

---

## Task F2: Pure weather helpers (TDD)

**Files:**
- Create: `frontend/src/pages/trips/lib/weather.ts`
- Test: `frontend/src/pages/trips/lib/weather.test.ts`

**Interfaces:**
- Consumes: `WeatherPointDto`, `WeatherReadingDto` (F1).
- Produces: `weatherWindow(arrivalMs, nowMs) => 'past' | 'ok' | 'beyond'`; `iconUrl(base, isDark) => string`; `RAIN_TINT_THRESHOLD = 60` + `isRainy(pct)`; `weatherChipState(isLoading, reading) => 'loading' | 'nodata' | 'data'`; `arrivalIso(dayDate, hhmm) => string`; `buildWeatherBatches(stops, nowMs) => {now, arrival}`.

- [ ] **Step 1: Write the failing tests**

`frontend/src/pages/trips/lib/weather.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {
  weatherWindow, iconUrl, isRainy, RAIN_TINT_THRESHOLD, weatherChipState, arrivalIso, buildWeatherBatches,
} from './weather'

const HOUR = 3600_000
const H240 = 240 * HOUR

describe('weatherWindow', () => {
  const now = 1_000_000_000
  it('is past when arrival < now', () => expect(weatherWindow(now - 1, now)).toBe('past'))
  it('is ok at exactly now + 240h (inclusive)', () => expect(weatherWindow(now + H240, now)).toBe('ok'))
  it('is beyond just past 240h', () => expect(weatherWindow(now + H240 + 60_000, now)).toBe('beyond'))
  it('is ok inside the window', () => expect(weatherWindow(now + HOUR, now)).toBe('ok'))
})

describe('iconUrl', () => {
  const base = 'https://maps.gstatic.com/weather/v1/cloudy'
  it('appends .svg in light theme', () => expect(iconUrl(base, false)).toBe(`${base}.svg`))
  it('appends _dark.svg in dark theme', () => expect(iconUrl(base, true)).toBe(`${base}_dark.svg`))
})

describe('isRainy', () => {
  it('is true at the threshold', () => expect(isRainy(RAIN_TINT_THRESHOLD)).toBe(true))
  it('is false just below', () => expect(isRainy(RAIN_TINT_THRESHOLD - 1)).toBe(false))
  it('treats null as not rainy', () => expect(isRainy(null)).toBe(false))
})

describe('weatherChipState', () => {
  it('is loading when loading and no reading yet', () => expect(weatherChipState(true, undefined)).toBe('loading'))
  it('is nodata when reading missing and not loading', () => expect(weatherChipState(false, undefined)).toBe('nodata'))
  it('is nodata when reading has no data', () =>
    expect(weatherChipState(false, {stopId: 's', hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null, description: null})).toBe('nodata'))
  it('is data when reading has data', () =>
    expect(weatherChipState(false, {stopId: 's', hasData: true, conditionType: 'CLOUDY', iconBaseUri: 'x', tempC: 29, rainPct: 20, description: 'y'})).toBe('data'))
})

describe('arrivalIso', () => {
  it('joins day date + HH:MM into a local wall-clock ISO', () =>
    expect(arrivalIso('2026-07-12', '14:30')).toBe('2026-07-12T14:30:00'))
  it('tolerates a full timestamp date and HH:MM:SS', () =>
    expect(arrivalIso('2026-07-12T00:00:00', '14:30:00')).toBe('2026-07-12T14:30:00'))
})

describe('buildWeatherBatches', () => {
  const now = Date.parse('2026-07-12T00:00:00')
  const stops = [
    {stopId: 's1', lat: 13.7, lng: 100.5, arrivalIso: '2026-07-12T09:00:00'},        // in window
    {stopId: 's2', lat: NaN, lng: 100.5, arrivalIso: '2026-07-12T10:00:00'},         // no coords -> dropped from both
    {stopId: 's3', lat: 18.8, lng: 98.9, arrivalIso: '2026-09-01T10:00:00'},         // beyond horizon
  ]
  const {now: nowPts, arrival} = buildWeatherBatches(stops, now)

  it('includes every finite-coord stop in the Now batch', () =>
    expect(nowPts.map((p) => p.stopId)).toEqual(['s1', 's3']))
  it('includes only in-window stops in the On-arrival batch, carrying arrivalIso', () => {
    expect(arrival.map((p) => p.stopId)).toEqual(['s1'])
    expect(arrival[0].arrivalIso).toBe('2026-07-12T09:00:00')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd c:/Repo2/t/menunest/frontend && npx vitest run src/pages/trips/lib/weather.test.ts`
Expected: FAIL — `./weather` cannot be resolved.

- [ ] **Step 3: Implement the helpers**

`frontend/src/pages/trips/lib/weather.ts`:

```ts
import type {WeatherPointDto, WeatherReadingDto} from '../../../shared/api/api'

// Google's daily/hourly forecast horizon is 10 days = 240 hours (verified: hours=241 -> HTTP 400).
const HORIZON_MS = 240 * 60 * 60 * 1000
export type WeatherWindow = 'past' | 'ok' | 'beyond'

/** Classify an arrival instant against the live clock; 'ok' is the only fetchable window. */
export function weatherWindow(arrivalMs: number, nowMs: number): WeatherWindow {
  if (arrivalMs < nowMs) return 'past'
  if (arrivalMs > nowMs + HORIZON_MS) return 'beyond'
  return 'ok'
}

/** Google weather condition icon URL: light `.svg`, dark `_dark.svg` (ADR-031). */
export function iconUrl(iconBaseUri: string, isDark: boolean): string {
  return `${iconBaseUri}${isDark ? '_dark' : ''}.svg`
}

export const RAIN_TINT_THRESHOLD = 60
/** The On-arrival chip gets the deeper "rainy" tint at/above this rain probability. */
export function isRainy(rainPct: number | null | undefined): boolean {
  return (rainPct ?? 0) >= RAIN_TINT_THRESHOLD
}

export type ChipState = 'loading' | 'nodata' | 'data'
/** Which visual state a chip renders, given its query loading flag and (maybe) a reading. */
export function weatherChipState(isLoading: boolean, reading: WeatherReadingDto | undefined): ChipState {
  if (isLoading && !reading) return 'loading'
  if (!reading || !reading.hasData) return 'nodata'
  return 'data'
}

/** Local wall-clock ISO for a stop's arrival: day date (yyyy-MM-dd) + schedule HH:MM. */
export function arrivalIso(dayDate: string, hhmm: string): string {
  return `${dayDate.slice(0, 10)}T${hhmm.slice(0, 5)}:00`
}

interface WeatherStop {
  stopId: string
  lat: number
  lng: number
  arrivalIso: string
}

/** Split scheduled stops into the two batches the endpoint needs. Now = every finite-coord stop;
 *  On-arrival = only stops whose arrival is within [now, now+240h] (past/beyond are gated out and
 *  render No-data client-side without a request). */
export function buildWeatherBatches(
  stops: WeatherStop[],
  nowMs: number,
): {now: WeatherPointDto[]; arrival: WeatherPointDto[]} {
  const now: WeatherPointDto[] = []
  const arrival: WeatherPointDto[] = []
  for (const s of stops) {
    if (!Number.isFinite(s.lat) || !Number.isFinite(s.lng)) continue
    now.push({stopId: s.stopId, lat: s.lat, lng: s.lng})
    if (weatherWindow(Date.parse(s.arrivalIso), nowMs) === 'ok') {
      arrival.push({stopId: s.stopId, lat: s.lat, lng: s.lng, arrivalIso: s.arrivalIso})
    }
  }
  return {now, arrival}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd c:/Repo2/t/menunest/frontend && npx vitest run src/pages/trips/lib/weather.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/pages/trips/lib/weather.ts frontend/src/pages/trips/lib/weather.test.ts
git commit -m "feat(trips): pure weather helpers (window/icon/rainy/chip-state/batches) (#10)"
```

---

## Task F3: Weather icons + WeatherChip component

**Files:**
- Create: `frontend/src/pages/trips/components/WeatherIcons.tsx`
- Create: `frontend/src/pages/trips/components/WeatherChip.tsx`

**Interfaces:**
- Consumes: `iconUrl`, `isRainy`, `weatherChipState` (F2); `WeatherReadingDto` (F1).
- Produces: `RainDropIcon`, `NoWeatherIcon` (props: `{className?: string}`); `WeatherChip({kind, reading, isLoading, isDark?})`.

- [ ] **Step 1: Create the icon components**

`frontend/src/pages/trips/components/WeatherIcons.tsx` (follows the `TripFormIcons` `IconProps` + shared `base` convention — inline SVG, `1em`/`currentColor`, never emoji):

```tsx
type IconProps = {className?: string}

const base = {
  viewBox: '0 0 24 24',
  width: '1em',
  height: '1em',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
  focusable: false,
}

/** Rain-drop — precedes the rain-probability % in a weather chip. */
export function RainDropIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M12 3s6 7 6 10.5A6 6 0 0 1 6 13.5C6 10 12 3 12 3z" />
    </svg>
  )
}

/** Slashed cloud — the "no weather data" chip glyph. */
export function NoWeatherIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M17.5 19a4.5 4.5 0 0 0 .3-9 6 6 0 0 0-11.4-1.5" />
      <path d="M4 4l16 16" />
    </svg>
  )
}
```

- [ ] **Step 2: Create the WeatherChip component**

`frontend/src/pages/trips/components/WeatherChip.tsx`. UI copy is Thai (`ตอนนี้` / `ไปถึง` / `ไม่มีข้อมูล`); the condition `<img>` alt is the Thai `description`. Light theme by default (`isDark = false`) — the trips UI is light; the prop is here so a future theme signal can flip it:

```tsx
import type {WeatherReadingDto} from '../../../shared/api/api'
import {iconUrl, isRainy, weatherChipState} from '../lib/weather'
import {RainDropIcon, NoWeatherIcon} from './WeatherIcons'

const LABEL = {now: 'ตอนนี้', arr: 'ไปถึง'} as const

export function WeatherChip({
  kind,
  reading,
  isLoading,
  isDark = false,
}: {
  kind: 'now' | 'arr'
  reading: WeatherReadingDto | undefined
  isLoading: boolean
  isDark?: boolean
}) {
  const state = weatherChipState(isLoading, reading)

  if (state === 'loading') {
    return <span className={`chip wx ${kind} loading`} aria-hidden="true"><span className="lab">{LABEL[kind]}</span></span>
  }
  if (state === 'nodata') {
    return (
      <span className="chip wx nodata">
        <span className="lab">{LABEL[kind]}</span>
        <NoWeatherIcon />
        ไม่มีข้อมูลอากาศ
      </span>
    )
  }

  const r = reading! // state === 'data' ⇒ reading is present and hasData
  const rainy = kind === 'arr' && isRainy(r.rainPct)
  return (
    <span className={`chip wx ${kind}${rainy ? ' rainy' : ''}`}>
      <span className="lab">{LABEL[kind]}</span>
      {r.iconBaseUri && <img src={iconUrl(r.iconBaseUri, isDark)} alt={r.description ?? ''} width={18} height={18} />}
      {r.tempC != null && <span className="t">{Math.round(r.tempC)}°</span>}
      {r.rainPct != null && (
        <span className="r"><RainDropIcon />{r.rainPct}%</span>
      )}
    </span>
  )
}
```

- [ ] **Step 3: Typecheck**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc -b`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/pages/trips/components/WeatherIcons.tsx frontend/src/pages/trips/components/WeatherChip.tsx
git commit -m "feat(trips): WeatherIcons + WeatherChip component (#10)"
```

---

## Task F4: Weather chip styles

**Files:**
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: the class names emitted by `WeatherChip` (F3): `.chip.wx.now`, `.chip.wx.arr`, `.chip.wx.arr.rainy`, `.chip.wx.nodata`, `.chip.wx.loading`, `.lab`, `.t`, `.r`.

- [ ] **Step 1: Add the tokens**

In `frontend/src/pages/trips/trips-tokens.css`, add the weather tokens inside the existing `.trips-page, .trip-detail { ... }` token block (next to `--teal`, `--warn`, …). These are the exact values used in the confirmed mock:

```css
  --now:        #0b7a87;
  --now-bg:     #e3f5f6;
  --arr:        #1863b7;
  --arr-bg:     #e9f1fb;
  --arr-rain:   #0f5bb0;
  --arr-rain-bg:#dbe9fb;
```

- [ ] **Step 2: Add the chip rules**

Append to `trips-tokens.css`, right after the existing `.chip.dwell` rule (so the weather chips sit in the same `.stop-chips` row):

```css
/* ── Weather chips (per-Stop: "ตอนนี้" + "ไปถึง") — ADR-028/030/031 ── */
.chip.wx { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; padding: 2px 9px 2px 5px; }
.chip.wx .lab { font-size: 9px; font-weight: 700; opacity: 0.72; text-transform: uppercase; letter-spacing: 0.03em; }
.chip.wx img { width: 18px; height: 18px; display: block; }
.chip.wx .t { font-weight: 700; }
.chip.wx .r { display: inline-flex; align-items: center; gap: 1px; font-weight: 700; }
.chip.wx .r svg { width: 9px; height: 11px; }
.chip.wx.now { background: var(--now-bg); color: var(--now); }
.chip.wx.arr { background: var(--arr-bg); color: var(--arr); }
.chip.wx.arr.rainy { background: var(--arr-rain-bg); color: var(--arr-rain); }
.chip.wx.nodata { background: #f1f5f9; color: var(--muted); }
.chip.wx.nodata svg { width: 15px; height: 15px; }
.chip.wx.loading { background: #f1f5f9; color: transparent; min-width: 64px; }
```

- [ ] **Step 3: Build to verify the CSS compiles**

Run: `cd c:/Repo2/t/menunest/frontend && npm run build`
Expected: `tsc -b && vite build` succeeds.

- [ ] **Step 4: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): weather chip tokens + styles (#10)"
```

---

## Task F5: useStopWeather hook

**Files:**
- Create: `frontend/src/pages/trips/hooks/useStopWeather.ts`

**Interfaces:**
- Consumes: `useGetStopWeatherQuery`, `WeatherReadingDto`, `ItineraryDayDto`, `TripPlaceDto` (F1/api); `ScheduledStopWithFlag` from `useSchedule`; `arrivalIso`, `buildWeatherBatches` (F2).
- Produces: `useStopWeather(day, scheduled, placesById) => Record<stopId, {now?: WeatherReadingDto; arrival: WeatherReadingDto | undefined; nowLoading: boolean; arrivalLoading: boolean}>`. Stops gated out of the On-arrival window get a synthetic No-data `arrival` reading with `arrivalLoading=false`.

- [ ] **Step 1: Implement the hook**

`frontend/src/pages/trips/hooks/useStopWeather.ts`:

```ts
import {useGetStopWeatherQuery} from '../../../shared/api/api'
import type {ItineraryDayDto, TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {ScheduledStopWithFlag} from './useSchedule'
import {arrivalIso, buildWeatherBatches} from '../lib/weather'

export interface StopWeather {
  now: WeatherReadingDto | undefined
  arrival: WeatherReadingDto | undefined
  nowLoading: boolean
  arrivalLoading: boolean
}

const noData = (stopId: string): WeatherReadingDto => ({
  stopId, hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null, description: null,
})

export function useStopWeather(
  day: ItineraryDayDto,
  scheduled: ScheduledStopWithFlag[],
  placesById: Record<string, TripPlaceDto>,
): Record<string, StopWeather> {
  // Recomputed on every render (NOT memoised on a captured clock) so the horizon gate is
  // re-evaluated on read — a Stop flips data<->No-data as its arrival crosses now+240h (ADR-030).
  // RTK Query collapses the batch args to a stable cache key via the endpoint's serializeQueryArgs,
  // so passing fresh arrays each render does not cause refetch churn.
  const stops = scheduled
    .map((s) => {
      const p = placesById[s.stop.tripPlaceId]
      return p ? {stopId: s.stop.id, lat: p.lat, lng: p.lng, arrivalIso: arrivalIso(day.date, s.arrival)} : null
    })
    .filter((s): s is {stopId: string; lat: number; lng: number; arrivalIso: string} => s !== null)

  const batches = buildWeatherBatches(stops, Date.now())
  const inArrivalBatch = new Set(batches.arrival.map((p) => p.stopId))

  const {data: nowData, isLoading: nowLoading} = useGetStopWeatherQuery(
    {kind: 'Now', points: batches.now},
    {skip: batches.now.length === 0},
  )
  const {data: arrData, isLoading: arrLoading} = useGetStopWeatherQuery(
    {kind: 'OnArrival', points: batches.arrival},
    {skip: batches.arrival.length === 0},
  )

  const nowById = new Map((nowData ?? []).map((r) => [r.stopId, r]))
  const arrById = new Map((arrData ?? []).map((r) => [r.stopId, r]))
  const out: Record<string, StopWeather> = {}
  for (const s of stops) {
    const gatedOut = !inArrivalBatch.has(s.stopId)
    out[s.stopId] = {
      now: nowById.get(s.stopId),
      // Past/beyond stops are gated out client-side: synthetic No-data, never "loading".
      arrival: gatedOut ? noData(s.stopId) : arrById.get(s.stopId),
      nowLoading,
      arrivalLoading: gatedOut ? false : arrLoading,
    }
  }
  return out
}
```

- [ ] **Step 2: Typecheck**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc -b`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/pages/trips/hooks/useStopWeather.ts
git commit -m "feat(trips): useStopWeather hook (Now + On-arrival batches) (#10)"
```

---

## Task F6: Wire chips into the itinerary

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`

**Interfaces:**
- Consumes: `WeatherChip` (F3); `useStopWeather` + `StopWeather` (F5).
- Produces: two `WeatherChip`s rendered in each stop card's `.stop-chips` row.

- [ ] **Step 1: Add weather props to ItineraryStopCard**

In `frontend/src/pages/trips/components/ItineraryStopCard.tsx`, import `WeatherChip` and extend the existing `WeatherReadingDto` type import (the file already has `import type {TripPlaceDto} from '../../../shared/api/api'` at line 2 — add `WeatherReadingDto` to it), then add three optional props (additive — when omitted the card renders exactly as today):

```tsx
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import {WeatherChip} from './WeatherChip'
```

Add to the destructured props and the prop type:

```tsx
  nowReading,
  arrivalReading,
  weatherLoading = false,
```

```tsx
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
```

Then, inside the existing `.stop-chips` div, **after** the dwell chip, render the two weather chips:

```tsx
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {dwell} น.</span>
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>
```

- [ ] **Step 2: Call useStopWeather in ItineraryTab and pass readings down**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`:

Import the hook (after the other hook imports):

```tsx
import {useStopWeather} from '../hooks/useStopWeather'
```

After the existing `const {scheduled, dayEnd, totalTravelSeconds} = useSchedule(day ?? EMPTY_DAY, placesById)` line, add:

```tsx
  const stopWeather = useStopWeather(day ?? EMPTY_DAY, scheduled, placesById)
```

In the `scheduled.map(...)` render, pass the per-stop readings to `ItineraryStopCard` (add these three props to the existing `<ItineraryStopCard ... />`):

```tsx
                  nowReading={stopWeather[s.stop.id]?.now}
                  arrivalReading={stopWeather[s.stop.id]?.arrival}
                  weatherLoading={(stopWeather[s.stop.id]?.nowLoading ?? false) || (stopWeather[s.stop.id]?.arrivalLoading ?? false)}
```

- [ ] **Step 3: Typecheck + build**

Run: `cd c:/Repo2/t/menunest/frontend && npm run build`
Expected: `tsc -b && vite build` succeeds.

- [ ] **Step 4: Run the full frontend + backend test suites**

Run: `cd c:/Repo2/t/menunest/frontend && npm test`
Expected: all Vitest tests pass (including `weather.test.ts`).

Run: `cd c:/Repo2/t/menunest/backend && dotnet test MenuNest.sln`
Expected: all backend tests pass.

- [ ] **Step 5: Visual verification against the mock**

Start the app (frontend dev server + backend, per the repo's run flow), open a Trip's itinerary, and confirm against [docs/mocks/trip-weather-mock.html](../../mocks/trip-weather-mock.html):
- Each stop shows a **ตอนนี้** chip (teal) and a **ไปถึง** chip (blue).
- A stop with high arrival rain% (≥60) shows the deeper-blue **rainy** tint on the ไปถึง chip.
- A far-future day (>10 days out) shows **ตอนนี้** with data and **ไปถึง** as **ไม่มีข้อมูลอากาศ** (slashed-cloud).
- Icons load from `maps.gstatic.com` (condition SVG); temp shows `NN°`; rain shows the drop + `NN%`.

- [ ] **Step 6: Commit**

```bash
cd c:/Repo2/t/menunest
git add frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/ItineraryTab.tsx
git commit -m "feat(trips): render per-stop Now + On-arrival weather chips (closes #10)"
```

---

## Definition of done

- Backend: `cd backend && dotnet test MenuNest.sln` — all green (5 new weather test classes + existing suite).
- Frontend: `cd frontend && npm test` — all green (incl. `weather.test.ts`); `npm run build` — clean.
- Visual: itinerary stops show two weather chips matching the confirmed mock, incl. the No-data state.
- Issue #10 auto-closes when the final commit merges to `main`.
- No EF migration exists or is needed (nothing persisted).
