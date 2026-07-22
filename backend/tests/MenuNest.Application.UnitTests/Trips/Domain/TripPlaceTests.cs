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
    public void SetBestTimeWindows_replaces_the_whole_list()
    {
        var place = TripPlace.Create(Guid.NewGuid(), "P", 0, 0, PlaceCategory.See);
        place.SetBestTimeWindows(new[]
        {
            BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), "แดดร่ม"),
            BestTimeWindow.Create(new TimeOnly(17, 0), new TimeOnly(19, 0), null),
        });
        place.BestTimeWindows.Should().HaveCount(2);
        place.SetBestTimeWindows(Array.Empty<BestTimeWindow>());
        place.BestTimeWindows.Should().BeEmpty();
    }

    [Fact]
    public void SetBestTimeWindows_rejects_more_than_6() =>
        FluentActions.Invoking(() => TripPlace.Create(Guid.NewGuid(), "P", 0, 0, PlaceCategory.See)
                .SetBestTimeWindows(Enumerable.Range(0, 7)
                    .Select(i => BestTimeWindow.Create(new TimeOnly(i, 0), new TimeOnly(i, 30), null))))
            .Should().Throw<DomainException>();

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