using FluentAssertions;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class RetimeStopToWeatherHandlerTests
{
    // Captures the point/hours it is asked for and replays a fixed hourly list.
    private sealed class StubWeather : IWeatherService
    {
        public WeatherPoint? ReceivedPoint;
        public int ReceivedHours;
        private readonly IReadOnlyList<HourlyReading> _hours;
        public StubWeather(IReadOnlyList<HourlyReading> hours) => _hours = hours;

        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
        {
            ReceivedPoint = point; ReceivedHours = hours;
            return Task.FromResult(_hours);
        }
    }

    private static HourlyReading H(int hour, bool day, double feels)
        => new(new DateTime(2026, 7, 12, hour, 0, 0), day, feels - 5, feels, "CLEAR", null, 0, 0);

    // A mediator whose Send(RetimeStopToHourCommand) runs the REAL apply-core handler against
    // the same InMemory context, so the delegation actually retimes the seeded day.
    private static Mock<IMediator> DelegatingMediator(HandlerTestFixture fx, Action<RetimeStopToHourCommand> capture)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RetimeStopToHourCommand>(), It.IsAny<CancellationToken>()))
            .Returns<RetimeStopToHourCommand, CancellationToken>((cmd, token) =>
            {
                capture(cmd);
                return new RetimeStopToHourHandler(fx.Db, fx.UserProvisioner.Object, new RetimeStopToHourValidator(), fx.Clock)
                    .Handle(cmd, token);
            });
        return mediator;
    }

    private static Mock<IRouteService> RouteReturning(int seconds)
    {
        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegTime> { new(seconds, 5000, null, RouteSource.Routed) });
        return route;
    }

    [Fact]
    public async Task CoolestDaytime_picks_min_feelslike_hour_subtracts_offset_and_retimes_the_day()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);                 // apply must pin (turn OFF)
        fx.Db.ItineraryDays.Add(day);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        var pB = TripPlace.Create(trip.Id, "B", 13.80, 100.60, PlaceCategory.Eat);  // anchor place
        fx.Db.TripPlaces.AddRange(pA, pB);
        var s0 = Stop.Create(day.Id, pA.Id, 0, 60, TravelMode.Drive);               // dwell 60 before anchor
        var s1 = Stop.Create(day.Id, pB.Id, 1, 45, TravelMode.Walk);                // anchor stop
        fx.Db.Stops.AddRange(s0, s1);
        await fx.Db.SaveChangesAsync();

        // Daytime coolest is 06:00 (feels 30); leg 1800s=30m + dwell 60m => offset 90m => 06:00 - 90m = 04:30.
        var hours = new List<HourlyReading> { H(6, true, 30), H(13, true, 39), H(15, true, 39), H(22, false, 28) };
        var weather = new StubWeather(hours);
        RetimeStopToHourCommand? delegated = null;
        var mediator = DelegatingMediator(fx, c => delegated = c);

        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(1800).Object, weather, mediator.Object,
            new RetimeStopToWeatherValidator());

        var result = await handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, s1.Id, new RetimeTarget("coolestDaytime", null, 48)),
            CancellationToken.None);

        // Weather was read for the anchor stop's coords + window.
        weather.ReceivedPoint!.Lat.Should().Be(13.80);
        weather.ReceivedPoint!.Lng.Should().Be(100.60);
        weather.ReceivedHours.Should().Be(48);
        // Delegated to the apply core with the resolved start/date.
        delegated!.NewDayStartTime.Should().Be(new TimeOnly(4, 30));
        delegated!.NewAnchorDate.Should().Be(new DateOnly(2026, 7, 12));
        // The anchor day ends retimed + pinned.
        var reloaded = await fx.Db.ItineraryDays.FirstAsync(d => d.Id == day.Id, CancellationToken.None);
        reloaded.DayStartTime.Should().Be(new TimeOnly(4, 30));
        reloaded.UseCurrentTimeAsStart.Should().BeFalse();
        result.MovedTrip.Should().BeFalse();
    }

    [Fact]
    public async Task Unreachably_early_target_throws()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        fx.Db.ItineraryDays.Add(day);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        var pB = TripPlace.Create(trip.Id, "B", 13.80, 100.60, PlaceCategory.Eat);
        fx.Db.TripPlaces.AddRange(pA, pB);
        var s0 = Stop.Create(day.Id, pA.Id, 0, 60, TravelMode.Drive);
        var s1 = Stop.Create(day.Id, pB.Id, 1, 45, TravelMode.Walk);
        fx.Db.Stops.AddRange(s0, s1);
        await fx.Db.SaveChangesAsync();

        var mediator = DelegatingMediator(fx, _ => { });
        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(1800).Object,
            new StubWeather(Array.Empty<HourlyReading>()), mediator.Object, new RetimeStopToWeatherValidator());

        // Target 00:30 with offset 90m => start would be negative.
        var act = () => handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, s1.Id,
                new RetimeTarget("hour", new DateTime(2026, 7, 12, 0, 30, 0), null)),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
        mediator.Verify(m => m.Send(It.IsAny<RetimeStopToHourCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_stop_throws()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var mediator = DelegatingMediator(fx, _ => { });
        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(1800).Object,
            new StubWeather(Array.Empty<HourlyReading>()), mediator.Object, new RetimeStopToWeatherValidator());

        var act = () => handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, Guid.NewGuid(),
                new RetimeTarget("hour", new DateTime(2026, 7, 12, 10, 0, 0), null)),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }

    // Seeds a 1-day trip with a single anchor stop (index 0, no offset/legs involved) — keeps these
    // resolver-focused tests from depending on IRouteService leg math.
    private static async Task<(Trip trip, ItineraryDay day, Stop stop)> SeedOneStopDayAsync(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        fx.Db.ItineraryDays.Add(day);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        fx.Db.TripPlaces.Add(pA);
        var stop = Stop.Create(day.Id, pA.Id, 0, 1, TravelMode.Drive);
        fx.Db.Stops.Add(stop);
        await fx.Db.SaveChangesAsync();
        return (trip, day, stop);
    }

    [Fact]
    public async Task Coolest_target_with_no_candidates_in_requested_half_throws()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneStopDayAsync(fx);

        // Every hour StubWeather returns is nighttime; requesting coolestDaytime leaves
        // WeatherHourSelection.CoolestHour with no candidate for that half.
        var hours = new List<HourlyReading> { H(22, false, 28), H(23, false, 27) };
        var weather = new StubWeather(hours);
        var mediator = DelegatingMediator(fx, _ => { });
        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(0).Object, weather, mediator.Object,
            new RetimeStopToWeatherValidator());

        var act = () => handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, stop.Id, new RetimeTarget("coolestDaytime", null, 48)),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
        mediator.Verify(m => m.Send(It.IsAny<RetimeStopToHourCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Coolest_target_with_null_windowHours_defaults_to_48()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneStopDayAsync(fx);

        var hours = new List<HourlyReading> { H(6, true, 30) };
        var weather = new StubWeather(hours);
        var mediator = DelegatingMediator(fx, _ => { });
        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(0).Object, weather, mediator.Object,
            new RetimeStopToWeatherValidator());

        await handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, stop.Id, new RetimeTarget("coolestDaytime", null, null)),
            CancellationToken.None);

        weather.ReceivedHours.Should().Be(48); // WindowHours omitted ⇒ defaults to 48, not 0/unbounded
    }

    [Fact]
    public async Task Coolest_result_on_a_later_calendar_date_advances_the_delegated_NewAnchorDate()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneStopDayAsync(fx);

        // Coolest nighttime hour is on the NEXT calendar date (cross-midnight): 07-12 23:00 (feels 28)
        // vs 07-13 01:00 (feels 20, cooler) — the min-feels-like pick crosses into the next day.
        var hours = new List<HourlyReading>
        {
            new(new DateTime(2026, 7, 12, 23, 0, 0), false, 23, 28, "CLEAR", null, 0, 0),
            new(new DateTime(2026, 7, 13, 1, 0, 0), false, 15, 20, "CLEAR", null, 0, 0),
        };
        var weather = new StubWeather(hours);
        RetimeStopToHourCommand? delegated = null;
        var mediator = DelegatingMediator(fx, c => delegated = c);
        var handler = new RetimeStopToWeatherHandler(
            fx.Db, fx.UserProvisioner.Object, RouteReturning(0).Object, weather, mediator.Object,
            new RetimeStopToWeatherValidator());

        var result = await handler.Handle(
            new RetimeStopToWeatherCommand(trip.Id, day.Id, stop.Id, new RetimeTarget("coolestNighttime", null, 48)),
            CancellationToken.None);

        delegated!.NewAnchorDate.Should().Be(new DateOnly(2026, 7, 13));
        delegated!.NewDayStartTime.Should().Be(new TimeOnly(1, 0));
        result.MovedTrip.Should().BeTrue();
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id, CancellationToken.None)).StartDate
            .Should().Be(new DateOnly(2026, 7, 13));
    }
}
