using Mediator;
namespace MenuNest.Application.UseCases.Trips.RemoveStop;
public sealed record RemoveStopCommand(Guid TripId, Guid StopId) : ICommand<Unit>;
