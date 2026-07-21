using Mediator;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

/// <summary>Apply core (web + shared): re-time the anchor Day so the anchor Stop arrives at a
/// client-resolved hour. Shifts the Day start (always) and, for a cross-day target, the whole
/// Trip.StartDate + realign (ADR-108/109); pins the Day by turning off current-time-start (ADR-110).</summary>
public sealed record RetimeStopToHourCommand(
    Guid TripId, Guid DayId, Guid StopId, TimeOnly NewDayStartTime, DateOnly NewAnchorDate)
    : ICommand<RetimeResultDto>;
