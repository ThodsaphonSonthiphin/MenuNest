using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The windows found, or why there are none, plus the band dates actually examined.</summary>
public sealed record WeatherWindowCut(
    IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss,
    DateOnly? FirstExaminedDate, DateOnly? LastExaminedDate);

/// <summary>Cuts an hourly forecast into Weather windows. Pure — no I/O — in the same spirit as
/// WeatherHourSelection.CoolestHour, so the whole judging rule is unit-testable.</summary>
public static class WeatherWindowSelection
{
    private sealed record Examined(HourlyReading Hour, DateOnly BandDate, HourVerdict Verdict);

    public static WeatherWindowCut Cut(
        IReadOnlyList<HourlyReading> hours, WeatherThresholds thresholds,
        DateOnly? fromDate, DateOnly? toDate, int fromHour, int toHour, int minWindowHours)
    {
        var examined = hours
            .Where(h => WeatherHourJudge.InBand(h.DisplayLocal.Hour, fromHour, toHour))
            .Select(h => new Examined(h, WeatherHourJudge.BandDate(h.DisplayLocal, fromHour, toHour),
                                      WeatherHourJudge.Judge(h, thresholds)))
            .Where(x => (fromDate is null || x.BandDate >= fromDate) && (toDate is null || x.BandDate <= toDate))
            .OrderBy(x => x.Hour.DisplayLocal)
            .ToList();

        if (examined.Count == 0)
            return new WeatherWindowCut(
                Array.Empty<WeatherWindowDto>(),
                new WeatherWindowMissDto(WeatherWindowMissReason.NoWeatherData, null, null, null, 0, 0, 0),
                null, null);

        var firstDate = examined.Min(x => x.BandDate);
        var lastDate = examined.Max(x => x.BandDate);

        var runs = new List<List<Examined>>();
        List<Examined>? current = null;
        foreach (var x in examined)
        {
            if (!x.Verdict.IsGood) { current = null; continue; }
            var joins = current is not null
                && current[^1].BandDate == x.BandDate
                && current[^1].Hour.DisplayLocal.AddHours(1) == x.Hour.DisplayLocal;
            if (!joins)
            {
                current = new List<Examined>();
                runs.Add(current);
            }
            current!.Add(x);
        }

        var windows = runs
            .Where(r => r.Count >= minWindowHours)
            .Select(r => ToWindow(r, thresholds))
            .ToList();
        if (windows.Count > 0)
            return new WeatherWindowCut(windows, null, firstDate, lastDate);

        var blocked = examined.Where(x => x.Verdict.IsBlocked).ToList();
        var reason = runs.Count > 0 ? WeatherWindowMissReason.NoWindowLongEnough
            : blocked.Count > 0 ? WeatherWindowMissReason.AllHoursBlocked
            : WeatherWindowMissReason.NoWeatherData;

        WeatherSignal? blocking = null;
        double? closest = null;
        double? threshold = null;
        if (blocked.Count > 0)
        {
            // OrderByDescending is stable, so ties keep the enum's declared order: Rain, Heat, Sun.
            var signal = Enum.GetValues<WeatherSignal>()
                .OrderByDescending(s => blocked.Count(x => x.Verdict.Failed.Contains(s)))
                .First();
            blocking = signal;
            // Only hours THIS signal blocked: an hour it passed says nothing about how far to relax it.
            closest = blocked
                .Where(x => x.Verdict.Failed.Contains(signal))
                .Min(x => WeatherHourJudge.ValueOf(x.Hour, signal));
            threshold = thresholds.Of(signal);
        }

        var miss = new WeatherWindowMissDto(
            reason, blocking, closest, threshold,
            HoursExamined: examined.Count,
            HoursBlocked: blocked.Count,
            LongestRunHours: runs.Count == 0 ? 0 : runs.Max(r => r.Count));
        return new WeatherWindowCut(Array.Empty<WeatherWindowDto>(), miss, firstDate, lastDate);
    }

    private static WeatherWindowDto ToWindow(List<Examined> run, WeatherThresholds t)
    {
        bool Selected(WeatherSignal s) => t.Selected.Contains(s);
        return new WeatherWindowDto(
            Date: run[0].BandDate,
            StartLocal: run[0].Hour.DisplayLocal,
            EndLocalExclusive: run[^1].Hour.DisplayLocal.AddHours(1),
            Hours: run.Count,
            WorstRainPct: Selected(WeatherSignal.Rain) ? run.Max(x => x.Hour.RainPct) : null,
            WorstFeelsLikeC: Selected(WeatherSignal.Heat) ? run.Max(x => x.Hour.FeelsLikeC) : null,
            WorstUvIndex: Selected(WeatherSignal.Sun) ? run.Max(x => x.Hour.UvIndex) : null);
    }
}
