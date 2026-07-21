using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class RetimeStopToHourHandlerTests
{
    private static RetimeStopToHourHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new RetimeStopToHourValidator());

    [Fact]
    public async Task Same_day_sets_day_start_pins_the_day_and_leaves_the_date()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);              // apply must turn this OFF (ADR-110)
        fx.Db.ItineraryDays.Add(day);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        fx.Db.TripPlaces.Add(pA);
        var s0 = Stop.Create(day.Id, pA.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(s0);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new RetimeStopToHourCommand(trip.Id, day.Id, s0.Id, new TimeOnly(10, 45), new DateOnly(2026, 7, 12)),
            CancellationToken.None);

        var reloaded = await fx.Db.ItineraryDays.FirstAsync(d => d.Id == day.Id);
        reloaded.DayStartTime.Should().Be(new TimeOnly(10, 45));
        reloaded.UseCurrentTimeAsStart.Should().BeFalse();
        reloaded.Date.Should().Be(new DateOnly(2026, 7, 12));
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 12));
        result.MovedTrip.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_day_throws_not_found()
    {
        using var fx = new HandlerTestFixture();
        await FluentActions.Awaiting(() => Build(fx).Handle(
                new RetimeStopToHourCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new TimeOnly(9, 0), new DateOnly(2026, 7, 12)),
                CancellationToken.None).AsTask())
            .Should().ThrowAsync<MenuNest.Domain.Exceptions.DomainException>();
    }
}
