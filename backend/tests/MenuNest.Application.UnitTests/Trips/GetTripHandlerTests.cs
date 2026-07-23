using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.GetTrip;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class GetTripHandlerTests
{
    [Fact]
    public async Task Returns_the_current_users_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Chiang Mai", new DateOnly(2026, 11, 14), 3, TravelMode.Drive, "เชียงใหม่");
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var result = await new GetTripHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new GetTripQuery(trip.Id), CancellationToken.None);

        result.Id.Should().Be(trip.Id);
        result.Name.Should().Be("Chiang Mai");
        result.DayCount.Should().Be(3);
    }

    [Fact]
    public async Task Throws_when_trip_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var others = Trip.Create(Guid.NewGuid(), "Not mine", new DateOnly(2026, 11, 14), 2, TravelMode.Drive);
        fx.Db.Trips.Add(others);
        await fx.Db.SaveChangesAsync();

        var act = () => new GetTripHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new GetTripQuery(others.Id), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_trip_is_soft_deleted()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Gone", new DateOnly(2026, 11, 14), 2, TravelMode.Drive);
        trip.SoftDelete();
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var act = () => new GetTripHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new GetTripQuery(trip.Id), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task GetTrip_returns_IsDaily_false_for_a_normal_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 7, 23), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var dto = await new GetTripHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new GetTripQuery(trip.Id), CancellationToken.None);

        dto.IsDaily.Should().BeFalse();
    }
}
