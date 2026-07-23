using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A planned journey owned by one <see cref="User"/> (user-scoped — ADR-005).
/// Holds a per-trip pool of <see cref="TripPlace"/> and a day-by-day itinerary
/// (<see cref="ItineraryDay"/> → <see cref="Stop"/>). Expenses are Phase 2 (ADR-009).
/// </summary>
public sealed class Trip : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Destination { get; private set; }
    public DateOnly StartDate { get; private set; }
    public int DayCount { get; private set; }
    public TravelMode DefaultTravelMode { get; private set; }
    public bool IsDaily { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Trip() { } // EF

    public static Trip Create(
        Guid userId, string name, DateOnly startDate, int dayCount,
        TravelMode defaultTravelMode, string? destination = null, bool isDaily = false)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Trip name is required.");
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");
        if (isDaily && dayCount != 1) throw new DomainException("A daily trip must be a single day.");

        return new Trip
        {
            UserId = userId,
            Name = name.Trim(),
            Destination = destination?.Trim(),
            StartDate = startDate,
            DayCount = dayCount,
            DefaultTravelMode = defaultTravelMode,
            IsDaily = isDaily,
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Trip name is required.");
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, string? destination, TravelMode defaultTravelMode)
    {
        Rename(name);
        Destination = destination?.Trim();
        DefaultTravelMode = defaultTravelMode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reschedule(DateOnly startDate, int dayCount)
    {
        if (dayCount < 1) throw new DomainException("A trip must have at least one day.");
        if (IsDaily && dayCount > 1)
            throw new DomainException("A daily trip must stay a single day. Turn off daily mode first.");
        StartDate = startDate;
        DayCount = dayCount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDaily(bool isDaily)
    {
        if (isDaily && DayCount != 1)
            throw new DomainException("A daily trip must be a single day.");
        IsDaily = isDaily;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
