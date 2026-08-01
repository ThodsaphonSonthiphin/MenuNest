using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class UpdateTripHandlerTests
{
    private static UpdateTripHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new UpdateTripValidator());

    [Fact]
    public async Task UpdateTrip_realigns_day_dates()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Original", new DateOnly(2026, 11, 1), 3, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 3; i++)
            fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        // Reschedule from Nov 1 (3 days) → Dec 10 (2 days)
        var cmd = new UpdateTripCommand(trip.Id, "Rescheduled", null,
            new DateOnly(2026, 12, 10), 2, TravelMode.Walk);
        var dto = await Build(fx).Handle(cmd, CancellationToken.None);

        dto.StartDate.Should().Be(new DateOnly(2026, 12, 10));
        dto.DayCount.Should().Be(2);

        var days = await fx.Db.ItineraryDays
            .Where(d => d.TripId == trip.Id)
            .OrderBy(d => d.Date)
            .ToListAsync();

        days.Should().HaveCount(2, "surplus day was removed");
        days[0].Date.Should().Be(new DateOnly(2026, 12, 10));
        days[1].Date.Should().Be(new DateOnly(2026, 12, 11));
    }

    [Fact]
    public async Task UpdateTrip_adds_trailing_days_when_extended()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Short", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 2; i++)
            fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        var cmd = new UpdateTripCommand(trip.Id, "Extended", null,
            new DateOnly(2026, 11, 5), 4, TravelMode.Drive);
        await Build(fx).Handle(cmd, CancellationToken.None);

        var days = await fx.Db.ItineraryDays
            .Where(d => d.TripId == trip.Id)
            .OrderBy(d => d.Date)
            .ToListAsync();

        days.Should().HaveCount(4);
        days[0].Date.Should().Be(new DateOnly(2026, 11, 5));
        days[3].Date.Should().Be(new DateOnly(2026, 11, 8));
    }

    [Fact]
    public async Task UpdateTrip_refuses_a_shrink_that_would_delete_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 11, 1), 3, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var days = new List<ItineraryDay>();
        for (var i = 0; i < 3; i++)
        {
            var d = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1).AddDays(i));
            days.Add(d);
            fx.Db.ItineraryDays.Add(d);
        }
        var place = TripPlace.Create(trip.Id, "Cafe", 18.80, 98.92, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        fx.Db.Stops.Add(Stop.Create(days[2].Id, place.Id, 0, 60, TravelMode.Drive)); // sits on day 3
        await fx.Db.SaveChangesAsync();

        // 3 -> 2 days: day 3 (and its one stop) would go.
        var cmd = new UpdateTripCommand(trip.Id, "Trip", null, new DateOnly(2026, 11, 1), 2, TravelMode.Drive);

        var thrown = await FluentActions
            .Awaiting(() => Build(fx).Handle(cmd, CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>();
        thrown.Which.Message.Should().Contain("1 scheduled stop").And.Contain("day(s) 3-3");

        // Nothing was persisted — the day and its stop are still there.
        (await fx.Db.ItineraryDays.CountAsync(d => d.TripId == trip.Id)).Should().Be(3);
        (await fx.Db.Stops.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateTrip_throws_when_trip_not_found()
    {
        using var fx = new HandlerTestFixture();
        var cmd = new UpdateTripCommand(Guid.NewGuid(), "X", null,
            new DateOnly(2026, 11, 1), 1, TravelMode.Drive);

        await FluentActions.Awaiting(() =>
            Build(fx).Handle(cmd, CancellationToken.None).AsTask()
        ).Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");
    }

    [Fact]
    public async Task UpdateTrip_cannot_see_another_users_trip()
    {
        using var fx = new HandlerTestFixture();
        var otherTrip = Trip.Create(Guid.NewGuid(), "Other", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(otherTrip);
        await fx.Db.SaveChangesAsync();

        var cmd = new UpdateTripCommand(otherTrip.Id, "Stolen", null,
            new DateOnly(2026, 11, 1), 1, TravelMode.Drive);

        await FluentActions.Awaiting(() =>
            Build(fx).Handle(cmd, CancellationToken.None).AsTask()
        ).Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");
    }
}
