using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetStopHourlyForecastHandlerTests
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
                new(new DateTime(2026, 7, 12, 13, 0, 0), true, 34.0, 39.4, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 20, 8),
                new(new DateTime(2026, 7, 12, 22, 0, 0), false, 28.0, 30.0, "CLEAR", null, 5, 0),
            };
            return Task.FromResult(list);
        }
    }

    private static GetStopHourlyForecastHandler Build(HandlerTestFixture fx, StubWeather w)
        => new(fx.Db, fx.UserProvisioner.Object, w, new GetStopHourlyForecastValidator());

    [Fact]
    public async Task Resolves_the_stops_place_coords_and_forwards_hours()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12));
        fx.Db.ItineraryDays.Add(day);
        var place = TripPlace.Create(trip.Id, "A", 18.7883, 98.9853, PlaceCategory.See);
        fx.Db.TripPlaces.Add(place);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(stop);
        await fx.Db.SaveChangesAsync();

        var weather = new StubWeather();
        var dtos = await Build(fx, weather).Handle(
            new GetStopHourlyForecastQuery(trip.Id, stop.Id, 48), CancellationToken.None);

        weather.ReceivedPoint!.Lat.Should().Be(18.7883);
        weather.ReceivedPoint!.Lng.Should().Be(98.9853);
        weather.ReceivedHours.Should().Be(48);
        dtos.Should().HaveCount(2);
        dtos[0].IsDaytime.Should().BeTrue();
        dtos[0].FeelsLikeC.Should().Be(39.4);
        dtos[1].IsDaytime.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_stop_throws_not_found()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var act = () => Build(fx, new StubWeather()).Handle(
            new GetStopHourlyForecastQuery(trip.Id, Guid.NewGuid(), 48), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }
}
