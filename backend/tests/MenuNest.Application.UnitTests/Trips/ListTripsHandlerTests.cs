using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class ListTripsHandlerTests
{
    [Fact]
    public async Task Returns_only_current_users_non_deleted_trips()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Trips.Add(Trip.Create(fx.User.Id, "Mine", new DateOnly(2026, 11, 1), 2, TravelMode.Drive));
        var others = Trip.Create(Guid.NewGuid(), "Other", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(others);
        var deleted = Trip.Create(fx.User.Id, "Gone", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        deleted.SoftDelete();
        fx.Db.Trips.Add(deleted);
        await fx.Db.SaveChangesAsync();

        var result = await new ListTripsHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        result.Should().ContainSingle(t => t.Name == "Mine");
    }
}
