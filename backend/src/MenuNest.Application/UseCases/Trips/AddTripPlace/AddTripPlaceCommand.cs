using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;

/// <summary>
/// The five trailing members are ADR-156's copy-at-add-time payload and are ALL defaulted:
/// AddTripPlaceCommand has 10 positional construction sites (2 production, 8 test) and none
/// of them may break. Construct the new members by name.
/// </summary>
public sealed record AddTripPlaceCommand(
    Guid TripId, string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson,
    Guid? OriginTripPlaceId = null,
    string? Notes = null,
    IReadOnlyList<ReviewLinkDto>? ReviewLinks = null,
    IReadOnlyList<BestTimeWindowDto>? BestTimeWindows = null,
    IReadOnlyList<SeasonPeriodDto>? SeasonPeriods = null)
    : ICommand<TripPlaceDto>;
