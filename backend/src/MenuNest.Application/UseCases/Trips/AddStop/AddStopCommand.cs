using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.AddStop;
public sealed record AddStopCommand(
    Guid TripId, Guid DayId, Guid TripPlaceId, int DwellMinutes, TravelMode TravelModeToReach)
    : ICommand<StopDto>;
