using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
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

    [Fact]
    public void SetBestTime_normalizes_partial_window_to_null()
    {
        var place = TripPlace.Create(Trip, "x", 0, 0, PlaceCategory.Other);

        // Partial window (start only) normalizes to null
        place.SetBestTime(new TimeOnly(8, 0), null);
        place.BestTimeStart.Should().BeNull();
        place.BestTimeEnd.Should().BeNull();

        // Partial window (end only) normalizes to null
        place.SetBestTime(null, new TimeOnly(10, 0));
        place.BestTimeStart.Should().BeNull();
        place.BestTimeEnd.Should().BeNull();

        // Complete window sets both
        place.SetBestTime(new TimeOnly(8, 0), new TimeOnly(10, 0));
        place.BestTimeStart.Should().Be(new TimeOnly(8, 0));
        place.BestTimeEnd.Should().Be(new TimeOnly(10, 0));
    }

    [Fact]
    public void New_place_has_no_review_links()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public void SetReviewLinks_replaces_the_whole_list()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", "one") });
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/2", null), ReviewLink.Create("https://x.com/3", null) });
        p.ReviewLinks.Select(r => r.Url).Should().Equal("https://x.com/2", "https://x.com/3");
    }

    [Fact]
    public void SetReviewLinks_with_empty_clears_the_list()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        p.SetReviewLinks(new[] { ReviewLink.Create("https://x.com/1", null) });
        p.SetReviewLinks(Array.Empty<ReviewLink>());
        p.ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public void SetReviewLinks_rejects_more_than_ten()
    {
        var p = TripPlace.Create(Guid.NewGuid(), "A", 0, 0, PlaceCategory.See);
        var many = Enumerable.Range(0, 11).Select(i => ReviewLink.Create($"https://x.com/{i}", null));
        var act = () => p.SetReviewLinks(many);
        act.Should().Throw<DomainException>();
    }
}
