using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class TripDailyTests
{
    private static Trip OneDay() =>
        Trip.Create(Guid.NewGuid(), "Commute", new DateOnly(2026, 7, 23), 1, TravelMode.Drive);

    [Fact]
    public void New_trip_is_not_daily_by_default()
        => OneDay().IsDaily.Should().BeFalse();

    [Fact]
    public void SetDaily_true_on_single_day_sets_flag()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.IsDaily.Should().BeTrue();
    }

    [Fact]
    public void SetDaily_true_throws_when_multi_day()
    {
        var trip = Trip.Create(Guid.NewGuid(), "Trip", new DateOnly(2026, 7, 23), 3, TravelMode.Drive);
        var act = () => trip.SetDaily(true);
        act.Should().Throw<DomainException>();
        trip.IsDaily.Should().BeFalse("a rejected enable must not mutate the flag");
    }

    [Fact]
    public void SetDaily_false_is_always_allowed()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.SetDaily(false);
        trip.IsDaily.Should().BeFalse();
    }

    [Fact]
    public void Create_as_daily_with_multi_day_throws()
    {
        var act = () => Trip.Create(Guid.NewGuid(), "X", new DateOnly(2026, 7, 23), 2, TravelMode.Drive, null, isDaily: true);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_to_multi_day_throws_while_daily()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        var act = () => trip.Reschedule(new DateOnly(2026, 8, 1), 2);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_single_day_is_allowed_while_daily()
    {
        var trip = OneDay();
        trip.SetDaily(true);
        trip.Reschedule(new DateOnly(2026, 8, 1), 1);
        trip.StartDate.Should().Be(new DateOnly(2026, 8, 1));
    }
}