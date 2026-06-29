using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed record CreateTripCommand(
    string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode)
    : ICommand<TripDto>;
