using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class DeleteTripPlaceHandlerTests
{
    [Fact]
    public async Task Throws_when_place_is_scheduled()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "Cafe", 18.80, 98.92, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        fx.Db.Stops.Add(Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive));
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteTripPlaceHandler(fx.Db, fx.UserProvisioner.Object);

        await FluentActions
            .Awaiting(() => handler.Handle(new DeleteTripPlaceCommand(trip.Id, place.Id), CancellationToken.None).AsTask())
            .Should()
            .ThrowAsync<DomainException>()
            .WithMessage("*ถูกจัดลงตาราง*");
    }

    [Fact]
    public async Task Deletes_place_with_no_scheduled_stop()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "Museum", 18.80, 98.92, PlaceCategory.See);
        fx.Db.TripPlaces.Add(place);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteTripPlaceHandler(fx.Db, fx.UserProvisioner.Object);
        await handler.Handle(new DeleteTripPlaceCommand(trip.Id, place.Id), CancellationToken.None);

        fx.Db.TripPlaces.Any(p => p.Id == place.Id).Should().BeFalse();
    }
}
