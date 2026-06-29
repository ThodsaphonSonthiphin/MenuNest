using MenuNest.Domain.Enums;
namespace MenuNest.Application.Abstractions;

public sealed record RoutePoint(double Lat, double Lng);
public sealed record LegTime(int Seconds, int Meters);

public interface IRouteService
{
    /// <summary>Travel time/distance for each consecutive leg of an ordered point list.</summary>
    Task<IReadOnlyList<LegTime>> GetLegTimesAsync(IReadOnlyList<RoutePoint> orderedPoints, TravelMode mode, CancellationToken ct);
}
