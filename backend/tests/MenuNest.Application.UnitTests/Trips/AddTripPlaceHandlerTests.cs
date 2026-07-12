using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class AddTripPlaceHandlerTests
{
    [Fact]
    public async Task Adds_place_to_owned_trip()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        await fx.Db.SaveChangesAsync();

        var dto = await new AddTripPlaceHandler(fx.Db, fx.UserProvisioner.Object, new AddTripPlaceValidator())
            .Handle(new AddTripPlaceCommand(trip.Id, "Wat", 18.8, 98.9, PlaceCategory.See, "ChIJabc", null, 0, null, null),
                CancellationToken.None);

        dto.Name.Should().Be("Wat");
        fx.Db.TripPlaces.Should().ContainSingle(p => p.TripId == trip.Id);
    }

    [Fact]
    public async Task Rejects_place_on_trip_not_owned()
    {
        using var fx = new HandlerTestFixture();
        var foreign = Trip.Create(Guid.NewGuid(), "x", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(foreign);
        await fx.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => new AddTripPlaceHandler(fx.Db, fx.UserProvisioner.Object, new AddTripPlaceValidator())
            .Handle(new AddTripPlaceCommand(foreign.Id, "Wat", 0, 0, PlaceCategory.Other, null, null, null, null, null),
                CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public void ToDto_maps_review_links()
    {
        var place = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.Eat);
        place.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", "one") });
        var dto = AddTripPlaceHandler.ToDto(place);
        dto.ReviewLinks.Should().ContainSingle();
        dto.ReviewLinks[0].Url.Should().Be("https://x.com/1");
        dto.ReviewLinks[0].Label.Should().Be("one");
    }
}
