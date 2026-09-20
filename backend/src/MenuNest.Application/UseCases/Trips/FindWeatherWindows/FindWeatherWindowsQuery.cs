using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>Find the Weather windows at a bare location (issue #153, menunest-217). Every parameter
/// after Signals is optional; the defaults live in the handler and in WeatherWindowThresholds.</summary>
public sealed record FindWeatherWindowsQuery(
    double Lat,
    double Lng,
    IReadOnlyList<WeatherSignal> Signals,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? FromHour = null,
    int? ToHour = null,
    int? MaxRainPct = null,
    double? MaxFeelsLikeC = null,
    int? MaxUvIndex = null,
    int? MinWindowHours = null) : IQuery<WeatherWindowResultDto>;
