using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToWeather;

/// <summary>Server twin of the client <c>coolestHour</c> (frontend retiming.ts): pick the coolest
/// hour of one half of the day by feels-like temperature.</summary>
public static class WeatherHourSelection
{
    /// <summary>The min <see cref="HourlyReading.FeelsLikeC"/> hour among those with
    /// <see cref="HourlyReading.IsDaytime"/> == <paramref name="daytime"/> and a non-null feels-like,
    /// earliest <see cref="HourlyReading.DisplayLocal"/> on ties; null if that half is empty.</summary>
    public static HourlyReading? CoolestHour(IReadOnlyList<HourlyReading> hours, bool daytime)
    {
        HourlyReading? best = null;
        foreach (var h in hours
            .Where(h => h.IsDaytime == daytime && h.FeelsLikeC is not null)
            .OrderBy(h => h.DisplayLocal))
        {
            if (best is null || h.FeelsLikeC < best.FeelsLikeC) best = h;
        }
        return best;
    }
}
