using Mediator;

namespace MenuNest.Application.UseCases.Trips.DeleteTrip;

public sealed record DeleteTripCommand(Guid TripId) : ICommand<Unit>;
