using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class CreateTripHandlerTests
{
    [Fact]
    public async Task Creates_trip_and_seeds_one_day_per_day_count()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());

        var dto = await handler.Handle(
            new CreateTripCommand("เชียงใหม่", "Chiang Mai", new DateOnly(2026, 11, 14), 3, TravelMode.Drive),
            CancellationToken.None);

        dto.DayCount.Should().Be(3);
        var trip = fx.Db.Trips.Single();
        trip.UserId.Should().Be(fx.User.Id);
        var days = await fx.Db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync();
        days.Should().HaveCount(3);
        days[0].Date.Should().Be(new DateOnly(2026, 11, 14));
        days[2].Date.Should().Be(new DateOnly(2026, 11, 16));
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateTripHandler(fx.Db, fx.UserProvisioner.Object, new CreateTripValidator());
        await FluentActions.Awaiting(() =>
            handler.Handle(
                new CreateTripCommand("  ", null, new DateOnly(2026, 11, 14), 3, TravelMode.Drive),
                CancellationToken.None).AsTask()
        ).Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
