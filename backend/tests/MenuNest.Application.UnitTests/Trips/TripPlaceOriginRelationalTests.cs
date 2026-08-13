using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-156: OriginTripPlaceId is a nullable, opaque Guid — no FK, no index. A relational
/// round-trip is the only test that proves the property AND its EF mapping together.
/// </summary>
public sealed class TripPlaceOriginRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;

    public TripPlaceOriginRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    private Trip SeedTrip()
    {
        var user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(user);
        var trip = Trip.Create(user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        _db.SaveChanges();
        return trip;
    }

    [Fact]
    public async Task Origin_defaults_to_null_and_round_trips_a_value()
    {
        var trip = SeedTrip();
        var root = TripPlace.Create(trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See);
        var copy = TripPlace.Create(trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
            originTripPlaceId: root.Id);
        _db.TripPlaces.AddRange(root, copy);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var reloadedRoot = await _db.TripPlaces.SingleAsync(p => p.Id == root.Id);
        var reloadedCopy = await _db.TripPlaces.SingleAsync(p => p.Id == copy.Id);

        reloadedRoot.OriginTripPlaceId.Should().BeNull("a fresh capture has no origin");
        reloadedCopy.OriginTripPlaceId.Should().Be(root.Id);
    }

    [Fact]
    public async Task Origin_may_reference_a_row_that_no_longer_exists()
    {
        // Opaque, not a foreign key: deletes are HARD, so a dangling value must persist and read back.
        var trip = SeedTrip();
        var vanished = Guid.NewGuid();
        var place = TripPlace.Create(trip.Id, "Orphan copy", 1, 2, PlaceCategory.Other,
            originTripPlaceId: vanished);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var reloaded = await _db.TripPlaces.SingleAsync(p => p.Id == place.Id);
        reloaded.OriginTripPlaceId.Should().Be(vanished);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
