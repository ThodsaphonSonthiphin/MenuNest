using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

/// <summary>ADR-156 §2/§3: two place_id-less rows sharing one root collapse to ONE Discover
/// card, and the DTO reports the already-flattened root so no chain can form.</summary>
public sealed class ListMyPlacesOriginGroupingTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListMyPlacesOriginGroupingTests()
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

    private ListMyPlacesHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new ListMyPlacesHandler(_db, users.Object);
    }

    [Fact]
    public async Task Two_trips_one_root_is_one_card_carrying_the_root()
    {
        var octTrip = Trip.Create(_user.Id, "Oct", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        var decTrip = Trip.Create(_user.Id, "Dec", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(octTrip, decTrip);

        var root = TripPlace.Create(octTrip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See);
        var copy = TripPlace.Create(decTrip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
            originTripPlaceId: root.Id);
        _db.TripPlaces.AddRange(root, copy);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), default);

        result.Should().HaveCount(1, "one physical place is one card");
        result[0].OriginTripPlaceId.Should().Be(root.Id);
        result[0].Trips.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_lone_capture_reports_its_own_id_as_the_root()
    {
        var trip = Trip.Create(_user.Id, "Solo", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "Stall", 13.75, 100.50, PlaceCategory.Eat);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), default);

        result.Should().HaveCount(1);
        result[0].OriginTripPlaceId.Should().Be(place.Id, "its own id IS the root");
    }

    [Fact]
    public async Task A_place_id_still_wins_over_the_origin_key()
    {
        // The column is inert for the common case: same place_id, two trips, still one card.
        var a = Trip.Create(_user.Id, "A", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        var b = Trip.Create(_user.Id, "B", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(a, b);
        _db.TripPlaces.Add(TripPlace.Create(a.Id, "Cafe", 1, 2, PlaceCategory.Cafe, "places/ChIJabc"));
        _db.TripPlaces.Add(TripPlace.Create(b.Id, "Cafe", 1, 2, PlaceCategory.Cafe, "places/ChIJabc"));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), default);
        result.Should().HaveCount(1);
        result[0].Trips.Should().HaveCount(2);
    }

    [Fact]
    public async Task Two_unrelated_place_id_less_rows_stay_two_cards()
    {
        var trip = Trip.Create(_user.Id, "T", new DateOnly(2026, 10, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        _db.TripPlaces.Add(TripPlace.Create(trip.Id, "Stall A", 1, 2, PlaceCategory.Eat));
        _db.TripPlaces.Add(TripPlace.Create(trip.Id, "Stall B", 1, 2, PlaceCategory.Eat));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), default);
        result.Should().HaveCount(2, "no shared root, so no grouping");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
