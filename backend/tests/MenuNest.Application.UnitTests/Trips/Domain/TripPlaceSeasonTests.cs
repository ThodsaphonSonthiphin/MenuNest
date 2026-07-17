using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripPlaceSeasonTests
{
    private static TripPlace NewPlace() =>
        TripPlace.Create(System.Guid.NewGuid(), "3000 โบก", 15.4, 105.4, PlaceCategory.See);

    [Fact]
    public void SetSeasonPeriods_replaces_the_whole_list()
    {
        var p = NewPlace();
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6 }, "ฝน") });
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Good, new[] { 10, 11 }, "เย็น") });
        p.SeasonPeriods.Should().HaveCount(1);
        p.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Good);
    }

    [Fact]
    public void SetSeasonPeriods_empty_clears()
    {
        var p = NewPlace();
        p.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5 }, null) });
        p.SetSeasonPeriods(System.Array.Empty<SeasonPeriod>());
        p.SeasonPeriods.Should().BeEmpty();
    }

    [Fact]
    public void SetSeasonPeriods_rejects_over_cap()
    {
        var p = NewPlace();
        var many = Enumerable.Range(0, 13)
            .Select(_ => SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, null));
        var act = () => p.SetSeasonPeriods(many);
        act.Should().Throw<DomainException>();
    }
}
