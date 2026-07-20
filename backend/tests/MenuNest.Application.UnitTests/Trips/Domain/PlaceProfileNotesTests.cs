using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public sealed class PlaceProfileNotesTests
{
    private static PlaceProfile New() => PlaceProfile.Create(Guid.NewGuid(), "places/x");

    [Fact]
    public void SetNotes_trims_and_stores()
    {
        var p = New();
        p.SetNotes("  จอดรถลานล่าง  ");
        p.Notes.Should().Be("จอดรถลานล่าง");
    }

    [Fact]
    public void SetNotes_blank_becomes_null()
    {
        var p = New();
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
