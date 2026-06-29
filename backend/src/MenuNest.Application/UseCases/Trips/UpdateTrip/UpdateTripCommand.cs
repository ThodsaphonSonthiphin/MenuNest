using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

public sealed record UpdateTripCommand(
    Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode)
    : ICommand<TripDto>;
