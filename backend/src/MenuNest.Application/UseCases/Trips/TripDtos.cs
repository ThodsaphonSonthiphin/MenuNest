using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips;

public sealed record TripDto(
    Guid Id, string Name, string? Destination,
    DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode, bool IsDaily);

public sealed record ReviewLinkDto(string Url, string? Label);

public sealed record SeasonPeriodDto(SeasonKind Kind, IReadOnlyList<int> Months, string? Note);

public sealed record BestTimeWindowDto(TimeOnly Start, TimeOnly End, string? Note);

public sealed record ChecklistItemDto(Guid Id, string Name);

public sealed record StopChecklistEntryDto(Guid Id, Guid ChecklistItemId, string Name, bool IsChecked);

public sealed record TripPlaceDto(
    Guid Id, Guid TripId, string? GooglePlaceId, string Name,
    double Lat, double Lng, string? Address, PlaceCategory Category,
    int? PriceLevel, string? PhotoUrl,
    string? OpeningHoursJson, string? FeeNote, string? Notes,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    bool HasProfile,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods,
    IReadOnlyList<BestTimeWindowDto> BestTimeWindows);

public sealed record LegDto(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source);

public sealed record StopDto(
    Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes,
    TravelMode TravelModeToReach, LegDto? LegToReach, bool IsVisited,
    IReadOnlyList<StopChecklistEntryDto> Checklist);

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

/// <summary>Wire shape of a weather re-timing target (mirrors RetimeTarget).
/// Kind ∈ hour | coolestDaytime | coolestNighttime.</summary>
public sealed record RetimeTargetDto(string Kind, DateTime? LocalDateTime, int? WindowHours);

/// <summary>One Weather window: a dated, contiguous run of forecast hours in which every selected
/// signal stayed below its threshold (menunest-218). Worst* are null for unselected signals.</summary>
public sealed record WeatherWindowDto(
    DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours,
    int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex);

/// <summary>Why no window came back (menunest-225). ClosestValue is the lowest value of the blocking
/// signal among the hours it blocked; blocking is &gt;=, so only a threshold ABOVE it unblocks one.</summary>
public sealed record WeatherWindowMissDto(
    WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal,
    double? ClosestValue, double? Threshold,
    int HoursExamined, int HoursBlocked, int LongestRunHours);

/// <summary>Miss is null exactly when Windows is non-empty. SearchedFrom/SearchedThrough name the
/// band dates actually examined; HorizonTruncated says ToDate ran past the forecast.</summary>
public sealed record WeatherWindowResultDto(
    IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss,
    bool HorizonTruncated, DateOnly SearchedFrom, DateOnly SearchedThrough);