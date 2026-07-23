using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class DailyEvergreenGuardTests
{
    [Fact]
    public async Task SetDayUseCurrentTime_false_is_refused_on_a_daily_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        var act = () => new SetDayUseCurrentTimeHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetDayUseCurrentTimeCommand(trip.Id, day.Id, false), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>();

        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue();
    }

    [Fact]
    public async Task SetDayUseCurrentTime_true_still_works_on_a_daily_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        await fx.Db.SaveChangesAsync();

        await new SetDayUseCurrentTimeHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetDayUseCurrentTimeCommand(trip.Id, day.Id, true), CancellationToken.None);

        (await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id)).UseCurrentTimeAsStart.Should().BeTrue();
    }
}