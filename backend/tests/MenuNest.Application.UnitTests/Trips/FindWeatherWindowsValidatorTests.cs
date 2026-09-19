using FluentAssertions;
using FluentValidation.TestHelper;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class FindWeatherWindowsValidatorTests
{
    private readonly FindWeatherWindowsValidator _v = new();

    private static FindWeatherWindowsQuery Q(params WeatherSignal[] signals)
        => new(19.36, 98.44, signals.Length == 0 ? Array.Empty<WeatherSignal>() : signals);

    [Fact]
    public void Accepts_the_minimal_query()
        => _v.TestValidate(Q(WeatherSignal.Rain)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Accepts_a_band_that_wraps_past_midnight()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { FromHour = 18, ToHour = 2 })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Accepts_thresholds_for_selected_signals()
        => _v.TestValidate(Q(WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun)
                with { MaxRainPct = 70, MaxFeelsLikeC = 38.5, MaxUvIndex = 8 })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Rejects_an_empty_signal_list()
        => _v.TestValidate(Q()).ShouldHaveValidationErrorFor(x => x.Signals);

    [Fact]
    public void Rejects_a_null_signal_list()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { Signals = null! })
            .ShouldHaveValidationErrorFor(x => x.Signals);

    [Fact]
    public void Rejects_an_undefined_signal()
    {
        var result = _v.TestValidate(Q((WeatherSignal)99));
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Rejects_a_rain_threshold_when_rain_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Heat) with { MaxRainPct = 70 })
            .ShouldHaveValidationErrorFor(x => x.MaxRainPct);

    [Fact]
    public void Rejects_a_feels_like_threshold_when_heat_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Sun) with { MaxFeelsLikeC = 38 })
            .ShouldHaveValidationErrorFor(x => x.MaxFeelsLikeC);

    [Fact]
    public void Rejects_a_uv_threshold_when_sun_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Heat) with { MaxUvIndex = 8 })
            .ShouldHaveValidationErrorFor(x => x.MaxUvIndex);

    [Fact]
    public void Rejects_an_explicit_threshold_of_zero()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { MaxRainPct = 0 })
            .ShouldHaveValidationErrorFor(x => x.MaxRainPct);

    [Fact]
    public void Rejects_a_from_date_after_the_to_date()
    {
        var result = _v.TestValidate(Q(WeatherSignal.Rain) with
            { FromDate = new DateOnly(2026, 9, 22), ToDate = new DateOnly(2026, 9, 21) });
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(24, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(null, 25)]
    public void Rejects_hours_outside_the_band_ranges(int? fromHour, int? toHour)
    {
        var result = _v.TestValidate(Q(WeatherSignal.Rain) with { FromHour = fromHour, ToHour = toHour });
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Rejects_a_minimum_window_length_outside_1_to_24(int min)
        => _v.TestValidate(Q(WeatherSignal.Rain) with { MinWindowHours = min })
            .ShouldHaveValidationErrorFor(x => x.MinWindowHours);

    [Fact]
    public void Rejects_an_out_of_range_latitude()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { Lat = 91 })
            .ShouldHaveValidationErrorFor(x => x.Lat);
}
