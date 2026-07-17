using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// Verifies <see cref="PlaceProfileSync"/> carries <see cref="TripPlace.SeasonPeriods"/> /
/// <see cref="PlaceProfile.SeasonPeriods"/> through the full master lifecycle: push-to-master
/// (<see cref="PlaceProfileSync.UpsertFromAsync"/>) copies season onto the profile, and a later
/// capture (<see cref="PlaceProfileSync.SeedIntoAsync"/>) copies it back onto a fresh place.
/// </summary>
public sealed class PlaceProfileSeasonLifecycleTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public PlaceProfileSeasonLifecycleTests()
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

    [Fact]
    public async Task Upsert_copies_season_into_master_then_seed_copies_it_back()
    {
        var t1 = Trip.Create(_user.Id, "T1", new DateOnly(2026, 7, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t1);
        var p1 = TripPlace.Create(t1.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        p1.SetSeasonPeriods(new[] { SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6 }, "ฝน") });
        _db.TripPlaces.Add(p1);
        await _db.SaveChangesAsync();

        await PlaceProfileSync.UpsertFromAsync(_db, _user.Id, p1, default); // push-to-master
        await _db.SaveChangesAsync();
        var master = await _db.PlaceProfiles.SingleAsync(p => p.UserId == _user.Id && p.GooglePlaceId == "gp1");
        master.SeasonPeriods.Should().HaveCount(1);
        master.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Bad);

        var t2 = Trip.Create(_user.Id, "T2", new DateOnly(2026, 8, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t2);
        var p2 = TripPlace.Create(t2.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        _db.TripPlaces.Add(p2);
        var seeded = await PlaceProfileSync.SeedIntoAsync(_db, _user.Id, p2, default); // capture seed
        seeded.Should().BeTrue();
        p2.SeasonPeriods.Should().HaveCount(1);
        p2.SeasonPeriods[0].Note.Should().Be("ฝน");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}