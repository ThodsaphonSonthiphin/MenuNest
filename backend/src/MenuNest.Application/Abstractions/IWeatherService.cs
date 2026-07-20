using MenuNest.Domain.Enums;
namespace MenuNest.Application.Abstractions;

public sealed record WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal);
public sealed record WeatherReading(
    string StopId, bool HasData, string? ConditionType, string? IconBaseUri,
    double? TempC, int? RainPct, string? Description,
    int? UvIndex, double? FeelsLikeC);

public interface IWeatherService
{
    /// <summary>Resolve a weather reading of the given kind for each point. Any failure degrades a
    /// single point to HasData=false rather than throwing (ADR-030).</summary>
    Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct);
}
