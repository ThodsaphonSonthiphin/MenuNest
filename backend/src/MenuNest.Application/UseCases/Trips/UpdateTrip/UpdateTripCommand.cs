using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

/// <param name="AllowStopLoss">
/// Explicit confirmation that the caller accepts destroying the Stops on the days a lower
/// <paramref name="DayCount"/> removes (ADR-140). Trailing and defaulted on purpose: every
/// existing construction site keeps compiling, and the unsafe value is the one you have to
/// ask for. The SPA sets it only after the user confirms (ADR-138).
/// </param>
public sealed record UpdateTripCommand(
    Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode,
    bool AllowStopLoss = false)
    : ICommand<TripDto>;
