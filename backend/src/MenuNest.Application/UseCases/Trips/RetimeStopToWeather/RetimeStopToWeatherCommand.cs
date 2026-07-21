using Mediator;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToWeather;

/// <summary>MCP re-timing: resolve <paramref name="Target"/> to a concrete (date, hour), compute the
/// server-side offset from INTER-STOP legs only (no approach leg — an AI caller has no live location),
/// then delegate the write to <c>RetimeStopToHourCommand</c>. Returns its <see cref="RetimeResultDto"/>.</summary>
public sealed record RetimeStopToWeatherCommand(
    Guid TripId, Guid DayId, Guid StopId, RetimeTarget Target)
    : ICommand<RetimeResultDto>;
