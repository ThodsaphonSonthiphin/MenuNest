using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherWindowSelectionTests
{
    private static readonly DateOnly Day0 = new(2026, 9, 20);
    private static readonly WeatherThresholds RainOnly = new(new[] { WeatherSignal.Rain }, 60, null, null);
    private static readonly WeatherThresholds RainAndHeat =
        new(new[] { WeatherSignal.Rain, WeatherSignal.Heat }, 60, 40, null);

    private static HourlyReading H(int day, int hour, int? rain = 10, double? feels = 30, int? uv = 2)
        => new(Day0.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), true, 28, feels, "CLEAR", null, rain, uv);

    private static IEnumerable<HourlyReading> Run(int day, int fromHour, int count)
        => Enumerable.Range(fromHour, count).Select(h => H(day, h));

    private static WeatherWindowCut Cut(
        IEnumerable<HourlyReading> hours, WeatherThresholds? t = null,
        DateOnly? from = null, DateOnly? to = null, int fromHour = 0, int toHour = 24, int min = 1)
        => WeatherWindowSelection.Cut(hours.ToList(), t ?? RainOnly, from, to, fromHour, toHour, min);

    // ── windows ───────────────────────────────────────────────────────────────

    [Fact]
    public void Consecutive_good_hours_form_one_window()
    {
        var cut = Cut(Run(0, 6, 3));

        cut.Miss.Should().BeNull();
        cut.Windows.Should().ContainSingle();
        var w = cut.Windows[0];
        w.Date.Should().Be(Day0);
        w.StartLocal.Should().Be(Day0.ToDateTime(new TimeOnly(6, 0)));
        w.EndLocalExclusive.Should().Be(Day0.ToDateTime(new TimeOnly(9, 0)));
        w.Hours.Should().Be(3);
    }

    [Fact]
    public void A_blocked_hour_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7), H(0, 8, rain: 70), H(0, 9), H(0, 10) });

        cut.Windows.Select(w => w.Hours).Should().Equal(2, 2);
    }

    [Fact]
    public void A_missing_bucket_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7), H(0, 9), H(0, 10) });

        cut.Windows.Select(w => w.StartLocal.Hour).Should().Equal(6, 9);
    }

    [Fact]
    public void An_unjudgeable_hour_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: null), H(0, 8) });

        cut.Windows.Select(w => w.StartLocal.Hour).Should().Equal(6, 8);
    }

    [Fact]
    public void Runs_shorter_than_the_minimum_are_dropped()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: 90), H(0, 8), H(0, 9) }, min: 2);

        cut.Windows.Should().ContainSingle().Which.StartLocal.Hour.Should().Be(8);
    }

    [Fact]
    public void The_band_edges_split_windows_per_day()
    {
        var cut = Cut(Run(0, 0, 24).Concat(Run(1, 0, 24)), fromHour: 8, toHour: 16);

        cut.Windows.Should().HaveCount(2);
        cut.Windows.Should().OnlyContain(w => w.Hours == 8);
        cut.Windows.Select(w => w.Date).Should().Equal(Day0, Day0.AddDays(1));
    }

    [Fact]
    public void With_the_default_band_midnight_still_splits_windows()
    {
        var cut = Cut(new[] { H(0, 22), H(0, 23), H(1, 0), H(1, 1) });

        cut.Windows.Select(w => w.Hours).Should().Equal(2, 2);
    }

    [Fact]
    public void A_wrapping_band_joins_across_midnight_and_dates_the_window_by_its_first_hour()
    {
        var cut = Cut(Run(0, 18, 6).Concat(Run(1, 0, 2)), fromHour: 18, toHour: 2);

        var w = cut.Windows.Should().ContainSingle().Subject;
        w.Hours.Should().Be(8);
        w.Date.Should().Be(Day0);
        w.EndLocalExclusive.Should().Be(Day0.AddDays(1).ToDateTime(new TimeOnly(2, 0)));
    }

    [Fact]
    public void The_date_range_filters_by_band_date()
    {
        var cut = Cut(Run(0, 6, 3).Concat(Run(1, 6, 3)), from: Day0.AddDays(1), to: Day0.AddDays(1));

        cut.Windows.Should().ContainSingle().Which.Date.Should().Be(Day0.AddDays(1));
        cut.FirstExaminedDate.Should().Be(Day0.AddDays(1));
        cut.LastExaminedDate.Should().Be(Day0.AddDays(1));
    }

    [Fact]
    public void Worst_values_are_reported_only_for_selected_signals()
    {
        var cut = Cut(new[] { H(0, 6, rain: 10, feels: 35, uv: 9), H(0, 7, rain: 40, feels: 36, uv: 3) });

        var w = cut.Windows.Single();
        w.WorstRainPct.Should().Be(40);
        w.WorstFeelsLikeC.Should().BeNull("Heat was not selected — an unjudged value must not be reported");
        w.WorstUvIndex.Should().BeNull();
    }

    // ── the miss ──────────────────────────────────────────────────────────────

    [Fact]
    public void No_hours_at_all_is_NoWeatherData()
    {
        var cut = Cut(Array.Empty<HourlyReading>());

        cut.Windows.Should().BeEmpty();
        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        cut.Miss.HoursExamined.Should().Be(0);
        cut.FirstExaminedDate.Should().BeNull();
    }

    [Fact]
    public void Hours_that_all_fall_outside_the_band_are_NoWeatherData()
    {
        var cut = Cut(new[] { H(0, 20), H(0, 21) }, fromHour: 8, toHour: 10);

        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
    }

    [Fact]
    public void Every_hour_blocked_names_the_signal_the_closest_value_and_the_threshold()
    {
        var cut = Cut(new[] { H(0, 6, rain: 70), H(0, 7, rain: 65), H(0, 8, rain: 80) });

        cut.Windows.Should().BeEmpty();
        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        m.BlockingSignal.Should().Be(WeatherSignal.Rain);
        m.ClosestValue.Should().Be(65);
        m.Threshold.Should().Be(60);
        m.HoursExamined.Should().Be(3);
        m.HoursBlocked.Should().Be(3);
        m.LongestRunHours.Should().Be(0);
    }

    [Fact]
    public void The_blocking_signal_is_the_one_that_failed_the_most_hours()
    {
        var cut = Cut(new[] { H(0, 6, feels: 45), H(0, 7, feels: 44), H(0, 8, rain: 70) }, RainAndHeat);

        cut.Miss!.BlockingSignal.Should().Be(WeatherSignal.Heat);
        cut.Miss.ClosestValue.Should().Be(44);
        cut.Miss.Threshold.Should().Be(40);
    }

    [Fact]
    public void A_tie_is_broken_in_declared_order_and_closest_ignores_hours_this_signal_passed()
    {
        // Hour 6 is rejected by heat only; its rain of 10 says nothing about how far to relax rain.
        var cut = Cut(new[] { H(0, 6, rain: 10, feels: 45), H(0, 7, rain: 70, feels: 30) }, RainAndHeat);

        cut.Miss!.BlockingSignal.Should().Be(WeatherSignal.Rain);
        cut.Miss.ClosestValue.Should().Be(70, "not 10 — quoting a passing hour's value would relax nothing");
    }

    [Fact]
    public void Some_good_hours_but_no_run_long_enough_is_NoWindowLongEnough()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: 70), H(0, 8) }, min: 2);

        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.NoWindowLongEnough);
        m.LongestRunHours.Should().Be(1);
        m.BlockingSignal.Should().Be(WeatherSignal.Rain);
        m.HoursBlocked.Should().Be(1);
    }

    [Fact]
    public void Every_hour_unjudgeable_is_NoWeatherData_with_no_invented_cause()
    {
        var cut = Cut(new[] { H(0, 6, rain: null), H(0, 7, rain: null) });

        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        m.BlockingSignal.Should().BeNull();
        m.ClosestValue.Should().BeNull();
        m.HoursExamined.Should().Be(2);
        m.HoursBlocked.Should().Be(0);
    }

    [Fact]
    public void Miss_is_null_exactly_when_windows_exist()
    {
        Cut(Run(0, 6, 2)).Miss.Should().BeNull();
        Cut(new[] { H(0, 6, rain: 99) }).Miss.Should().NotBeNull();
    }

    [Fact]
    public void ClosestValue_for_heat_is_the_rounded_value()
    {
        var thresholds = new WeatherThresholds(new[] { WeatherSignal.Heat }, null, 39, null);
        var cut = Cut(new[] { H(0, 6, feels: 39.6) }, thresholds);

        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        m.BlockingSignal.Should().Be(WeatherSignal.Heat);
        m.ClosestValue.Should().Be(40);
        m.Threshold.Should().Be(39);
    }
}
