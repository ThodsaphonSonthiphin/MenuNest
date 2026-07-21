using System.Globalization;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Google Weather API (weather.googleapis.com/v1). Now -> GET currentConditions:lookup;
// OnArrival -> GET forecast/hours:lookup?hours=240, following nextPageToken (the API returns only 24
// buckets/page) until the hour bucket matching arrival is found — a single un-paginated call saw only
// the next ~24h, silently No-data-ing any stop arriving further out.
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

    // forecast/hours returns 24 buckets per page; 10 pages ≈ the 240h (10-day) horizon the client gates to.
    private const int MaxForecastPages = 10;

    public GoogleWeatherService(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts, IMemoryCache cache, ILogger<GoogleWeatherService> log)
    { _http = http; _opts = opts.Value; _cache = cache; _log = log; }

    public async Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
    {
        var result = new WeatherReading[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var key = CacheKey(p, kind);
            if (_cache.TryGetValue(key, out WeatherReading? hit) && hit is not null) { result[i] = hit with { StopId = p.StopId }; continue; }
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
            var client = _http.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Now is a single call (8s). OnArrival may walk up to MaxForecastPages sequential requests, so give
            // the whole walk a larger shared budget (~10 pages at the observed ~1.6s/page + margin) rather than
            // starving a far-horizon stop into a false No-data — while still bounding total wait per stop.
            timeoutCts.CancelAfter(kind == WeatherReadingKind.Now ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(20));

            if (kind == WeatherReadingKind.Now)
            {
                var nowUrl = $"https://weather.googleapis.com/v1/currentConditions:lookup?location.latitude={lat}&location.longitude={lng}&unitsSystem=METRIC&languageCode=th";
                using var nowDoc = await GetJsonAsync(client, nowUrl, timeoutCts.Token);
                return ParseReading(nowDoc.RootElement, p.StopId);
            }

            // OnArrival: forecast/hours returns only 24 buckets per response and paginates the rest of
            // the 240h horizon behind nextPageToken. Follow the pages until the bucket matching the
            // arrival hour is found — a single un-paginated call only saw the next ~24h, so any stop
            // arriving further out silently degraded to No-data.
            if (p.ArrivalLocal is not { } av) return NoData(p.StopId); // a null arrival can never match an hour bucket — don't page
            var arrivalHour = new DateTime(av.Year, av.Month, av.Day, av.Hour, 0, 0);
            string? pageToken = null;
            for (var page = 0; page < MaxForecastPages; page++)
            {
                var url = $"https://weather.googleapis.com/v1/forecast/hours:lookup?location.latitude={lat}&location.longitude={lng}&hours=240&unitsSystem=METRIC&languageCode=th";
                if (pageToken is not null) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
                using var doc = await GetJsonAsync(client, url, timeoutCts.Token);
                var el = PickHour(doc.RootElement, arrivalHour);
                if (el is not null) return ParseReading(el.Value, p.StopId);
                // Buckets are chronological. Once this page's last bucket has reached the arrival hour
                // without an exact match, the arrival is in the past or a gap — no later page can hold
                // it, so stop rather than burning the rest of the horizon on a permanently No-data stop.
                if (LastBucketReaches(doc.RootElement, arrivalHour)) break;
                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var tok) ? tok.GetString() : null;
                if (string.IsNullOrEmpty(pageToken)) break;
            }
            return NoData(p.StopId);
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested(); // honour real caller cancellation; degrade only on Google failures/timeouts
            _log.LogWarning(ex, "Weather lookup failed for {StopId}; returning No-data.", p.StopId);
            return NoData(p.StopId);
        }
    }

    public async Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
    {
        var want = Math.Clamp(hours, 1, 240);
        var cacheKey = $"wx:Hourly:{point.Lat:F5},{point.Lng:F5}:{want}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<HourlyReading>? cached) && cached is not null) return cached;

        var lat = point.Lat.ToString(CultureInfo.InvariantCulture);
        var lng = point.Lng.ToString(CultureInfo.InvariantCulture);
        var client = _http.CreateClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
        var result = new List<HourlyReading>();
        try
        {
            string? pageToken = null;
            for (var page = 0; page < MaxForecastPages && result.Count < want; page++)
            {
                var url = $"https://weather.googleapis.com/v1/forecast/hours:lookup?location.latitude={lat}&location.longitude={lng}&hours=240&unitsSystem=METRIC&languageCode=th";
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

    private async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
        req.Headers.Add("X-Goog-Maps-Solution-ID", "gmp_git_agentskills_v1");
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    // OnArrival: pick the forecast hour whose location-local displayDateTime matches the arrival
    // wall-clock hour (arrival is day.date + scheduled HH:MM). No match (out of the 240h horizon
    // that slipped past the client gate, or a gap) => null => No-data.
    private static JsonElement? PickHour(JsonElement root, DateTime? arrivalHour)
    {
        if (arrivalHour is not { } a || !root.TryGetProperty("forecastHours", out var hours)) return null;
        foreach (var h in hours.EnumerateArray())
            if (BucketHour(h) == a) return h;
        return null;
    }

    // True once the last bucket on the page is at/after the arrival hour: buckets are chronological, so
    // a miss up to here means the arrival hour is in the past or a gap and no later page can hold it.
    private static bool LastBucketReaches(JsonElement root, DateTime arrivalHour)
    {
        if (!root.TryGetProperty("forecastHours", out var hours)) return false;
        DateTime? last = null;
        foreach (var h in hours.EnumerateArray())
            if (BucketHour(h) is { } b) last = b;
        return last is { } l && l >= arrivalHour;
    }

    // The location-local wall-clock hour a forecast bucket represents (displayDateTime), truncated to the hour.
    private static DateTime? BucketHour(JsonElement h)
    {
        if (!h.TryGetProperty("displayDateTime", out var dt)) return null;
        var y = dt.TryGetProperty("year", out var yy) ? yy.GetInt32() : 0;
        var mo = dt.TryGetProperty("month", out var mm) ? mm.GetInt32() : 0;
        var d = dt.TryGetProperty("day", out var dd) ? dd.GetInt32() : 0;
        var hr = dt.TryGetProperty("hours", out var hh) ? hh.GetInt32() : -1;
        return y > 0 && mo > 0 && d > 0 && hr >= 0 ? new DateTime(y, mo, d, hr, 0, 0) : null;
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
        int? uv = el.TryGetProperty("uvIndex", out var uvi) && uvi.ValueKind == JsonValueKind.Number
            ? uvi.GetInt32() : null;
        double? feels = el.TryGetProperty("feelsLikeTemperature", out var fl)
            && fl.TryGetProperty("degrees", out var fd) ? fd.GetDouble() : null;
        var hasData = type is not null || temp is not null;
        return new WeatherReading(stopId, hasData, type, icon, temp, rain, desc, uv, feels);
    }

    private static WeatherReading NoData(string stopId) => new(stopId, false, null, null, null, null, null, null, null);

    private static string CacheKey(WeatherPoint p, WeatherReadingKind kind)
    {
        var baseKey = $"wx:{kind}:{p.Lat:F5},{p.Lng:F5}"; // ~5 dp, matching GoogleRouteService's leg key
        return kind == WeatherReadingKind.OnArrival && p.ArrivalLocal is { } a ? $"{baseKey}:{a:yyyyMMddHH}" : baseKey;
    }
}
