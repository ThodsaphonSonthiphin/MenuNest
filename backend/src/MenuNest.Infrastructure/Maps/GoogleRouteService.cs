using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Routes API: POST https://routes.googleapis.com/distanceMatrix/v2:computeRouteMatrix
// Headers: X-Goog-Api-Key, X-Goog-FieldMask
// Duration field in response is a Duration string, e.g. "120s" — parsed via TrimEnd('s').
// travelMode: DRIVE | WALK | TRANSIT  (Routes API GA, not legacy Distance Matrix API)
// Cache TTL: 12 h per leg — well within the 30-day ToS caching limit.
// Data is never used for ML training. No prohibited territory routing.
namespace MenuNest.Infrastructure.Maps;

public sealed class GoogleRouteService : IRouteService
{
    private readonly IHttpClientFactory _http;
    private readonly GoogleMapsOptions _opts;
    private readonly IMemoryCache _cache;
    private readonly HaversineRouteService _fallback = new();
    private readonly ILogger<GoogleRouteService> _log;

    public GoogleRouteService(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts, IMemoryCache cache, ILogger<GoogleRouteService> log)
    { _http = http; _opts = opts.Value; _cache = cache; _log = log; }

    public async Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> pts, TravelMode mode, CancellationToken ct)
    {
        if (pts.Count < 2) return Array.Empty<LegTime>();
        var result = new LegTime[pts.Count - 1];
        var misses = new List<int>();
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            if (_cache.TryGetValue(Key(pts[i], pts[i + 1], mode), out LegTime? hit) && hit is not null) result[i] = hit;
            else misses.Add(i);
        }
        if (misses.Count == 0) return result;

        try
        {
            // computeRouteMatrix over the missing (origin,dest) pairs.
            foreach (var i in misses)
            {
                var leg = await ComputeOneAsync(pts[i], pts[i + 1], mode, ct);
                _cache.Set(Key(pts[i], pts[i + 1], mode), leg, TimeSpan.FromHours(12));
                result[i] = leg;
            }
            return result;
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested(); // honour real caller cancellation; only fall back on Google failures/timeouts
            _log.LogWarning(ex, "Routes API failed; using Haversine fallback.");
            var fb = await _fallback.GetLegTimesAsync(pts, mode, ct);
            for (var i = 0; i < result.Length; i++) result[i] ??= fb[i];
            return result;
        }
    }

    private async Task<LegTime> ComputeOneAsync(RoutePoint o, RoutePoint d, TravelMode mode, CancellationToken ct)
    {
        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/distanceMatrix/v2:computeRouteMatrix");
        req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
        req.Headers.Add("X-Goog-FieldMask", "originIndex,destinationIndex,duration,distanceMeters,condition");
        req.Headers.Add("X-Goog-Maps-Solution-ID", "gmp_git_agentskills_v1");
        req.Content = JsonContent.Create(new
        {
            origins = new[] { Wp(o) },
            destinations = new[] { Wp(d) },
            travelMode = mode switch { TravelMode.Walk => "WALK", TravelMode.Transit => "TRANSIT", _ => "DRIVE" },
        });
        // Bound each Google call so one slow upstream cannot stall the whole itinerary response.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        var resp = await client.SendAsync(req, timeoutCts.Token);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeoutCts.Token));
        var el = doc.RootElement.EnumerateArray().First();
        var seconds = ParseDuration(el.GetProperty("duration").GetString());
        var meters = el.TryGetProperty("distanceMeters", out var m) ? m.GetInt32() : 0;
        return new LegTime(seconds, meters, null, RouteSource.Routed);

        static object Wp(RoutePoint p) => new { waypoint = new { location = new { latLng = new { latitude = p.Lat, longitude = p.Lng } } } };
        static int ParseDuration(string? s) =>
            s is not null && double.TryParse(s.TrimEnd('s'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? (int)Math.Round(v) : 0;
    }

    private static string Key(RoutePoint o, RoutePoint d, TravelMode mode)
        => $"leg:{o.Lat:F5},{o.Lng:F5}->{d.Lat:F5},{d.Lng:F5}:{mode}";
}
