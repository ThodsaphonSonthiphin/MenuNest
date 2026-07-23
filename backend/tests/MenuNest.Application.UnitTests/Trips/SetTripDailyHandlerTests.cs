using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetTripDaily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class SetTripDailyHandlerTests
{
    private static (Trip trip, ItineraryDay day) SeedSingleDay(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23));
        fx.Db.Trips.Add(trip);
        fx.Db.ItineraryDays.Add(day);
        fx.Db.SaveChanges();
        return (trip, day);
    }

    [Fact]
    public async Task Enable_sets_flag_and_forces_day_current_time()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day) = SeedSingleDay(fx);

        var dto = await new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, true), CancellationToken.None);

        dto.IsDaily.Should().BeTrue();
        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_on_multi_day_trip_throws()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 7, 23), 3, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 3; i++) fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 23).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        var act = () => new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, true), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Disable_clears_flag_but_leaves_day_current_time_untouched()
    {
        using var fx = new HandlerTestFixture();
        var (trip, day) = SeedSingleDay(fx);
        day.SetUseCurrentTimeAsStart(true);
        trip.SetDaily(true);
        await fx.Db.SaveChangesAsync();

        var dto = await new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(trip.Id, false), CancellationToken.None);

        dto.IsDaily.Should().BeFalse();
        var reloaded = await fx.Db.ItineraryDays.SingleAsync(d => d.Id == day.Id);
        reloaded.UseCurrentTimeAsStart.Should().BeTrue("disable only unlocks; it does not force the day flag off");
    }

    [Fact]
    public async Task Cannot_set_daily_on_another_users_trip()
    {
        using var fx = new HandlerTestFixture();
        var other = Trip.Create(Guid.NewGuid(), "Other", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);
        fx.Db.Trips.Add(other);
        await fx.Db.SaveChangesAsync();

        var act = () => new SetTripDailyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new SetTripDailyCommand(other.Id, true), CancellationToken.None).AsTask();
        await FluentActions.Awaiting(act).Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");
    }
}