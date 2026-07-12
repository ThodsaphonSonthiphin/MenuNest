using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class TripPlaceReviewLinksPersistenceTests
{
    [Fact]
    public async Task ReviewLinks_round_trip_through_the_json_column()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        place.SetReviewLinks(new[]
        {
            ReviewLink.Create("https://www.tiktok.com/@u/video/1", "@foodie"),
            ReviewLink.Create("https://youtu.be/abc", null),
        });
        fx.Db.TripPlaces.Add(place);
        await fx.Db.SaveChangesAsync();

        // fresh context to force a real read back from storage
        fx.Db.ChangeTracker.Clear();
        var read = await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id);

        read.ReviewLinks.Should().HaveCount(2);
        read.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@u/video/1");
        read.ReviewLinks[0].Label.Should().Be("@foodie");
        read.ReviewLinks[1].Label.Should().BeNull();
    }

    [Fact]
    public async Task Empty_review_links_round_trip_as_empty()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        (await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id))
            .ReviewLinks.Should().BeEmpty();
    }
}
