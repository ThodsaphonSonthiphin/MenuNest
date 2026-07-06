using FluentValidation.TestHelper;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetStopWeatherValidatorTests
{
    private readonly GetStopWeatherValidator _v = new();

    [Fact]
    public void Rejects_empty_points()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now, new List<WeatherPointDto>());
        _v.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Points);
    }

    [Fact]
    public void Rejects_out_of_range_latitude()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 999, 100, null) });
        _v.TestValidate(q).ShouldHaveValidationErrorFor("Points[0].Lat");
    }

    [Fact]
    public void Accepts_valid_points()
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });
        _v.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Accepts_null_arrivalIso_on_on_arrival_points() // tolerance: arrivalIso is optional; a null yields No-data downstream, not a validation error (ADR-032)
    {
        var q = new GetStopWeatherQuery(WeatherReadingKind.OnArrival,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });
        _v.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }
}
