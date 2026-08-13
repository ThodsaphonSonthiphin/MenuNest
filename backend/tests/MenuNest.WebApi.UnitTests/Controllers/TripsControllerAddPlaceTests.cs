using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Enums;
using MenuNest.WebApi.Controllers;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

/// <summary>
/// ADR-156 (#48): the wire-boundary gate that the final review found missing. System.Text.Json
/// silently drops unknown members at model binding, so a test that constructs an
/// AddTripPlaceCommand directly (as every pre-existing test does) proves nothing about the
/// controller's own mapping -- it must go through TripsController.AddPlace, from an AddPlaceBody
/// instance, and assert on the command the controller actually built.
/// </summary>
public sealed class TripsControllerAddPlaceTests
{
    [Fact]
    public async Task AddPlace_maps_all_fifteen_AddPlaceBody_members_onto_AddTripPlaceCommand()
    {
        var mediator = new Mock<IMediator>();
        AddTripPlaceCommand? captured = null;
        var responseDto = new TripPlaceDto(
            Guid.NewGuid(), Guid.NewGuid(), "ChIJ123", "Wat Arun",
            13.7437, 100.4888, "Addr", PlaceCategory.See,
            2, "photo.jpg", "{}", null, "shady 06:30-09:00",
            new List<ReviewLinkDto>(), false,
            new List<SeasonPeriodDto>(), new List<BestTimeWindowDto>());

        mediator
            .Setup(m => m.Send(It.IsAny<AddTripPlaceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<TripPlaceDto>, CancellationToken>((c, _) => captured = (AddTripPlaceCommand)c)
            .Returns<ICommand<TripPlaceDto>, CancellationToken>((_, _) => new ValueTask<TripPlaceDto>(responseDto));

        var controller = new TripsController(mediator.Object);

        var tripId = Guid.NewGuid();
        var originTripPlaceId = Guid.NewGuid();
        var reviewLinks = new List<ReviewLinkDto> { new("https://www.tiktok.com/@a/video/1", "clip") };
        var bestTimeWindows = new List<BestTimeWindowDto> { new(new TimeOnly(6, 30), new TimeOnly(9, 0), "cool") };
        var seasonPeriods = new List<SeasonPeriodDto> { new(SeasonKind.Good, new List<int> { 10, 11 }, "dry season") };

        var body = new AddPlaceBody(
            Name: "Wat Arun",
            Lat: 13.7437,
            Lng: 100.4888,
            Category: PlaceCategory.See,
            GooglePlaceId: "ChIJ123",
            Address: "Addr",
            PriceLevel: 2,
            PhotoUrl: "photo.jpg",
            OpeningHoursJson: "{}",
            OriginTripPlaceId: originTripPlaceId,
            Notes: "shady 06:30-09:00",
            ReviewLinks: reviewLinks,
            BestTimeWindows: bestTimeWindows,
            SeasonPeriods: seasonPeriods);

        await controller.AddPlace(tripId, body, CancellationToken.None);

        captured.Should().NotBeNull("the controller must send exactly one AddTripPlaceCommand");
        captured!.TripId.Should().Be(tripId);
        captured.Name.Should().Be("Wat Arun");
        captured.Lat.Should().Be(13.7437);
        captured.Lng.Should().Be(100.4888);
        captured.Category.Should().Be(PlaceCategory.See);
        captured.GooglePlaceId.Should().Be("ChIJ123");
        captured.Address.Should().Be("Addr");
        captured.PriceLevel.Should().Be(2);
        captured.PhotoUrl.Should().Be("photo.jpg");
        captured.OpeningHoursJson.Should().Be("{}");
        captured.OriginTripPlaceId.Should().Be(originTripPlaceId,
            "the origin key is what ADR-156 relies on to recognise the same physical place across Trips");
        captured.Notes.Should().Be("shady 06:30-09:00",
            "this is the #36 capture-form field that has been silently dropped at the wire since it was introduced");
        captured.ReviewLinks.Should().BeEquivalentTo(reviewLinks);
        captured.BestTimeWindows.Should().BeEquivalentTo(bestTimeWindows);
        captured.SeasonPeriods.Should().BeEquivalentTo(seasonPeriods);
    }

    [Fact]
    public async Task AddPlace_binds_when_the_five_new_members_are_omitted()
    {
        // The Trips add-form's existing payload sends only the original ten members -- the five
        // new ones must default, or that call site breaks.
        var mediator = new Mock<IMediator>();
        AddTripPlaceCommand? captured = null;
        var responseDto = new TripPlaceDto(
            Guid.NewGuid(), Guid.NewGuid(), null, "Wat Arun", 13.7, 100.4, null, PlaceCategory.See,
            null, null, null, null, null,
            new List<ReviewLinkDto>(), false,
            new List<SeasonPeriodDto>(), new List<BestTimeWindowDto>());

        mediator
            .Setup(m => m.Send(It.IsAny<AddTripPlaceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<TripPlaceDto>, CancellationToken>((c, _) => captured = (AddTripPlaceCommand)c)
            .Returns<ICommand<TripPlaceDto>, CancellationToken>((_, _) => new ValueTask<TripPlaceDto>(responseDto));

        var controller = new TripsController(mediator.Object);
        var tripId = Guid.NewGuid();
        var body = new AddPlaceBody("Wat Arun", 13.7, 100.4, PlaceCategory.See, null, null, null, null, null);

        await controller.AddPlace(tripId, body, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OriginTripPlaceId.Should().BeNull();
        captured.Notes.Should().BeNull();
        captured.ReviewLinks.Should().BeNull();
        captured.BestTimeWindows.Should().BeNull();
        captured.SeasonPeriods.Should().BeNull();
    }
}
