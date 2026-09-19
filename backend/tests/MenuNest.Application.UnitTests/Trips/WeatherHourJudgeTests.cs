using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherHourJudgeTests
{
    private static readonly WeatherSignal[] All = { WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun };

    private static HourlyReading Hour(int? rain = 10, double? feels = 30, int? uv = 2, int hour = 10, int day = 20)
        => new(new DateTime(2026, 9, day, hour, 0, 0), true, 28, feels, "CLEAR", null, rain, uv);

    [Theory]
    [InlineData(8, true)]
    [InlineData(15, true)]
    [InlineData(16, false)] // toHour is exclusive
    [InlineData(7, false)]
    public void InBand_without_wrap_is_fromHour_inclusive_to_toHour_exclusive(int hour, bool expected)
        => WeatherHourJudge.InBand(hour, 8, 16).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(23)]
    public void The_default_band_0_to_24_holds_every_hour(int hour)
        => WeatherHourJudge.InBand(hour, 0, 24).Should().BeTrue();

    [Theory]
    [InlineData(18, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(17, false)]
    public void InBand_wraps_past_midnight_when_toHour_is_not_after_fromHour(int hour, bool expected)
        => WeatherHourJudge.InBand(hour, 18, 2).Should().Be(expected);

    [Fact]
    public void The_wrapped_tail_belongs_to_the_band_opened_on_the_earlier_date()
    {
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 21, 1, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 20));
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 20, 19, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public void Without_wrap_the_band_date_is_the_hours_own_date()
        => WeatherHourJudge.BandDate(new DateTime(2026, 9, 21, 1, 0, 0), 0, 24)
            .Should().Be(new DateOnly(2026, 9, 21));

    [Fact]
    public void An_hour_below_every_threshold_is_good()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 59, feels: 39.9, uv: 5), new WeatherThresholds(All, 60, 40, 6));

        v.IsGood.Should().BeTrue();
        v.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void A_value_exactly_at_the_threshold_blocks_matching_the_web_apps_badge_rule()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 60, feels: 40, uv: 6), new WeatherThresholds(All, 60, 40, 6));

        v.IsGood.Should().BeFalse();
        v.Failed.Should().Equal(WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun);
    }

    [Fact]
    public void A_missing_value_for_a_gating_signal_makes_the_hour_unjudgeable_not_blocked()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: null), new WeatherThresholds(new[] { WeatherSignal.Rain }, 60, null, null));

        v.IsGood.Should().BeFalse();
        v.IsBlocked.Should().BeFalse();
        v.Failed.Should().BeEmpty();
    }

    [Fact]
    public void A_signal_with_no_threshold_is_never_consulted_even_when_its_value_is_missing()
    {
        // Heat is selected but switched off (threshold null); its missing value must not matter.
        var v = WeatherHourJudge.Judge(Hour(feels: null), new WeatherThresholds(new[] { WeatherSignal.Heat }, null, null, null));

        v.IsGood.Should().BeTrue();
    }

    [Fact]
    public void A_rejection_beats_a_missing_value()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 80, feels: null), new WeatherThresholds(All, 60, 40, 6));

        v.IsBlocked.Should().BeTrue();
        v.Failed.Should().Equal(WeatherSignal.Rain);
    }

    [Fact]
    public void ValueOf_reads_the_matching_field()
    {
        var h = Hour(rain: 55, feels: 33.5, uv: 7);

        WeatherHourJudge.ValueOf(h, WeatherSignal.Rain).Should().Be(55);
        WeatherHourJudge.ValueOf(h, WeatherSignal.Heat).Should().Be(33.5);
        WeatherHourJudge.ValueOf(h, WeatherSignal.Sun).Should().Be(7);
    }
}
