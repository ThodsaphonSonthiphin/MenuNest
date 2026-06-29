using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class ReorderStopsHandlerTests
{
    [Fact]
    public async Task Reorders_sequences_to_match_supplied_order()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var p = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.See);
        var q = TripPlace.Create(trip.Id, "B", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.AddRange(p, q);
        var s0 = Stop.Create(day.Id, p.Id, 0, 60, TravelMode.Drive);
        var s1 = Stop.Create(day.Id, q.Id, 1, 60, TravelMode.Drive);
        fx.Db.Stops.AddRange(s0, s1);
        await fx.Db.SaveChangesAsync();

        await new ReorderStopsHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ReorderStopsCommand(trip.Id, day.Id, new[] { s1.Id, s0.Id }), CancellationToken.None);

        var ordered = await fx.Db.Stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence).ToListAsync();
        ordered[0].Id.Should().Be(s1.Id);
        ordered[1].Id.Should().Be(s0.Id);
    }
}
