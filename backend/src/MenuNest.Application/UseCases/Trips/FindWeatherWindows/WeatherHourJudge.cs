using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The verdict on one forecast hour. Neither good nor blocked = unjudgeable: a signal that
/// gates the hour had no value, and no signal rejected it.</summary>
public sealed record HourVerdict(bool IsGood, IReadOnlyList<WeatherSignal> Failed)
{
    public bool IsBlocked => Failed.Count > 0;
}

public static class WeatherHourJudge
{
    /// <summary>[fromHour, toHour). When toHour &lt;= fromHour the band wraps past midnight
    /// (e.g. 18→2 for a night market).</summary>
    public static bool InBand(int hour, int fromHour, int toHour)
        => toHour > fromHour
            ? hour >= fromHour && hour < toHour
            : hour >= fromHour || hour < toHour;

    /// <summary>The date whose band this hour belongs to. On a wrapping band the after-midnight tail
    /// belongs to the band opened the evening before (spec §5; ADR-118 crosses midnight the same way).</summary>
    public static DateOnly BandDate(DateTime displayLocal, int fromHour, int toHour)
    {
        var date = DateOnly.FromDateTime(displayLocal);
        return toHour <= fromHour && displayLocal.Hour < toHour ? date.AddDays(-1) : date;
    }

    public static double? ValueOf(HourlyReading hour, WeatherSignal signal) => signal switch
    {
        WeatherSignal.Rain => hour.RainPct,
        WeatherSignal.Heat => hour.FeelsLikeC,
        WeatherSignal.Sun => hour.UvIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    /// <summary>Blocked when any gating signal is AT OR ABOVE its threshold. Only signals with a
    /// non-null threshold gate the hour.</summary>
    public static HourVerdict Judge(HourlyReading hour, WeatherThresholds thresholds)
    {
        var failed = new List<WeatherSignal>(3);
        var missing = false;
        foreach (var signal in Enum.GetValues<WeatherSignal>())
        {
            if (thresholds.Of(signal) is not { } limit) continue;
            if (ValueOf(hour, signal) is not { } value) { missing = true; continue; }
            if (value >= limit) failed.Add(signal);
        }
        return new HourVerdict(IsGood: failed.Count == 0 && !missing, Failed: failed);
    }
}
