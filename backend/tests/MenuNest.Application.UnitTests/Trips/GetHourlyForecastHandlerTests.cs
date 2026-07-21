using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.GetHourlyForecast;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetHourlyForecastHandlerTests
{
    private sealed class StubWeather : IWeatherService
    {
        public WeatherPoint? ReceivedPoint;
        public int ReceivedHours;
        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
        {
            ReceivedPoint = point; ReceivedHours = hours;
            IReadOnlyList<HourlyReading> list = new List<HourlyReading>
            {
                new(new DateTime(2026,7,12,13,0,0), true,  34.0, 39.4, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 20, 8),
                new(new DateTime(2026,7,12,22,0,0), false, 28.0, 30.0, "CLEAR",  null, 5, 0),
            };
            return Task.FromResult(list);
        }
    }

    [Fact]
    public async Task Forwards_lat_lng_hours_and_maps_readings()
    {
        var stub = new StubWeather();
        var handler = new GetHourlyForecastHandler(stub, new GetHourlyForecastValidator());

        var dtos = await handler.Handle(new GetHourlyForecastQuery(13.7563, 100.5018, 48), CancellationToken.None);

        stub.ReceivedPoint!.Lat.Should().Be(13.7563);
        stub.ReceivedPoint!.Lng.Should().Be(100.5018);
        stub.ReceivedHours.Should().Be(48);
        dtos.Should().HaveCount(2);
        dtos[0].IsDaytime.Should().BeTrue();
        dtos[0].FeelsLikeC.Should().Be(39.4);
        dtos[1].IsDaytime.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_hours_over_the_240h_horizon()
    {
        var handler = new GetHourlyForecastHandler(new StubWeather(), new GetHourlyForecastValidator());
        await FluentActions.Awaiting(() => handler.Handle(new GetHourlyForecastQuery(13.75, 100.50, 999), CancellationToken.None).AsTask())
            .Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
