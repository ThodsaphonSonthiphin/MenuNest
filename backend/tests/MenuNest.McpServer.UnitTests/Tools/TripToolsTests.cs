using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class TripToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly TripTools _sut;

    public TripToolsTests() => _sut = new TripTools(_mediator.Object);

    [Fact]
    public async Task push_place_profile_sends_PushPlaceProfileCommand_with_correct_ids()
    {
        var tripId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var expectedDto = new TripPlaceDto(
            Guid.NewGuid(), tripId, null, "Wat Arun",
            13.7437, 100.4888, null, PlaceCategory.See,
            null, null, null, null,
            null, null, null,
            new List<ReviewLinkDto>(),
            new List<PlaceChecklistEntryDto>(),
            true,
            new List<SeasonPeriodDto>());

        _mediator
            .Setup(m => m.Send(It.Is<PushPlaceProfileCommand>(c => c.TripId == tripId && c.PlaceId == placeId), It.IsAny<CancellationToken>()))
            .Returns<PushPlaceProfileCommand, CancellationToken>((_, _) => new ValueTask<TripPlaceDto>(expectedDto));

        var result = await _sut.push_place_profile(tripId, placeId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<PushPlaceProfileCommand>(c => c.TripId == tripId && c.PlaceId == placeId), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expectedDto);
    }
}