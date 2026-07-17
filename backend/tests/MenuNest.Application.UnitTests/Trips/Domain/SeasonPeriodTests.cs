using FluentAssertions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class SeasonPeriodTests
{
    [Fact]
    public void Create_dedupes_and_sorts_months_and_trims_note()
    {
        var p = SeasonPeriod.Create(SeasonKind.Bad, new[] { 9, 5, 9, 6 }, "  น้ำท่วม  ");
        p.Kind.Should().Be(SeasonKind.Bad);
        p.Months.Should().Equal(5, 6, 9);
        p.Note.Should().Be("น้ำท่วม");
    }

    [Fact]
    public void Create_blank_note_becomes_null()
    {
        SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, "   ").Note.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void Create_rejects_out_of_range_month(int bad)
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Good, new[] { bad }, null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_empty_months()
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Bad, System.Array.Empty<int>(), "x");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_too_long_note()
    {
        var act = () => SeasonPeriod.Create(SeasonKind.Good, new[] { 0 }, new string('x', 201));
        act.Should().Throw<DomainException>();
    }
}
