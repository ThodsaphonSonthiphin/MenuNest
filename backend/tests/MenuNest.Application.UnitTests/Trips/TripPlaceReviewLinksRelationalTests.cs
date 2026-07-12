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
/// so the actual production converter/comparer and the actual <c>.IsRequired(false)</c> on
/// the <c>ReviewLinksJson</c> column are exercised under a store that enforces NOT NULL. A
/// regression that reverts <c>.IsRequired(false)</c> back to required must fail
/// <see cref="Zero_review_links_saves_without_a_NOT_NULL_violation"/> with a SQLite
/// constraint violation, since a <c>TripPlace</c> with no review links serializes the
/// column to a null value (see the converter's <c>v.Count == 0 ? null : ...</c> branch).
///
/// NOTE (found while writing this test): EF Core never passes a database NULL through a
/// value converter's "from provider" lambda — "A null in a database column is always a
/// null in the entity instance, and vice-versa" (learn.microsoft.com/ef/core/modeling/
/// value-conversions). So a round trip through the *real* converter leaves
/// <c>TripPlace.ReviewLinks</c> as an actual C# <c>null</c>, not an empty list, even though
/// the converter's own "to model" branch would return <c>new List&lt;ReviewLink&gt;()</c>
/// for an empty column if it were ever invoked. The InMemory look-alike converter in
/// <c>InMemoryAppDbContext</c> never surfaces this because it always serializes to
/// <c>"[]"</c> instead of emitting a literal null, so the provider value is never null on
/// read. This is a latent gap (not the finding this test file was written to close): any
/// caller that does <c>place.ReviewLinks.Count</c>/<c>.Any()</c>, or calls
/// <c>TripPlace.SetReviewLinks</c> (which does <c>_reviewLinks.Clear()</c>), against a
/// <see cref="TripPlace"/> freshly loaded from real SQL with zero saved review links would
/// hit a <see cref="NullReferenceException"/>. Nothing in Application/WebApi/McpServer
/// consumes <c>ReviewLinks</c> yet (grep confirms zero hits outside Domain/Infrastructure/
/// migrations), so this has not fired in practice — flagged here rather than silently
/// asserted around, since asserting "empty" would misstate what the store actually returns.
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
        // No SetReviewLinks call at all — the ReviewLinksJson column serializes to a
        // SQL null via the converter's `v.Count == 0 ? null : ...` branch. This is the
        // exact save that threw a NOT NULL constraint violation before ReviewLinksJson
        // was mapped with .IsRequired(false); it must NOT throw here. This is the
        // regression guard: a revert of .IsRequired(false) fails right here, at the
        // SaveChangesAsync call, before the round-trip assertion below is even reached.
        var placeId = SeedTripAndPlace();

        var act = () => _db.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        _db.ChangeTracker.Clear();
        var read = await _db.TripPlaces.AsNoTracking().FirstAsync(p => p.Id == placeId);

        // EF Core never runs a database NULL through a value converter (see the class
        // doc above), so the real converter's `null => new List<ReviewLink>()` branch is
        // unreachable in practice and ReviewLinks comes back as an actual null here, not
        // an empty list. Coalesce so this assertion states the true current behavior
        // rather than a false "empty list" claim.
        (read.ReviewLinks ?? new List<ReviewLink>()).Should().BeEmpty();
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
