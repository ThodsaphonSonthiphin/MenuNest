using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// Reschedule/realign tests on a *relational* provider (SQLite), unlike the
/// InMemory-backed <see cref="UpdateTripHandlerTests"/> which enforces no unique
/// index. The realign reassigns kept days onto dates their siblings may still hold
/// (e.g. nudging the start forward by fewer days than DayCount, or shrinking onto a
/// surplus day's date), so correctness depends on EF Core ordering the per-row
/// statements safely — highest target date first, drops before the updates that
/// reuse their dates — so the unique (TripId, Date) index is never transiently
/// violated. These lock in that invariant against a store that actually enforces
/// it; a regression (a raw bulk update, or splitting the realign across SaveChanges
/// the wrong way) would fail here with a UNIQUE constraint violation. This is the
/// path the inline TripDateEditor made reachable: nudging a multi-day trip's start.
/// </summary>
public sealed class UpdateTripHandlerRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;

    public UpdateTripHandlerRelationalTests()
    {
        // A private, in-memory SQLite DB that lives as long as the open connection.
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_conn)
            .Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();

        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();

        _users = new Mock<IUserProvisioner>();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
    }

    private UpdateTripHandler Build() => new(_db, _users.Object, new UpdateTripValidator());

    /// <summary>Seeds a trip with <paramref name="dayCount"/> contiguous days from
    /// <paramref name="start"/>, then detaches everything so the handler loads fresh.</summary>
    private Guid SeedTrip(DateOnly start, int dayCount)
    {
        var trip = Trip.Create(_user.Id, "Trip", start, dayCount, TravelMode.Drive);
        _db.Trips.Add(trip);
        for (var i = 0; i < dayCount; i++)
            _db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, start.AddDays(i)));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return trip.Id;
    }

    private async Task<List<DateOnly>> DayDatesAsync(Guid tripId) =>
        await _db.ItineraryDays.Where(d => d.TripId == tripId)
            .OrderBy(d => d.Date).Select(d => d.Date).ToListAsync();

    private UpdateTripCommand Cmd(Guid tripId, DateOnly start, int dayCount) =>
        new(tripId, "Trip", null, start, dayCount, TravelMode.Drive);

    [Fact]
    public async Task Forward_nudge_smaller_than_dayCount_realigns_without_unique_collision()
    {
        // 5-day trip Nov 14–18; nudge the start forward 1 day → target Nov 15–19,
        // which overlaps the current dates on Nov 15–18 — the most natural user action.
        // EF must order the updates highest-date-first so no intermediate duplicate arises.
        var trip = SeedTrip(new DateOnly(2026, 11, 14), 5);

        await Build().Handle(Cmd(trip, new DateOnly(2026, 11, 15), 5), CancellationToken.None);

        (await DayDatesAsync(trip)).Should().Equal(
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17),
            new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 19));
    }

    [Fact]
    public async Task Backward_nudge_realigns_without_collision()
    {
        var trip = SeedTrip(new DateOnly(2026, 11, 14), 5);

        await Build().Handle(Cmd(trip, new DateOnly(2026, 11, 12), 5), CancellationToken.None);

        (await DayDatesAsync(trip)).Should().Equal(
            new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 14),
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16));
    }

    [Fact]
    public async Task Shrink_with_forward_shift_realigns_onto_a_dropped_days_date()
    {
        // 5-day trip Nov 14–18 → start Nov 15, 3 days. A kept day's final date (Nov 17)
        // equals a *surplus* day's current date; EF must drop that day before the update.
        var trip = SeedTrip(new DateOnly(2026, 11, 14), 5);

        await Build().Handle(Cmd(trip, new DateOnly(2026, 11, 15), 3), CancellationToken.None);

        (await DayDatesAsync(trip)).Should().Equal(
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17));
    }

    [Fact]
    public async Task Extend_with_forward_shift_realigns_and_adds_trailing_days()
    {
        var trip = SeedTrip(new DateOnly(2026, 11, 14), 2);

        await Build().Handle(Cmd(trip, new DateOnly(2026, 11, 15), 4), CancellationToken.None);

        (await DayDatesAsync(trip)).Should().Equal(
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18));
    }

    [Fact]
    public async Task Same_date_reschedule_is_a_noop_on_day_dates()
    {
        var trip = SeedTrip(new DateOnly(2026, 11, 14), 3);

        await Build().Handle(Cmd(trip, new DateOnly(2026, 11, 14), 3), CancellationToken.None);

        (await DayDatesAsync(trip)).Should().Equal(
            new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
