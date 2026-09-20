using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

public sealed class TripsControllerWeatherWindowsTests
{
    // The same shape Program.cs configures for MVC: web defaults + string enums.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void The_action_is_routed_POST_api_trips_weather_windows()
    {
        var method = typeof(TripsController).GetMethod(nameof(TripsController.WeatherWindows))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("api/trips/weather/windows");
    }

    [Fact]
    public void A_minimal_body_binds_with_string_signals_and_every_optional_left_null()
    {
        var q = JsonSerializer.Deserialize<FindWeatherWindowsQuery>(
            """{ "lat": 19.36, "lng": 98.44, "signals": ["Heat", "Sun"] }""", Json)!;

        q.Lat.Should().Be(19.36);
        q.Lng.Should().Be(98.44);
        q.Signals.Should().Equal(WeatherSignal.Heat, WeatherSignal.Sun);
        q.FromDate.Should().BeNull();
        q.ToDate.Should().BeNull();
        q.FromHour.Should().BeNull();
        q.MaxFeelsLikeC.Should().BeNull();
        q.MinWindowHours.Should().BeNull();
    }

    [Fact]
    public void A_full_body_binds_every_member()
    {
        var q = JsonSerializer.Deserialize<FindWeatherWindowsQuery>("""
            { "lat": 19.36, "lng": 98.44, "signals": ["Rain"],
              "fromDate": "2026-09-26", "toDate": "2026-09-27",
              "fromHour": 8, "toHour": 12, "maxRainPct": 70, "minWindowHours": 3 }
            """, Json)!;

        q.FromDate.Should().Be(new DateOnly(2026, 9, 26));
        q.ToDate.Should().Be(new DateOnly(2026, 9, 27));
        q.FromHour.Should().Be(8);
        q.ToHour.Should().Be(12);
        q.MaxRainPct.Should().Be(70);
        q.MinWindowHours.Should().Be(3);
    }

    [Fact]
    public async Task WeatherWindows_sends_the_body_unchanged_and_returns_the_result()
    {
        var mediator = new Mock<IMediator>();
        var q = new FindWeatherWindowsQuery(19.36, 98.44, new[] { WeatherSignal.Rain }, MaxRainPct: 70);
        var dto = new WeatherWindowResultDto(
            Array.Empty<WeatherWindowDto>(), null, false, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 29));
        mediator
            .Setup(m => m.Send(It.Is<FindWeatherWindowsQuery>(x => x == q), It.IsAny<CancellationToken>()))
            .Returns<FindWeatherWindowsQuery, CancellationToken>((_, _) => new ValueTask<WeatherWindowResultDto>(dto));

        var result = await new TripsController(mediator.Object).WeatherWindows(q, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(dto);
    }
}
