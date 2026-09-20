using FluentAssertions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherWindowThresholdsTests
{
    private static readonly WeatherSignal[] All = { WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun };

    [Fact]
    public void Unselected_signals_get_no_threshold_even_when_one_is_stored()
    {
        var t = WeatherWindowThresholds.Resolve(
            new[] { WeatherSignal.Rain }, null, null, null, storedUvWarn: 8, storedFeelsWarn: 38);

        t.RainPct.Should().Be(60);
        t.FeelsLikeC.Should().BeNull();
        t.UvIndex.Should().BeNull();
        t.Selected.Should().BeEquivalentTo(new[] { WeatherSignal.Rain });
    }

    [Fact]
    public void A_missing_UserSettings_row_resolves_to_the_built_in_defaults()
    {
        // The handler passes null/null when the User has no UserSettings row at all.
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: null, storedFeelsWarn: null);

        t.RainPct.Should().Be(60);
        t.FeelsLikeC.Should().Be(40);
        t.UvIndex.Should().Be(6);
    }

    [Fact]
    public void A_stored_value_beats_the_built_in_default()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 8, storedFeelsWarn: 38);

        t.FeelsLikeC.Should().Be(38);
        t.UvIndex.Should().Be(8);
    }

    [Fact]
    public void A_stored_zero_means_off_so_the_selected_signal_never_blocks()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 0, storedFeelsWarn: 0);

        t.FeelsLikeC.Should().BeNull();
        t.UvIndex.Should().BeNull();
        t.RainPct.Should().Be(60, "rain has no stored threshold, so 'off' cannot apply to it");
        t.Selected.Should().BeEquivalentTo(All, "an off signal is still selected — its Worst* value is still reported");
    }

    [Fact]
    public void An_explicit_value_beats_the_stored_value_and_the_default()
    {
        var t = WeatherWindowThresholds.Resolve(All, 70, 42.5, 9, storedUvWarn: 0, storedFeelsWarn: 38);

        t.RainPct.Should().Be(70);
        t.FeelsLikeC.Should().Be(42.5);
        t.UvIndex.Should().Be(9, "an explicit value is honoured even when the stored threshold is off");
    }

    [Fact]
    public void Of_reads_the_threshold_for_one_signal()
    {
        var t = new WeatherThresholds(All, RainPct: 60, FeelsLikeC: 40, UvIndex: null);

        t.Of(WeatherSignal.Rain).Should().Be(60);
        t.Of(WeatherSignal.Heat).Should().Be(40);
        t.Of(WeatherSignal.Sun).Should().BeNull();
    }
}
