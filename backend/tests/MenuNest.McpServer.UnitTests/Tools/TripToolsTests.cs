using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;
using ModelContextProtocol.Server;

namespace MenuNest.McpServer.UnitTests.Tools;

public class TripToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly TripTools _sut;

    public TripToolsTests() => _sut = new TripTools(_mediator.Object);

    [Fact]
    public async Task push_place_profile_sends_PushPlaceProfileCommand_with_correct_ids()
    {
        var tripId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var expectedDto = new TripPlaceDto(
            Guid.NewGuid(), tripId, null, "Wat Arun",
            13.7437, 100.4888, null, PlaceCategory.See,
            null, null,
            null, null, null,
            new List<ReviewLinkDto>(),
            true,
            new List<SeasonPeriodDto>(),
            new List<BestTimeWindowDto>());

        _mediator
            .Setup(m => m.Send(It.Is<PushPlaceProfileCommand>(c => c.TripId == tripId && c.PlaceId == placeId), It.IsAny<CancellationToken>()))
            .Returns<PushPlaceProfileCommand, CancellationToken>((_, _) => new ValueTask<TripPlaceDto>(expectedDto));

        var result = await _sut.push_place_profile(tripId, placeId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<PushPlaceProfileCommand>(c => c.TripId == tripId && c.PlaceId == placeId), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expectedDto);
    }

    [Fact]
    public async Task get_stop_hourly_forecast_sends_query_with_correct_args()
    {
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        const int hours = 72;
        IReadOnlyList<HourlyReadingDto> expected = new List<HourlyReadingDto>
        {
            new(new DateTime(2026, 7, 12, 13, 0, 0), true, 34.0, 39.4, "CLOUDY", null, 20, 8),
        };

        _mediator
            .Setup(m => m.Send(It.Is<GetStopHourlyForecastQuery>(q => q.TripId == tripId && q.StopId == stopId && q.Hours == hours), It.IsAny<CancellationToken>()))
            .Returns<GetStopHourlyForecastQuery, CancellationToken>((_, _) => new ValueTask<IReadOnlyList<HourlyReadingDto>>(expected));

        var result = await _sut.get_stop_hourly_forecast(tripId, stopId, hours, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<GetStopHourlyForecastQuery>(q => q.TripId == tripId && q.StopId == stopId && q.Hours == hours), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task retime_stop_to_weather_sends_command_with_correct_args()
    {
        var tripId = Guid.NewGuid();
        var dayId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var target = new RetimeTarget("coolestDaytime", null, 48);
        var expected = new RetimeResultDto(false, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12), new TimeOnly(4, 30));

        _mediator
            .Setup(m => m.Send(It.Is<RetimeStopToWeatherCommand>(c => c.TripId == tripId && c.DayId == dayId && c.StopId == stopId && c.Target == target), It.IsAny<CancellationToken>()))
            .Returns<RetimeStopToWeatherCommand, CancellationToken>((_, _) => new ValueTask<RetimeResultDto>(expected));

        var result = await _sut.retime_stop_to_weather(tripId, dayId, stopId, target, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<RetimeStopToWeatherCommand>(c => c.TripId == tripId && c.DayId == dayId && c.StopId == stopId && c.Target == target), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    // ── find_weather_windows (#153) ───────────────────────────────────────────

    [Fact]
    public async Task find_weather_windows_sends_the_query_with_every_argument()
    {
        var dto = new WeatherWindowResultDto(
            Array.Empty<WeatherWindowDto>(), null, false, new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 27));
        _mediator
            .Setup(m => m.Send(It.IsAny<FindWeatherWindowsQuery>(), It.IsAny<CancellationToken>()))
            .Returns<FindWeatherWindowsQuery, CancellationToken>((_, _) => new ValueTask<WeatherWindowResultDto>(dto));

        var result = await _sut.find_weather_windows(
            19.36, 98.44, new[] { WeatherSignal.Heat, WeatherSignal.Sun },
            fromDate: new DateOnly(2026, 9, 26), toDate: new DateOnly(2026, 9, 27),
            fromHour: 8, toHour: 12, maxFeelsLikeC: 38, maxUvIndex: 7, minWindowHours: 3,
            ct: CancellationToken.None);

        result.Should().BeSameAs(dto);
        _mediator.Verify(m => m.Send(It.Is<FindWeatherWindowsQuery>(q =>
            q.Lat == 19.36 && q.Lng == 98.44
            && q.Signals.SequenceEqual(new[] { WeatherSignal.Heat, WeatherSignal.Sun })
            && q.FromDate == new DateOnly(2026, 9, 26) && q.ToDate == new DateOnly(2026, 9, 27)
            && q.FromHour == 8 && q.ToHour == 12
            && q.MaxRainPct == null && q.MaxFeelsLikeC == 38 && q.MaxUvIndex == 7
            && q.MinWindowHours == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void find_weather_windows_requires_only_lat_lng_and_signals()
    {
        var method = typeof(TripTools).GetMethod(nameof(TripTools.find_weather_windows))!;
        var tool = McpServerTool.Create(method, new TripTools(new Mock<IMediator>().Object), null);
        var schema = tool.ProtocolTool.InputSchema;

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "lat", "lng", "signals" });
        schema.GetProperty("properties").GetProperty("signals").GetRawText()
            .Should().Contain("\"Rain\"").And.Contain("\"Heat\"").And.Contain("\"Sun\"");
    }

    // The description is the ONLY place the assistant learns which signals a Thai question means,
    // that blocking is >=, and that an empty result is not bad weather. A silent edit is a silent
    // behaviour change, so it is pinned.
    [Fact]
    public void find_weather_windows_description_teaches_signal_choice_and_how_to_read_a_miss()
    {
        var description = typeof(TripTools)
            .GetMethod(nameof(TripTools.find_weather_windows))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("resolve_place", "the location must be resolved upstream (menunest-217)");
        description.Should().Contain("แดดไม่ร้อน").And.Contain("[Heat, Sun]");
        description.Should().Contain("ฝนไม่ตก").And.Contain("[Rain]");
        description.Should().Contain("AT OR ABOVE");
        description.Should().Contain("ABOVE closestValue");
        description.Should().Contain("NoWeatherData");
        description.Should().Contain("horizonTruncated");
        description.Should().Contain("hoursExamined is 0");
        description.Should().NotContain("best time", "the term is Weather window, never 'best time' (menunest-223)");
    }
}