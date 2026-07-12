using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class StopTests
{
    [Fact]
    public void Day_defaults_start_to_9am()
    {
        var d = ItineraryDay.Create(Guid.NewGuid(), new DateOnly(2026, 11, 14));
        d.DayStartTime.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void Stop_rejects_non_positive_dwell() =>
        FluentActions.Invoking(() =>
            Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 0, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void SetDwell_updates_minutes()
    {
        var s = Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 60, TravelMode.Walk);
        s.SetDwell(90);
        s.DwellMinutes.Should().Be(90);
    }

    [Fact]
    public void SetVisited_toggles_flag_and_defaults_false()
    {
        var s = Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 60, TravelMode.Drive);
        s.IsVisited.Should().BeFalse();      // new stop is never visited
        s.SetVisited(true);
        s.IsVisited.Should().BeTrue();
        s.SetVisited(false);
        s.IsVisited.Should().BeFalse();
    }
}
