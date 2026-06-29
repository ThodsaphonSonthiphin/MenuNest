using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A saved candidate location in a Trip's pool, anchored to a Google
/// <c>place_id</c> when resolved (the only Maps datum stored long-term — ADR-007).
/// Other fields are a cached snapshot from a live Places API call, never scraped.
/// </summary>
public sealed class TripPlace : Entity
{
    public Guid TripId { get; private set; }
    public string? GooglePlaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public string? Address { get; private set; }
    public PlaceCategory Category { get; private set; }
    public int? PriceLevel { get; private set; }
    public string? PhotoUrl { get; private set; }
    public TimeOnly? BestTimeStart { get; private set; }
    public TimeOnly? BestTimeEnd { get; private set; }
    public string? OpeningHoursJson { get; private set; }
    public string? FeeNote { get; private set; }
    public string? Notes { get; private set; }

    private TripPlace() { } // EF

    public static TripPlace Create(
        Guid tripId, string name, double lat, double lng, PlaceCategory category,
        string? googlePlaceId = null, string? address = null, int? priceLevel = null,
        string? photoUrl = null, string? openingHoursJson = null)
    {
        if (tripId == Guid.Empty) throw new DomainException("TripId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Place name is required.");
        if (priceLevel is < 0 or > 4) throw new DomainException("Price level must be 0–4.");

        return new TripPlace
        {
            TripId = tripId,
            Name = name.Trim(),
            Lat = lat,
            Lng = lng,
            Category = category,
            GooglePlaceId = googlePlaceId,
            Address = address?.Trim(),
            PriceLevel = priceLevel,
            PhotoUrl = photoUrl,
            OpeningHoursJson = openingHoursJson,
        };
    }

    public void UpdateDetails(string name, PlaceCategory category, string? address, string? feeNote, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Place name is required.");
        Name = name.Trim();
        Category = category;
        Address = address?.Trim();
        FeeNote = feeNote?.Trim();
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetBestTime(TimeOnly? start, TimeOnly? end)
    {
        if (start is not null && end is not null && end <= start)
            throw new DomainException("Best-time end must be after start.");
        BestTimeStart = start;
        BestTimeEnd = end;
        UpdatedAt = DateTime.UtcNow;
    }
}
