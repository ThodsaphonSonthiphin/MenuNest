using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

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
            new List<PlaceChecklistEntryDto>(),
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
}