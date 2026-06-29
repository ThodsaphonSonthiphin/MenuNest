using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class DeleteTripHandlerTests
{
    [Fact]
    public async Task Soft_deletes_owned_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteTripHandler(fx.Db, fx.UserProvisioner.Object);
        await handler.Handle(new DeleteTripCommand(trip.Id), CancellationToken.None);

        var reloaded = fx.Db.Trips.First(t => t.Id == trip.Id);
        reloaded.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Rejects_trip_not_owned()
    {
        using var fx = new HandlerTestFixture();
        var foreign = Trip.Create(Guid.NewGuid(), "t", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(foreign);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteTripHandler(fx.Db, fx.UserProvisioner.Object);

        await FluentActions
            .Awaiting(() => handler.Handle(new DeleteTripCommand(foreign.Id), CancellationToken.None).AsTask())
            .Should()
            .ThrowAsync<DomainException>();
    }
}
