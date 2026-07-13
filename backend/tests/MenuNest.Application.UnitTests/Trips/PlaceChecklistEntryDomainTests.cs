using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceChecklistEntryDomainTests
{
    [Fact]
    public void Create_sets_fields_unchecked()
    {
        var placeId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var e = PlaceChecklistEntry.Create(placeId, itemId);
        e.TripPlaceId.Should().Be(placeId);
        e.ChecklistItemId.Should().Be(itemId);
        e.IsChecked.Should().BeFalse();
        e.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SetChecked_toggles()
    {
        var e = PlaceChecklistEntry.Create(Guid.NewGuid(), Guid.NewGuid());
        e.SetChecked(true);
        e.IsChecked.Should().BeTrue();
        e.SetChecked(false);
        e.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_empty_ids()
    {
        FluentActions.Invoking(() => PlaceChecklistEntry.Create(Guid.Empty, Guid.NewGuid())).Should().Throw<DomainException>();
        FluentActions.Invoking(() => PlaceChecklistEntry.Create(Guid.NewGuid(), Guid.Empty)).Should().Throw<DomainException>();
    }
}