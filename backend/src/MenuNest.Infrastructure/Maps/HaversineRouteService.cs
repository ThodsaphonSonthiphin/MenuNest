using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
namespace MenuNest.Infrastructure.Maps;

/// <summary>Key-free fallback: great-circle distance × 1.3 road factor ÷ mode speed.</summary>
public sealed class HaversineRouteService : IRouteService
{
    private const double RoadFactor = 1.3;
    private static double SpeedMps(TravelMode m) => m switch
    { TravelMode.Walk => 1.4, TravelMode.Transit => 8.3, _ => 11.1 }; // ~5 / 30 / 40 km/h

    public Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> pts, TravelMode mode, CancellationToken ct)
    {
        var legs = new List<LegTime>();
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var meters = Haversine(pts[i], pts[i + 1]) * RoadFactor;
            legs.Add(new LegTime((int)Math.Round(meters / SpeedMps(mode)), (int)Math.Round(meters)));
        }
        return Task.FromResult<IReadOnlyList<LegTime>>(legs);
    }

    private static double Haversine(RoutePoint a, RoutePoint b)
    {
        const double R = 6_371_000;
        double dLat = Deg(b.Lat - a.Lat), dLng = Deg(b.Lng - a.Lng);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(Deg(a.Lat)) * Math.Cos(Deg(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
    private static double Deg(double d) => d * Math.PI / 180.0;
}
