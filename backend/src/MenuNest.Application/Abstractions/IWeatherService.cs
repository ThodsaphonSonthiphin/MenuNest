using MenuNest.Domain.Enums;
namespace MenuNest.Application.Abstractions;

public sealed record WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal);
public sealed record WeatherReading(
    string StopId, bool HasData, string? ConditionType, string? IconBaseUri,
    double? TempC, int? RainPct, string? Description,
    int? UvIndex, double? FeelsLikeC);

/// <summary>One hour of a location's forecast. DisplayLocal is the Google bucket's local wall-clock hour;
/// IsDaytime is Google's per-hour flag (sunrise-inclusive → sunset-exclusive).</summary>
public sealed record HourlyReading(
    DateTime DisplayLocal, bool IsDaytime,
    double? TempC, double? FeelsLikeC,
    string? ConditionType, string? IconBaseUri,
    int? RainPct, int? UvIndex);

public interface IWeatherService
{
    /// <summary>Resolve a weather reading of the given kind for each point. Any failure degrades a
    /// single point to HasData=false rather than throwing (ADR-030).</summary>
    Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct);

    /// <summary>Ordered hourly forecast for a single point, up to min(hours, 240). Reuses the same
    /// forecast/hours:lookup walk as On-arrival (no new billing SKU). Degrades to an empty list, never throws (ADR-030).</summary>
    Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct);
}
