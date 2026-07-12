using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One scheduled visit in an <see cref="ItineraryDay"/>: a reference to a
/// <see cref="TripPlace"/> plus a dwell duration and the travel mode used on the
/// leg arriving from the previous stop (<see cref="Sequence"/> 0 has no leg).
/// Arrival/leave times are derived, never stored (ADR-008).
/// </summary>
public sealed class Stop : Entity
{
    public Guid ItineraryDayId { get; private set; }
    public Guid TripPlaceId { get; private set; }
    public int Sequence { get; private set; }
    public int DwellMinutes { get; private set; }
    public TravelMode TravelModeToReach { get; private set; }
    public string? Notes { get; private set; }
    public bool IsVisited { get; private set; }

    private Stop() { } // EF

    public static Stop Create(Guid itineraryDayId, Guid tripPlaceId, int sequence, int dwellMinutes, TravelMode travelModeToReach)
    {
        if (itineraryDayId == Guid.Empty) throw new DomainException("ItineraryDayId is required.");
        if (tripPlaceId == Guid.Empty) throw new DomainException("TripPlaceId is required.");
        if (sequence < 0) throw new DomainException("Sequence cannot be negative.");
        if (dwellMinutes <= 0) throw new DomainException("Dwell minutes must be positive.");

        return new Stop
        {
            ItineraryDayId = itineraryDayId,
            TripPlaceId = tripPlaceId,
            Sequence = sequence,
            DwellMinutes = dwellMinutes,
            TravelModeToReach = travelModeToReach,
        };
    }

    public void SetSequence(int sequence)
    {
        if (sequence < 0) throw new DomainException("Sequence cannot be negative.");
        Sequence = sequence;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDwell(int dwellMinutes)
    {
        if (dwellMinutes <= 0) throw new DomainException("Dwell minutes must be positive.");
        DwellMinutes = dwellMinutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTravelMode(TravelMode mode)
    {
        TravelModeToReach = mode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetVisited(bool value)
    {
        IsVisited = value;
        UpdatedAt = DateTime.UtcNow;
    }
}
