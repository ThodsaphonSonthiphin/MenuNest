using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripPlaceTests
{
    private static readonly Guid Trip = Guid.NewGuid();

    [Fact]
    public void Create_sets_core_fields()
    {
        var p = TripPlace.Create(Trip, "วัดพระธาตุ", 18.80, 98.92, PlaceCategory.See, "places/ChIJxxx");
        p.TripId.Should().Be(Trip);
        p.Name.Should().Be("วัดพระธาตุ");
        p.Lat.Should().Be(18.80);
        p.Category.Should().Be(PlaceCategory.See);
        p.GooglePlaceId.Should().Be("places/ChIJxxx");
    }

    [Fact]
    public void Create_rejects_blank_name() =>
        FluentActions.Invoking(() => TripPlace.Create(Trip, "  ", 0, 0, PlaceCategory.Other))
            .Should().Throw<DomainException>();

    [Fact]
    public void SetBestTime_rejects_end_before_start() =>
        FluentActions.Invoking(() =>
            TripPlace.Create(Trip, "x", 0, 0, PlaceCategory.Other)
                .SetBestTime(new TimeOnly(18, 0), new TimeOnly(9, 0)))
            .Should().Throw<DomainException>();
}
