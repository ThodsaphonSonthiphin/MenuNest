using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class UpdateTripPlaceHandlerTests
{
    private static (Trip trip, TripPlace place) Seed(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        fx.Db.SaveChanges();
        return (trip, place);
    }

    private static UpdateTripPlaceHandler Handler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new UpdateTripPlaceValidator());

    private static UpdateTripPlaceCommand Cmd(Guid tripId, Guid placeId, IReadOnlyList<ReviewLinkDto> links) =>
        new(tripId, placeId, "A", PlaceCategory.Eat, null, null, null, null, null, links);

    [Fact]
    public async Task Sets_review_links_full_replace()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);

        await Handler(fx).Handle(Cmd(trip.Id, place.Id, new[]
        {
            new ReviewLinkDto("https://www.tiktok.com/@u/1", "one"),
            new ReviewLinkDto("https://youtu.be/x", null),
        }), CancellationToken.None);

        fx.Db.ChangeTracker.Clear();
        var read = await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id);
        read.ReviewLinks.Select(r => r.Url).Should().Equal("https://www.tiktok.com/@u/1", "https://youtu.be/x");
    }

    [Fact]
    public async Task Empty_list_clears_existing_review_links()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        await Handler(fx).Handle(Cmd(trip.Id, place.Id, new[] { new ReviewLinkDto("https://x.com/1", null) }), CancellationToken.None);
        fx.Db.ChangeTracker.Clear();

        await Handler(fx).Handle(Cmd(trip.Id, place.Id, Array.Empty<ReviewLinkDto>()), CancellationToken.None);
        fx.Db.ChangeTracker.Clear();

        (await fx.Db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id)).ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_invalid_review_url()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        var act = () => Handler(fx).Handle(Cmd(trip.Id, place.Id, new[] { new ReviewLinkDto("not-a-url", null) }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Rejects_more_than_ten_links()
    {
        using var fx = new HandlerTestFixture();
        var (trip, place) = Seed(fx);
        var links = Enumerable.Range(0, 11).Select(i => new ReviewLinkDto($"https://x.com/{i}", null)).ToArray();
        var act = () => Handler(fx).Handle(Cmd(trip.Id, place.Id, links), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
