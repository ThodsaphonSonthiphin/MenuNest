using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>One calendar day of a Trip's itinerary; owns ordered <see cref="Stop"/>s.</summary>
public sealed class ItineraryDay : Entity
{
    public Guid TripId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly DayStartTime { get; private set; }
    public bool UseCurrentTimeAsStart { get; private set; }

    private ItineraryDay() { } // EF

    public static ItineraryDay Create(Guid tripId, DateOnly date, TimeOnly? dayStartTime = null)
    {
        if (tripId == Guid.Empty) throw new DomainException("TripId is required.");
        return new ItineraryDay { TripId = tripId, Date = date, DayStartTime = dayStartTime ?? new TimeOnly(9, 0) };
    }

    public void SetStartTime(TimeOnly start)
    {
        DayStartTime = start;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetUseCurrentTimeAsStart(bool useCurrentTime)
    {
        UseCurrentTimeAsStart = useCurrentTime;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDate(DateOnly date)
    {
        Date = date;
        UpdatedAt = DateTime.UtcNow;
    }
}
