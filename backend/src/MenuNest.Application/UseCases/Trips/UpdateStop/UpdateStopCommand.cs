using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateStop;
public sealed record UpdateStopCommand(
    Guid TripId, Guid StopId, int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited = null)
    : ICommand<Unit>;
