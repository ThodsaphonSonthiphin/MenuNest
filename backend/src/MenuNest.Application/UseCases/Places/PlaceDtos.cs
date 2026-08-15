using MenuNest.Application.UseCases.Trips; // SeasonPeriodDto
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Places;

/// <summary>A Trip that contains a discovered Place (for the "อยู่ในทริป: …" line).</summary>
/// <param name="TripPlaceId">
/// ADR-166: the row id inside THIS Trip, which is what makes a delete addressable —
/// a Discover pin is a read-time group over N rows and carries no id of its own.
/// </param>
/// <param name="ScheduledStopCount">
/// ADR-168: how many Stops in this Trip reference that row, so the delete confirmation can
/// name the number instead of guessing. Free — the Stops table is already read for Visited.
/// </param>
public sealed record PlaceTripRefDto(Guid TripId, string TripName, Guid TripPlaceId, int ScheduledStopCount);

/// <summary>
/// One distinct saved Place surfaced in Discover (ไปไหนดี), deduped by GooglePlaceId
/// across all the user's Trips (ADR-100). Carries raw signal data so the client computes
/// open-now / season / best-time itself. Avoids the banned "Location" term.
/// </summary>
/// <param name="OriginTripPlaceId">
/// ADR-156: the group's ROOT TripPlace id, already flattened (rep.OriginTripPlaceId ?? rep.Id).
/// A client passes this straight back as AddTripPlaceCommand.OriginTripPlaceId, which is what
/// makes a chain of copies structurally impossible — the write path performs no lookup.
/// </param>
public sealed record DiscoverPlaceDto(
    string Key,
    string? GooglePlaceId,
    string Name,
    double Lat,
    double Lng,
    string? Address,
    PlaceCategory Category,
    int? PriceLevel,
    string? PhotoUrl,
    string? OpeningHoursJson,
    IReadOnlyList<BestTimeWindowDto> BestTimeWindows,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods,
    bool Visited,
    IReadOnlyList<PlaceTripRefDto> Trips,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    string? Notes,
    Guid OriginTripPlaceId);
