using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// Ordering cover for the Trips grid. Relational (SQLite) rather than InMemory so
/// the unsorted branch's <c>UpdatedAt ?? CreatedAt</c> is actually translated to SQL
/// instead of being evaluated in memory.
/// </summary>
public sealed class ListTripsOrderingTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListTripsOrderingTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    private ListTripsHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new ListTripsHandler(_db, users.Object);
    }

    /// <summary>
    /// CreatedAt/UpdatedAt have protected setters, so the timestamps are stamped
    /// through the change tracker — which bypasses accessibility.
    /// </summary>
    private Trip Seed(string name, DateOnly startDate, DateTime createdAt, DateTime? updatedAt)
    {
        var trip = Trip.Create(_user.Id, name, startDate, 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        _db.Entry(trip).Property(nameof(Trip.CreatedAt)).CurrentValue = createdAt;
        _db.Entry(trip).Property(nameof(Trip.UpdatedAt)).CurrentValue = updatedAt;
        return trip;
    }

    [Fact]
    public async Task Unsorted_lists_the_most_recently_modified_trip_first()
    {
        Seed("Edited long ago", new DateOnly(2026, 1, 1),
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            updatedAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Seed("Edited just now", new DateOnly(2026, 12, 1),
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            updatedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        Seed("Never edited", new DateOnly(2026, 6, 1),
            createdAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            updatedAt: null);
        await _db.SaveChangesAsync();

        var paged = await NewHandler().Handle(new ListTripsQuery(), CancellationToken.None);

        // A never-edited trip is ranked by its creation, so it sits between the two.
        paged.Result.Select(t => t.Name).Should()
            .ContainInOrder("Edited just now", "Never edited", "Edited long ago");
    }

    [Fact]
    public async Task Sorting_by_startDate_still_orders_by_the_trip_date()
    {
        // Creation order is deliberately the reverse of the start-date order, so a
        // regression back to the unsorted branch would flip the expectation.
        Seed("Third", new DateOnly(2026, 3, 1),
            createdAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), updatedAt: null);
        Seed("Second", new DateOnly(2026, 2, 1),
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), updatedAt: null);
        Seed("First", new DateOnly(2026, 1, 1),
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), updatedAt: null);
        await _db.SaveChangesAsync();

        var asc = await NewHandler().Handle(
            new ListTripsQuery(SortColumn: "startDate", SortDirection: "Ascending"), CancellationToken.None);
        asc.Result.Select(t => t.Name).Should().ContainInOrder("First", "Second", "Third");

        var desc = await NewHandler().Handle(
            new ListTripsQuery(SortColumn: "startDate", SortDirection: "Descending"), CancellationToken.None);
        desc.Result.Select(t => t.Name).Should().ContainInOrder("Third", "Second", "First");
    }

    [Theory]
    [InlineData("descending")]
    [InlineData("Descending")]
    public async Task A_descending_direction_is_honoured_whatever_its_casing(string direction)
    {
        Seed("Alpha", new DateOnly(2026, 1, 1),
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), updatedAt: null);
        Seed("Beta", new DateOnly(2026, 2, 1),
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), updatedAt: null);
        await _db.SaveChangesAsync();

        var paged = await NewHandler().Handle(
            new ListTripsQuery(SortColumn: "name", SortDirection: direction), CancellationToken.None);

        paged.Result.Select(t => t.Name).Should().ContainInOrder("Beta", "Alpha");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
