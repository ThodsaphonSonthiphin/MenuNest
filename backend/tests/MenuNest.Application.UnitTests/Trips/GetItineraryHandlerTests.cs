using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
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
            .ReturnsAsync(new List<LegTime> { new(900, 4200) }); // one leg for two points

        var days = await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        days.Should().HaveCount(1);
        days[0].Stops.Should().HaveCount(2);
        days[0].Stops[0].LegToReach.Should().BeNull();           // first stop: no leg
        days[0].Stops[1].LegToReach!.Seconds.Should().Be(900);   // second stop: leg from first
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
            .ReturnsAsync(new List<LegTime> { new(600, 2000) });

        await new GetItineraryHandler(fx.Db, fx.UserProvisioner.Object, route.Object)
            .Handle(new GetItineraryQuery(trip.Id), CancellationToken.None);

        capturedModes.Should().Equal(new[] { TravelMode.Walk, TravelMode.Transit });
    }
}
