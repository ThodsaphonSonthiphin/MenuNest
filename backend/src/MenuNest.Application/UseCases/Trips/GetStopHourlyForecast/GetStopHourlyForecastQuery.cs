using Mediator;

namespace MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;

/// <summary>Owner-scoped hourly forecast for a saved stop's location (MCP). Resolves the stop's
/// <see cref="Domain.Entities.TripPlace"/> coords, then reads up to <paramref name="Hours"/> (≤240)
/// of forecast via <see cref="Abstractions.IWeatherService.GetHourlyAsync"/>.</summary>
public sealed record GetStopHourlyForecastQuery(Guid TripId, Guid StopId, int Hours)
    : IQuery<IReadOnlyList<HourlyReadingDto>>;
