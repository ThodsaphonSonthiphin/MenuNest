using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The effective threshold for each signal. A null threshold means that signal never blocks
/// an hour — it is unselected, or selected with its stored Weather-alert threshold switched off.
/// An hour is BLOCKED when its value is AT OR ABOVE the threshold: the web app's rule
/// (isRainy: rainPct >= 60; UserSettings: "warn at UV >= N").</summary>
public sealed record WeatherThresholds(
    IReadOnlyCollection<WeatherSignal> Selected, int? RainPct, double? FeelsLikeC, int? UvIndex)
{
    public double? Of(WeatherSignal signal) => signal switch
    {
        WeatherSignal.Rain => RainPct,
        WeatherSignal.Heat => FeelsLikeC,
        WeatherSignal.Sun => UvIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };
}

/// <summary>Server twin of <c>effectiveThreshold</c> and <c>weatherAlertBadges</c> in
/// frontend/src/pages/trips/lib/weather.ts, and of frontend/src/pages/settings/weatherAlertControl.ts.
/// Change the rule in all three places or none.</summary>
public static class WeatherWindowThresholds
{
    public const int DefaultRainPct = 60;    // RAIN_TINT_THRESHOLD
    public const int DefaultFeelsLikeC = 40; // FEELS_WARN_DEFAULT
    public const int DefaultUvIndex = 6;     // UV_WARN_DEFAULT

    /// <summary>Explicit per-call value > stored Weather-alert threshold > built-in default
    /// (menunest-219). Only selected signals get a threshold. Rain has no stored threshold.</summary>
    public static WeatherThresholds Resolve(
        IReadOnlyCollection<WeatherSignal> signals,
        int? explicitRainPct, double? explicitFeelsLikeC, int? explicitUvIndex,
        int? storedUvWarn, int? storedFeelsWarn)
    {
        var selected = signals.Distinct().ToList();
        return new WeatherThresholds(
            selected,
            RainPct: selected.Contains(WeatherSignal.Rain)
                ? explicitRainPct ?? DefaultRainPct
                : null,
            FeelsLikeC: selected.Contains(WeatherSignal.Heat)
                ? explicitFeelsLikeC ?? (double?)Stored(storedFeelsWarn, DefaultFeelsLikeC)
                : null,
            UvIndex: selected.Contains(WeatherSignal.Sun)
                ? explicitUvIndex ?? Stored(storedUvWarn, DefaultUvIndex)
                : null);
    }

    /// <summary>null → the default · 0 → off (null: never blocks) · N → N.</summary>
    private static int? Stored(int? stored, int builtInDefault) => stored switch
    {
        null => builtInDefault,
        0 => null,
        var n => n,
    };
}
