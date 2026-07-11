using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetItineraryHandlerTests
{
    [Fact]
    public async Task Returns_days_with_ordered_stops_and_leg_times_for_non_first_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p1 = TripPlace.Create(trip.Id, "A", 18.80, 98.92, PlaceCategory.See);
        var p2 = TripPlace.Create(trip.Id, "B", 18.79, 98.99, PlaceCategory.Eat);
        fx.Db.TripPlaces.AddRange(p1, p2);
        fx.Db.Stops.Add(Stop.Create(day.Id, p1.Id, 0, 60, TravelMode.Drive));
        fx.Db.Stops.Add(Stop.Create(day.Id, p2.Id, 1, 45, TravelMode.Drive));
        await fx.Db.SaveChangesAsync();

        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegTime> { new(900, 4200, "poly123", RouteSource.Estimated) }); // one leg for two points

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        days.Should().HaveCount(1);
        days[0].Stops.Should().HaveCount(2);
        days[0].Stops[0].LegToReach.Should().BeNull();           // first stop: no leg
        days[0].Stops[1].LegToReach!.Seconds.Should().Be(900);   // second stop: leg from first
        days[0].Stops[1].LegToReach!.Source.Should().Be(RouteSource.Estimated);
        days[0].Stops[1].LegToReach!.EncodedPolyline.Should().Be("poly123");
    }

    [Fact]
    public async Task Returns_leg_times_using_arriving_stops_travel_mode()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p1 = TripPlace.Create(trip.Id, "A", 18.80, 98.92, PlaceCategory.See);
        var p2 = TripPlace.Create(trip.Id, "B", 18.79, 98.99, PlaceCategory.Eat);
        var p3 = TripPlace.Create(trip.Id, "C", 18.78, 98.88, PlaceCategory.See);
        fx.Db.TripPlaces.AddRange(p1, p2, p3);
        fx.Db.Stops.Add(Stop.Create(day.Id, p1.Id, 0, 60, TravelMode.Drive));   // stop[0] mode=Drive
        fx.Db.Stops.Add(Stop.Create(day.Id, p2.Id, 1, 45, TravelMode.Walk));    // stop[1] mode=Walk (arriving)
        fx.Db.Stops.Add(Stop.Create(day.Id, p3.Id, 2, 30, TravelMode.Transit)); // stop[2] mode=Transit (arriving)
        await fx.Db.SaveChangesAsync();

        var capturedModes = new List<TravelMode>();
        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(
                It.IsAny<IReadOnlyList<RoutePoint>>(),
                It.IsAny<TravelMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RoutePoint>, TravelMode, CancellationToken>(
                (_, mode, _) => capturedModes.Add(mode))
            .ReturnsAsync(new List<LegTime> { new(600, 2000, null, RouteSource.Routed) });

        await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        capturedModes.Should().Equal(new[] { TravelMode.Walk, TravelMode.Transit });
    }

    [Fact]
    public async Task Resolves_an_approach_leg_into_the_first_stop_when_viewer_coordinates_are_provided()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p1 = TripPlace.Create(trip.Id, "A", 18.80, 98.92, PlaceCategory.See);
        fx.Db.TripPlaces.Add(p1);
        fx.Db.Stops.Add(Stop.Create(day.Id, p1.Id, 0, 60, TravelMode.Drive));
        await fx.Db.SaveChangesAsync();

        IReadOnlyList<RoutePoint>? capturedPoints = null;
        var route = new Mock<IRouteService>();
        route.Setup(r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RoutePoint>, TravelMode, CancellationToken>((pts, _, _) => capturedPoints = pts)
            .ReturnsAsync(new List<LegTime> { new(500, 3000, "approachPoly", RouteSource.Routed) });

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, ViewerLat: 18.81, ViewerLng: 98.90), CancellationToken.None);

        days[0].Stops[0].LegToReach.Should().NotBeNull();
        days[0].Stops[0].LegToReach!.Seconds.Should().Be(500);
        days[0].Stops[0].LegToReach!.Source.Should().Be(RouteSource.Routed);
        days[0].Stops[0].LegToReach!.EncodedPolyline.Should().Be("approachPoly");
        capturedPoints.Should().NotBeNull();
        capturedPoints![0].Lat.Should().Be(18.81);
        capturedPoints![0].Lng.Should().Be(98.90);
        capturedPoints![1].Lat.Should().Be(18.80);
        capturedPoints![1].Lng.Should().Be(98.92);
    }

    [Fact]
    public async Task Does_not_resolve_an_approach_leg_when_the_day_has_no_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var route = new Mock<IRouteService>();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, ViewerLat: 18.81, ViewerLng: 98.90), CancellationToken.None);

        days.Should().HaveCount(1);
        days[0].Stops.Should().BeEmpty();
        route.Verify(
            r => r.GetLegTimesAsync(It.IsAny<IReadOnlyList<RoutePoint>>(), It.IsAny<TravelMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolves_a_current_time_day_start_in_the_supplied_time_zone()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Asia/Bangkok"), CancellationToken.None);

        days[0].UseCurrentTimeAsStart.Should().BeTrue();
        days[0].DayStartTime.Should().Be(new TimeOnly(19, 0, 0)); // 12:00 UTC + 7h ICT
        days[0].DayStartTime.Should().NotBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Applies_the_time_zone_not_the_server_clock()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "America/New_York"), CancellationToken.None);

        days[0].DayStartTime.Should().Be(new TimeOnly(7, 0, 0)); // 12:00 UTC - 5h EST (Jan, no DST)
    }

    [Fact]
    public async Task Ignores_a_missing_time_zone_when_no_day_is_flagged()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0)); // NOT flagged
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None); // no tz supplied

        days[0].UseCurrentTimeAsStart.Should().BeFalse();
        days[0].DayStartTime.Should().Be(new TimeOnly(9, 0)); // persisted, untouched
    }

    [Fact]
    public async Task Ignores_an_unresolvable_time_zone_when_no_day_is_flagged()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0)); // NOT flagged
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock)
            .Handle(new GetItineraryQuery(trip.Id, "Not/AZone"), CancellationToken.None); // bad tz, but unused

        days[0].DayStartTime.Should().Be(new TimeOnly(9, 0)); // no throw; bad tz never validated
    }

    [Fact]
    public async Task Rejects_a_missing_time_zone_when_a_day_is_flagged()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();
        var handler = new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock);

        var act = () => handler.Handle(new GetItineraryQuery(trip.Id), CancellationToken.None).AsTask(); // no tz

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Rejects_an_unresolvable_time_zone_when_a_day_is_flagged()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 1, 15), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 1, 15), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();
        var handler = new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, new Mock<IRouteService>().Object, fx.Clock);

        var act = () => handler.Handle(new GetItineraryQuery(trip.Id, "Not/AZone"), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }
}
