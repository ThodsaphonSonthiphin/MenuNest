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
            points.Select(p => new WeatherReading(p.StopId, false, null, null, null, null, null, null, null)).ToList());

    public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<HourlyReading>>(Array.Empty<HourlyReading>());
}
