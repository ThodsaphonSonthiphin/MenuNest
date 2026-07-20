using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public sealed class TripPlaceNotesTests
{
    private static TripPlace New() => TripPlace.Create(Guid.NewGuid(), "P", 1, 2, PlaceCategory.See, "gp");

    [Fact]
    public void SetNotes_trims_stores_and_nulls_blank()
    {
        var p = New();
        p.SetNotes("  hi  ");
        p.Notes.Should().Be("hi");
        p.SetNotes("   ");
        p.Notes.Should().BeNull();
    }

    [Fact]
    public void SetNotes_rejects_over_2000_chars()
    {
        var p = New();
        var act = () => p.SetNotes(new string('a', 2001));
        act.Should().Throw<DomainException>();
    }
}
