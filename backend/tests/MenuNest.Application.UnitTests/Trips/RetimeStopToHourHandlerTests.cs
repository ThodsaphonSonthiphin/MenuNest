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
    // fx.Clock is fixed at 2026-01-01 UTC; the past-date guard threshold is 2025-12-31 (today - 1 slack).
    private static RetimeStopToHourHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new RetimeStopToHourValidator(), fx.Clock);

    // Seeds a 1-day trip on `date` with a single anchor stop and returns (trip, day, stop).
    private static async Task<(Trip trip, ItineraryDay day, Stop stop)> SeedOneDayAsync(HandlerTestFixture fx, DateOnly date)
    {
        var trip = Trip.Create(fx.User.Id, "t", date, 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, date, new TimeOnly(9, 0));
        fx.Db.ItineraryDays.Add(day);
        var place = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        fx.Db.TripPlaces.Add(place);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(stop);
        await fx.Db.SaveChangesAsync();
        return (trip, day, stop);
    }

    [Fact]
    public async Task Same_day_sets_day_start_pins_the_day_and_leaves_the_date()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 7, 12), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        day.SetUseCurrentTimeAsStart(true);              // apply must turn this OFF (ADR-115)
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

    [Fact]
    public async Task Retiming_to_a_clearly_past_date_throws()
    {
        // Fix 1: a cross-day pick that resolves to a date well before "now" (here 2025-06-01,
        // vs fx.Clock 2026-01-01) must be rejected, never silently rewrite the trip into the past.
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneDayAsync(fx, new DateOnly(2026, 7, 12));

        await FluentActions.Awaiting(() => Build(fx).Handle(
                new RetimeStopToHourCommand(trip.Id, day.Id, stop.Id, new TimeOnly(10, 0), new DateOnly(2025, 6, 1)),
                CancellationToken.None).AsTask())
            .Should().ThrowAsync<MenuNest.Domain.Exceptions.DomainException>();

        // Nothing was mutated.
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 12));
    }

    [Fact]
    public async Task Retiming_to_a_future_cross_day_date_succeeds()
    {
        // A forward cross-day move to a clearly-future date is unaffected by the guard.
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneDayAsync(fx, new DateOnly(2026, 7, 12));

        var result = await Build(fx).Handle(
            new RetimeStopToHourCommand(trip.Id, day.Id, stop.Id, new TimeOnly(8, 0), new DateOnly(2026, 7, 14)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 14));
        (await fx.Db.ItineraryDays.FirstAsync(d => d.Id == day.Id)).Date.Should().Be(new DateOnly(2026, 7, 14));
    }

    [Fact]
    public async Task Retiming_to_yesterday_within_utc_slack_still_succeeds()
    {
        // Threshold is today - 1 (2025-12-31 for fx.Clock 2026-01-01): a viewer-local "today"
        // that is still UTC-yesterday must NOT be falsely rejected. 2025-12-31 is exactly the
        // boundary and is allowed (guard rejects only strictly earlier dates).
        using var fx = new HandlerTestFixture();
        var (trip, day, stop) = await SeedOneDayAsync(fx, new DateOnly(2026, 1, 3));

        var result = await Build(fx).Handle(
            new RetimeStopToHourCommand(trip.Id, day.Id, stop.Id, new TimeOnly(8, 0), new DateOnly(2025, 12, 31)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        (await fx.Db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2025, 12, 31));
    }
}
