using Mediator;
namespace MenuNest.Application.UseCases.Trips.SetDayStartTime;
public sealed record SetDayStartTimeCommand(Guid TripId, Guid DayId, TimeOnly StartTime) : ICommand<Unit>;
