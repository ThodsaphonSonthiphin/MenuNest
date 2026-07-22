using FluentAssertions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class BestTimeWindowTests
{
    [Fact]
    public void Create_trims_blank_note_to_null()
    {
        var w = BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), "  ");
        w.Note.Should().BeNull();
        w.Start.Should().Be(new TimeOnly(6, 0));
        w.End.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void Create_rejects_end_not_after_start() =>
        FluentActions.Invoking(() => BestTimeWindow.Create(new TimeOnly(9, 0), new TimeOnly(9, 0), null))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_rejects_note_over_200_chars() =>
        FluentActions.Invoking(() => BestTimeWindow.Create(new TimeOnly(6, 0), new TimeOnly(9, 0), new string('x', 201)))
            .Should().Throw<DomainException>();

}