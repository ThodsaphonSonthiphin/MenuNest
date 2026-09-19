using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>Find the Weather windows at a bare location (issue #153). The first backend reader of
/// the User's Weather-alert threshold (menunest-219); reuses the existing forecast/hours walk, so no
/// new billing SKU (ADR-119); persists nothing (ADR-033).</summary>
public sealed class FindWeatherWindowsHandler
    : IQueryHandler<FindWeatherWindowsQuery, WeatherWindowResultDto>
{
    public const int DefaultMinWindowHours = 2; // menunest-224
    private const int HorizonHours = 240;       // ADR-031
    private const int DefaultFromHour = 0;
    private const int DefaultToHour = 24;

    private readonly IWeatherService _weather;
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IClock _clock;
    private readonly IValidator<FindWeatherWindowsQuery> _validator;

    public FindWeatherWindowsHandler(
        IWeatherService weather, IApplicationDbContext db, IUserProvisioner users,
        IClock clock, IValidator<FindWeatherWindowsQuery> validator)
    { _weather = weather; _db = db; _users = users; _clock = clock; _validator = validator; }

    public async ValueTask<WeatherWindowResultDto> Handle(FindWeatherWindowsQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);

        var user = await _users.GetOrProvisionCurrentAsync(ct);
        // Created lazily on first write: a User who never opened /settings has NO row, and that
        // must resolve exactly like null — the built-in defaults.
        var settings = await _db.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        var thresholds = WeatherWindowThresholds.Resolve(
            q.Signals, q.MaxRainPct, q.MaxFeelsLikeC, q.MaxUvIndex,
            settings?.UvWarnThreshold, settings?.FeelsLikeWarnThreshold);

        var fromHour = q.FromHour ?? DefaultFromHour;
        var toHour = q.ToHour ?? DefaultToHour;
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var hours = await _weather.GetHourlyAsync(
            new WeatherPoint("", q.Lat, q.Lng, null), HoursToFetch(q.ToDate, today, fromHour, toHour), ct);

        var cut = WeatherWindowSelection.Cut(
            hours, thresholds, q.FromDate, q.ToDate, fromHour, toHour,
            q.MinWindowHours ?? DefaultMinWindowHours);

        var searchedFrom = cut.FirstExaminedDate ?? q.FromDate ?? today;
        return new WeatherWindowResultDto(
            cut.Windows,
            cut.Miss,
            HorizonTruncated: q.ToDate is { } to && hours.Count > 0
                && hours.Max(h => h.DisplayLocal) < LastBandHour(to, fromHour, toHour),
            SearchedFrom: searchedFrom,
            SearchedThrough: cut.LastExaminedDate ?? searchedFrom);
    }

    /// <summary>ToDate is a LOCAL date at the point and its UTC offset is unknown, so pad one day each
    /// side (plus one for a wrapping band's after-midnight tail). Whole days keep GetHourlyAsync's
    /// hours-keyed cache shared between callers (spec §9).</summary>
    private static int HoursToFetch(DateOnly? toDate, DateOnly today, int fromHour, int toHour)
    {
        if (toDate is not { } to) return HorizonHours;
        var padding = toHour > fromHour ? 2 : 3;
        return Math.Clamp((to.DayNumber - today.DayNumber + padding) * 24, 24, HorizonHours);
    }

    /// <summary>The last hour the band covers for ToDate — the next morning when the band wraps.</summary>
    private static DateTime LastBandHour(DateOnly toDate, int fromHour, int toHour)
    {
        var date = toHour > fromHour ? toDate : toDate.AddDays(1);
        return date.ToDateTime(new TimeOnly(toHour - 1, 0));
    }
}
