using Mediator;

namespace MenuNest.Application.UseCases.Trips.SetTripDaily;

public sealed record SetTripDailyCommand(Guid TripId, bool IsDaily) : ICommand<TripDto>;