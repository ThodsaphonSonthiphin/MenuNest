using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// Persistence tests for <see cref="TripPlace.SeasonPeriods"/> and
/// <see cref="PlaceProfile.SeasonPeriods"/> on a *relational* provider (SQLite), mirroring
/// <see cref="TripPlaceReviewLinksRelationalTests"/> so the real
/// <c>TripPlaceConfiguration</c>/<c>PlaceProfileConfiguration</c> converter/comparer and the
/// actual <c>SeasonPeriodsJson</c> column mapping (NOT NULL, <c>HasDefaultValueSql("'[]'")</c>)
/// are exercised, not the InMemory mirror.
/// </summary>
public sealed class TripPlaceSeasonRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public TripPlaceSeasonRelationalTests()
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
    }

    [Fact]
    public async Task SeasonPeriods_round_trip_through_the_json_column()
    {
        var trip = Trip.Create(_user.Id, "T", new DateOnly(2026, 7, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "3000 โบก", 15.4, 105.4, PlaceCategory.See, googlePlaceId: "gp1");
        place.SetSeasonPeriods(new[]
        {
            SeasonPeriod.Create(SeasonKind.Bad, new[] { 5, 6, 7, 8, 9 }, "น้ำท่วมหน้าฝน"),
            SeasonPeriod.Create(SeasonKind.Good, new[] { 10, 11, 0, 1 }, "อากาศเย็น"),
        });
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.TripPlaces.AsNoTracking().SingleAsync(p => p.Id == place.Id);
        loaded.SeasonPeriods.Should().HaveCount(2);
        loaded.SeasonPeriods[0].Note.Should().Be("น้ำท่วมหน้าฝน");
        loaded.SeasonPeriods[0].Months.Should().Equal(5, 6, 7, 8, 9);
        loaded.SeasonPeriods[1].Kind.Should().Be(SeasonKind.Good);
    }

    [Fact]
    public async Task Zero_season_periods_saves_without_a_NOT_NULL_violation()
    {
        var trip = Trip.Create(_user.Id, "T", new DateOnly(2026, 7, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.Eat);
        _db.TripPlaces.Add(place);

        var act = () => _db.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        _db.ChangeTracker.Clear();
        var read = await _db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == place.Id);
        read.SeasonPeriods.Should().NotBeNull();
        read.SeasonPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaceProfile_SeasonPeriods_round_trip_through_the_json_column()
    {
        var profile = PlaceProfile.Create(_user.Id, "gp2");
        profile.SetSeasonPeriods(new[]
        {
            SeasonPeriod.Create(SeasonKind.Good, new[] { 0, 1, 2 }, "high season"),
        });
        _db.PlaceProfiles.Add(profile);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.PlaceProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id);
        loaded.SeasonPeriods.Should().HaveCount(1);
        loaded.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Good);
        loaded.SeasonPeriods[0].Months.Should().Equal(0, 1, 2);
        loaded.SeasonPeriods[0].Note.Should().Be("high season");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
