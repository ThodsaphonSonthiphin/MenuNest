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
