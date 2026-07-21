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

    [Fact]
    public async Task Cross_day_target_shifts_whole_trip_without_unique_collision()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 7, 12), 3, TravelMode.Drive);
        _db.Trips.Add(trip);
        var day0 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 12), new TimeOnly(9, 0));
        var day1 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 13), new TimeOnly(9, 0));
        var day2 = ItineraryDay.Create(trip.Id, new DateOnly(2026, 7, 14), new TimeOnly(9, 0));
        _db.ItineraryDays.AddRange(day0, day1, day2);
        var pA = TripPlace.Create(trip.Id, "A", 13.75, 100.50, PlaceCategory.See);
        _db.TripPlaces.Add(pA);
        var anchor = Stop.Create(day0.Id, pA.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(anchor);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var handler = new RetimeStopToHourHandler(_db, _users.Object, new RetimeStopToHourValidator());
        // target on Jul 13 => deltaDays=+1 => StartDate Jul13, days realign to 13/14/15
        var result = await handler.Handle(
            new RetimeStopToHourCommand(trip.Id, day0.Id, anchor.Id, new TimeOnly(10, 45), new DateOnly(2026, 7, 13)),
            CancellationToken.None);

        result.MovedTrip.Should().BeTrue();
        result.TripStartBefore.Should().Be(new DateOnly(2026, 7, 12));
        result.TripStartAfter.Should().Be(new DateOnly(2026, 7, 13));
        (await _db.Trips.FirstAsync(t => t.Id == trip.Id)).StartDate.Should().Be(new DateOnly(2026, 7, 13));
        var dates = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync();
        dates.Should().Equal(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 15));
        var anchorDay = await _db.ItineraryDays.OrderBy(d => d.Date).FirstAsync(); // was day0, now Jul 13
        anchorDay.DayStartTime.Should().Be(new TimeOnly(10, 45));
        anchorDay.UseCurrentTimeAsStart.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
