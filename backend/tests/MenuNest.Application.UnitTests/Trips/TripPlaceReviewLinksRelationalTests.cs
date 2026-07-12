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
/// Persistence tests for <see cref="TripPlace.ReviewLinks"/> on a *relational* provider
/// (SQLite), unlike the InMemory-backed <see cref="TripPlaceReviewLinksPersistenceTests"/>
/// which never applies the real <c>TripPlaceConfiguration</c> and does not enforce column
/// nullability. <see cref="SqliteAppDbContext"/> calls <c>ApplyConfigurationsFromAssembly</c>,
/// so the actual production converter/comparer and the actual <c>ReviewLinksJson</c> column
/// mapping (NOT NULL, <c>HasDefaultValueSql("'[]'")</c>) are exercised under a store that
/// enforces NOT NULL.
///
/// This is the regression guard for the "ReviewLinks reads back as null" bug: EF Core never
/// passes a database NULL through a value converter's "from provider" lambda — "A null in a
/// database column is always a null in the entity instance, and vice-versa"
/// (learn.microsoft.com/ef/core/modeling/value-conversions). The original converter's
/// <c>ConvertToProvider</c> serialized an empty list to a literal SQL <c>NULL</c>
/// (<c>v.Count == 0 ? null : ...</c>), and the column was nullable, so a
/// <see cref="TripPlace"/> with zero review links read back with <c>ReviewLinks == null</c>
/// (not an empty list) — a latent <see cref="NullReferenceException"/> waiting for any
/// caller that does <c>place.ReviewLinks.Count</c>/<c>.Any()</c>/<c>.Select(...)</c>. The fix
/// makes the converter always serialize to a non-null string (empty list → <c>"[]"</c>) and
/// makes the column NOT NULL with <c>defaultValueSql: "'[]'"</c>, so a database NULL can
/// never occur in the first place — <see cref="Zero_review_links_saves_without_a_NOT_NULL_violation"/>
/// asserts the round-tripped value is both non-null and empty.
/// </summary>
public sealed class TripPlaceReviewLinksRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public TripPlaceReviewLinksRelationalTests()
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

    private Guid SeedTripAndPlace(Action<TripPlace>? configurePlace = null)
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.Eat);
        configurePlace?.Invoke(place);
        _db.TripPlaces.Add(place);
        return place.Id;
    }

    [Fact]
    public async Task Zero_review_links_saves_without_a_NOT_NULL_violation()
    {
        // No SetReviewLinks call at all — the ReviewLinksJson column now serializes the
        // empty list to the literal string "[]" (never a SQL null), and the column itself
        // is NOT NULL with a "'[]'" default, so this save must not throw and the value
        // read back must never be a C# null.
        var placeId = SeedTripAndPlace();

        var act = () => _db.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        _db.ChangeTracker.Clear();
        var read = await _db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == placeId);

        // This is the regression guard for the null-reads-back bug: a TripPlace with zero
        // review links must read back as an empty list, NOT a null reference — asserting
        // "not null" first so a regression to the old nullable-column/null-emitting
        // converter fails loudly here instead of silently coalescing away.
        read.ReviewLinks.Should().NotBeNull();
        read.ReviewLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Populated_review_links_round_trip_through_the_real_converter()
    {
        var placeId = SeedTripAndPlace(place => place.SetReviewLinks(new[]
        {
            ReviewLink.Create("https://www.tiktok.com/@u/video/1", "@foodie"),
            ReviewLink.Create("https://youtu.be/abc", null),
        }));
        await _db.SaveChangesAsync();

        // fresh read to force a real deserialize through the production converter
        _db.ChangeTracker.Clear();
        var read = await _db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == placeId);

        read.ReviewLinks.Should().HaveCount(2);
        read.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@u/video/1");
        read.ReviewLinks[0].Label.Should().Be("@foodie");
        read.ReviewLinks[1].Url.Should().Be("https://youtu.be/abc");
        read.ReviewLinks[1].Label.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
