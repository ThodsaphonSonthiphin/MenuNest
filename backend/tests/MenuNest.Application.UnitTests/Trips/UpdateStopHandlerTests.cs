using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class UpdateStopHandlerTests
{
    private static (Trip trip, Stop stop) Seed(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.See);
        fx.Db.TripPlaces.Add(place);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(stop);
        fx.Db.SaveChanges();
        return (trip, stop);
    }

    [Fact]
    public async Task IsVisited_true_marks_the_stop_visited()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, null, null, IsVisited: true), CancellationToken.None);

        (await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id)).IsVisited.Should().BeTrue();
    }

    [Fact]
    public async Task IsVisited_false_clears_the_flag()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);
        stop.SetVisited(true);
        await fx.Db.SaveChangesAsync();

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, null, null, IsVisited: false), CancellationToken.None);

        (await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id)).IsVisited.Should().BeFalse();
    }

    [Fact]
    public async Task Null_IsVisited_leaves_flag_and_still_updates_dwell()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);
        stop.SetVisited(true);
        await fx.Db.SaveChangesAsync();

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, DwellMinutes: 90, null, IsVisited: null), CancellationToken.None);

        var reloaded = await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id);
        reloaded.IsVisited.Should().BeTrue();   // untouched — no regression
        reloaded.DwellMinutes.Should().Be(90);
    }
}
