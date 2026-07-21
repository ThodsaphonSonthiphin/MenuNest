using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class RetimeStopToHourRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<MenuNest.Application.Abstractions.IUserProvisioner> _users;
    // Fixed well before every seeded date below, so the Fix-1 past-date guard never trips here.
    private readonly FixedClock _clock = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public RetimeStopToHourRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:"); _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options); _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user); _db.SaveChanges();
        _users = new Mock<MenuNest.Application.Abstractions.IUserProvisioner>();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private RetimeStopToHourHandler Build()
        => new(_db, _users.Object, new RetimeStopToHourValidator(), _clock);

    // Seeds an N-day trip starting `start`, with a single anchor Stop on day index `anchorDayIndex`.
    // Clears the change tracker so the handler reloads from the store (exercising the real unique index).
    private (Trip trip, Guid[] dayIds, Stop anchor) SeedTrip(DateOnly start, int dayCount, int anchorDayIndex)
    {
        var trip = Trip.Create(_user.Id, "Trip", start, dayCount, TravelMode.Drive);
        _db.Trips.Add(trip);
        var days = new ItineraryDay[dayCount];
        for (var i = 0; i < dayCount; i++)
        {
            days[i] = ItineraryDay.Create(trip.Id, start.AddDays(i), new TimeOnly(9, 0));
            _db.ItineraryDays.Add(days[i]);
        }
        var place = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var anchor = Stop.Create(days[anchorDayIndex].Id, place.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(anchor);
        _db.SaveChanges();
        var dayIds = days.Select(d => d.Id).ToArray();
        _db.ChangeTracker.Clear();
        return (trip, dayIds, anchor);
    }

    private async Task<List<DateOnly>> DatesAsync(Guid tripId)
        => await _db.ItineraryDays.Where(d => d.TripId == tripId).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync();

    [Fact]
    public async Task Cross_day_target_shifts_whole_trip_without_unique_collision()
    {
        var (trip, dayIds, anchor) = SeedTrip(new DateOnly(2026, 7, 12), 3, anchorDayIndex: 0);
        // target on Jul 13 => deltaDays=+1 => StartDate Jul13, days realign to 13/14/15
        var result = await Build().Handle(
            new RetimeStopToHourCommand(trip.Id, dayIds[0], anchor.Id, new TimeOnly(10, 45), new DateOnly(2026, 7, 13)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.TripStartBefore.Should().Be(new DateOnly(2026, 7, 12));
        result.TripStartAfter.Should().Be(new DateOnly(2026, 7, 13));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 13));
        (await DatesAsync(trip.Id)).Should().Equal(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 15));
        var anchorDay = await _db.ItineraryDays.OrderBy(d => d.Date).FirstAsync(); // was day0, now Jul 13
        anchorDay.DayStartTime.Should().Be(new TimeOnly(10, 45));
        anchorDay.UseCurrentTimeAsStart.Should().BeFalse();
    }

    [Fact]
    public async Task Anchor_on_middle_day_forward_shift_realigns_all_days()
    {
        // 3-day trip (12/13/14); anchor on the MIDDLE day (index 1 = Jul 13), moved forward to Jul 15.
        // deltaDays=+2 => StartDate Jul14, days realign to 14/15/16; the anchor day lands on Jul 15.
        var (trip, dayIds, anchor) = SeedTrip(new DateOnly(2026, 7, 12), 3, anchorDayIndex: 1);

        var result = await Build().Handle(
            new RetimeStopToHourCommand(trip.Id, dayIds[1], anchor.Id, new TimeOnly(11, 0), new DateOnly(2026, 7, 15)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.AnchorDate.Should().Be(new DateOnly(2026, 7, 15));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 14));
        (await DatesAsync(trip.Id)).Should().Equal(new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 16));
        var anchorDay = await _db.ItineraryDays.FirstAsync(d => d.Id == dayIds[1]);
        anchorDay.Date.Should().Be(new DateOnly(2026, 7, 15));
        anchorDay.DayStartTime.Should().Be(new TimeOnly(11, 0));
        anchorDay.UseCurrentTimeAsStart.Should().BeFalse();
    }

    [Fact]
    public async Task Multiday_trip_anchor_on_last_day_forward_shift_realigns_all_days()
    {
        // 5-day trip (>3) starting Aug 1; anchor on the LAST day (index 4 = Aug 5), moved forward to Aug 8.
        // deltaDays=+3 => StartDate Aug4, days realign to 04/05/06/07/08; the anchor day lands on Aug 8.
        var (trip, dayIds, anchor) = SeedTrip(new DateOnly(2026, 8, 1), 5, anchorDayIndex: 4);

        var result = await Build().Handle(
            new RetimeStopToHourCommand(trip.Id, dayIds[4], anchor.Id, new TimeOnly(7, 30), new DateOnly(2026, 8, 8)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.AnchorDate.Should().Be(new DateOnly(2026, 8, 8));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 8, 4));
        (await DatesAsync(trip.Id)).Should().Equal(
            new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 8));
        var anchorDay = await _db.ItineraryDays.FirstAsync(d => d.Id == dayIds[4]);
        anchorDay.Date.Should().Be(new DateOnly(2026, 8, 8));
        anchorDay.DayStartTime.Should().Be(new TimeOnly(7, 30));
    }

    [Fact]
    public async Task Backward_shift_within_the_future_realigns_all_days()
    {
        // Backward shift that STAYS in the future (well after fx clock 2026-01-01): 3-day trip Sep 10/11/12,
        // anchor on last day (Sep 12) pulled back to Sep 11 => deltaDays=-1 => StartDate Sep9, days 09/10/11.
        var (trip, dayIds, anchor) = SeedTrip(new DateOnly(2026, 9, 10), 3, anchorDayIndex: 2);

        var result = await Build().Handle(
            new RetimeStopToHourCommand(trip.Id, dayIds[2], anchor.Id, new TimeOnly(9, 15), new DateOnly(2026, 9, 11)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.AnchorDate.Should().Be(new DateOnly(2026, 9, 11));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 9, 9));
        (await DatesAsync(trip.Id)).Should().Equal(new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 11));
        (await _db.ItineraryDays.FirstAsync(d => d.Id == dayIds[2])).Date.Should().Be(new DateOnly(2026, 9, 11));
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
