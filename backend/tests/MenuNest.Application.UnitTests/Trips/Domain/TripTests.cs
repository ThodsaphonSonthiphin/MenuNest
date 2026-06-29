using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class TripTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 11, 14);

    [Fact]
    public void Create_sets_fields_and_defaults()
    {
        var t = Trip.Create(User, " เชียงใหม่ ", Start, 3, TravelMode.Drive, "Chiang Mai");
        t.UserId.Should().Be(User);
        t.Name.Should().Be("เชียงใหม่");           // trimmed
        t.Destination.Should().Be("Chiang Mai");
        t.StartDate.Should().Be(Start);
        t.DayCount.Should().Be(3);
        t.DefaultTravelMode.Should().Be(TravelMode.Drive);
        t.DeletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name) =>
        FluentActions.Invoking(() => Trip.Create(User, name, Start, 3, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_rejects_non_positive_day_count() =>
        FluentActions.Invoking(() => Trip.Create(User, "x", Start, 0, TravelMode.Drive))
            .Should().Throw<DomainException>();

    [Fact]
    public void Reschedule_updates_start_and_count()
    {
        var t = Trip.Create(User, "x", Start, 3, TravelMode.Drive);
        t.Reschedule(new DateOnly(2026, 12, 1), 5);
        t.StartDate.Should().Be(new DateOnly(2026, 12, 1));
        t.DayCount.Should().Be(5);
    }

    [Fact]
    public void SoftDelete_stamps_DeletedAt()
    {
        var t = Trip.Create(User, "x", Start, 3, TravelMode.Drive);
        t.SoftDelete();
        t.DeletedAt.Should().NotBeNull();
    }
}
