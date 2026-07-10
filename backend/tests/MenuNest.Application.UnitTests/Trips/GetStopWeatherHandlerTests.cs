using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetStopWeatherHandlerTests
{
    private sealed class StubWeather : IWeatherService
    {
        public IReadOnlyList<WeatherPoint>? Received;
        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
        {
            Received = points;
            IReadOnlyList<WeatherReading> readings = points
                .Select(p => new WeatherReading(p.StopId, true, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 29.1, 20, "มีเมฆมาก"))
                .ToList();
            return Task.FromResult(readings);
        }
    }

    private static GetStopWeatherHandler Build(StubWeather w) => new(w, new GetStopWeatherValidator());

    [Fact]
    public async Task Maps_service_readings_to_dtos()
    {
        var handler = Build(new StubWeather());
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, null) });

        var dtos = await handler.Handle(q, CancellationToken.None);

        dtos.Should().HaveCount(1);
        dtos[0].StopId.Should().Be("s1");
        dtos[0].HasData.Should().BeTrue();
        dtos[0].ConditionType.Should().Be("CLOUDY");
        dtos[0].TempC.Should().Be(29.1);
        dtos[0].RainPct.Should().Be(20);
        dtos[0].Description.Should().Be("มีเมฆมาก");
    }

    [Fact]
    public async Task Now_ignores_arrivalIso_when_building_points()
    {
        var stub = new StubWeather();
        var q = new GetStopWeatherQuery(WeatherReadingKind.Now,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, new DateTime(2026, 7, 12, 14, 0, 0)) });

        await Build(stub).Handle(q, CancellationToken.None);

        stub.Received![0].ArrivalLocal.Should().BeNull(); // Now => arrival dropped
    }

    [Fact]
    public async Task OnArrival_forwards_arrivalIso_to_the_service()
    {
        var stub = new StubWeather();
        var arrival = new DateTime(2026, 7, 12, 14, 0, 0);
        var q = new GetStopWeatherQuery(WeatherReadingKind.OnArrival,
            new List<WeatherPointDto> { new("s1", 13.7, 100.5, arrival) });

        await Build(stub).Handle(q, CancellationToken.None);

        stub.Received![0].ArrivalLocal.Should().Be(arrival);
    }
}
