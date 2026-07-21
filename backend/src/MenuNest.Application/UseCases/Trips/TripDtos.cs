using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips;

public sealed record TripDto(
    Guid Id, string Name, string? Destination,
    DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode);

public sealed record ReviewLinkDto(string Url, string? Label);

public sealed record SeasonPeriodDto(SeasonKind Kind, IReadOnlyList<int> Months, string? Note);

public sealed record ChecklistItemDto(Guid Id, string Name);

public sealed record PlaceChecklistEntryDto(Guid Id, Guid ChecklistItemId, string Name, bool IsChecked);

public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<PlaceChecklistEntryDto> Checklist,
    bool HasProfile,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods);

public sealed record LegDto(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source);

public sealed record StopDto(
    Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes,
    TravelMode TravelModeToReach, LegDto? LegToReach, bool IsVisited);

public sealed record ItineraryDayDto(
    Guid Id, DateOnly Date, TimeOnly DayStartTime, bool UseCurrentTimeAsStart, IReadOnlyList<StopDto> Stops);

public sealed record ResolvedPlaceDto(
    string? GooglePlaceId, string Name, double Lat, double Lng, string? Address,
    PlaceCategory Category, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson);

public sealed record WeatherPointDto(string StopId, double Lat, double Lng, DateTime? ArrivalIso);
public sealed record WeatherReadingDto(
    string StopId, bool HasData, string? ConditionType, string? IconBaseUri,
    double? TempC, int? RainPct, string? Description,
    int? UvIndex, double? FeelsLikeC);

public sealed record HourlyReadingDto(
    DateTime DisplayLocal, bool IsDaytime,
    double? TempC, double? FeelsLikeC,
    string? ConditionType, string? IconBaseUri,
    int? RainPct, int? UvIndex);

public sealed record RetimeResultDto(
    bool MovedTrip, DateOnly TripStartBefore, DateOnly TripStartAfter,
    DateOnly AnchorDate, TimeOnly NewDayStartTime);