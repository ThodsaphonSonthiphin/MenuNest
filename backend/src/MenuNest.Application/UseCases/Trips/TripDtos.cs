using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips;

public sealed record TripDto(
    Guid Id, string Name, string? Destination,
    DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode);

public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl, TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    string? OpeningHoursJson, string? FeeNote, string? Notes);

public sealed record LegDto(int Seconds, int Meters);

public sealed record StopDto(
    Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes,
    TravelMode TravelModeToReach, LegDto? LegToReach);

public sealed record ItineraryDayDto(
    Guid Id, DateOnly Date, TimeOnly DayStartTime, IReadOnlyList<StopDto> Stops);

public sealed record ResolvedPlaceDto(
    string? GooglePlaceId, string Name, double Lat, double Lng, string? Address,
    PlaceCategory Category, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson);
