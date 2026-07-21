using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherHourSelectionTests
{
    private static HourlyReading H(int hour, bool day, double feels)
        => new(new DateTime(2026, 7, 12, hour, 0, 0), day, feels - 5, feels, "CLEAR", null, 0, 0);

    [Fact]
    public void Picks_min_feelslike_of_the_requested_half_earliest_on_ties()
    {
        var hours = new List<HourlyReading> { H(13, true, 39), H(15, true, 39), H(22, false, 30), H(2, false, 28) };
        WeatherHourSelection.CoolestHour(hours, true)!.DisplayLocal.Hour.Should().Be(13);
        WeatherHourSelection.CoolestHour(hours, false)!.DisplayLocal.Hour.Should().Be(2);
    }

    [Fact]
    public void Returns_null_when_the_half_is_empty()
        => WeatherHourSelection.CoolestHour(new List<HourlyReading> { H(13, true, 39) }, false).Should().BeNull();
}
