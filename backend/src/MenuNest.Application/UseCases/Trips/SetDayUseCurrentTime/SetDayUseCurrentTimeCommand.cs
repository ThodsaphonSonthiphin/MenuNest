using Mediator;

namespace MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;

public sealed record SetDayUseCurrentTimeCommand(Guid TripId, Guid DayId, bool UseCurrentTime) : ICommand<Unit>;
