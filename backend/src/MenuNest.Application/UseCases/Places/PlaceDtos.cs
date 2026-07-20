using MenuNest.Application.UseCases.Trips; // SeasonPeriodDto
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Places;

/// <summary>A Trip that contains a discovered Place (for the "อยู่ในทริป: …" line).</summary>
public sealed record PlaceTripRefDto(Guid TripId, string TripName);

/// <summary>
/// One distinct saved Place surfaced in Discover (ไปไหนดี), deduped by GooglePlaceId
/// across all the user's Trips (ADR-100). Carries raw signal data so the client computes
/// open-now / season / best-time itself. Avoids the banned "Location" term.
/// </summary>
public sealed record DiscoverPlaceDto(
    string Key,
    string? GooglePlaceId,
    Guid RepresentativeTripPlaceId,
    string Name,
    double Lat,
    double Lng,
    string? Address,
    PlaceCategory Category,
    int? PriceLevel,
    string? PhotoUrl,
    string? OpeningHoursJson,
    TimeOnly? BestTimeStart,
    TimeOnly? BestTimeEnd,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods,
    bool Visited,
    bool HasProfile,
    IReadOnlyList<PlaceTripRefDto> Trips);
