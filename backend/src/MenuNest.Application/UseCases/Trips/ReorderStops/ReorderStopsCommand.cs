using Mediator;
namespace MenuNest.Application.UseCases.Trips.ReorderStops;
public sealed record ReorderStopsCommand(
    Guid TripId, Guid DayId, IReadOnlyList<Guid> OrderedStopIds) : ICommand<Unit>;
