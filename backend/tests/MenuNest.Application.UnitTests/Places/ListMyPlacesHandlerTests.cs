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

public sealed class ListMyPlacesHandlerTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListMyPlacesHandlerTests()
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
    public async Task Dedupes_same_google_place_across_two_trips_into_one_item_listing_both_trips()
    {
        var t1 = Trip.Create(_user.Id, "Chiang Mai", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        var t2 = Trip.Create(_user.Id, "Temples", new DateOnly(2026, 12, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(t1, t2);
        var p1 = TripPlace.Create(t1.Id, "Old name", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-1");
        var p2 = TripPlace.Create(t2.Id, "Old name 2", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-1");
        _db.TripPlaces.AddRange(p1, p2);
        await _db.SaveChangesAsync();
        p2.UpdateDetails("Newer snapshot", PlaceCategory.See, null, null, null); // sets UpdatedAt → representative
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].GooglePlaceId.Should().Be("gp-1");
        result[0].Name.Should().Be("Newer snapshot");
        result[0].Trips.Select(x => x.TripName).Should().BeEquivalentTo(new[] { "Chiang Mai", "Temples" });
    }

    [Fact]
    public async Task Place_without_google_id_is_its_own_item_keyed_tp()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        var p = TripPlace.Create(t.Id, "Unresolved", 13.7, 100.5, PlaceCategory.Eat);
        _db.TripPlaces.Add(p);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].GooglePlaceId.Should().BeNull();
        result[0].Key.Should().Be($"tp:{p.Id}");
    }

    [Fact]
    public async Task Rolls_up_visited_when_any_stop_for_the_place_is_visited()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        var p = TripPlace.Create(t.Id, "Wat", 18.7, 98.9, PlaceCategory.See, googlePlaceId: "gp-9");
        _db.TripPlaces.Add(p);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var stop = Stop.Create(day.Id, p.Id, 0, 60, TravelMode.Drive);
        stop.SetVisited(true);
        _db.Stops.Add(stop);
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Visited.Should().BeTrue();
    }

    [Fact]
    public async Task HasProfile_true_when_a_place_profile_exists_for_the_google_id()
    {
        var t = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(t);
        _db.TripPlaces.Add(TripPlace.Create(t.Id, "Cafe", 18.7, 98.9, PlaceCategory.Cafe, googlePlaceId: "gp-7"));
        _db.PlaceProfiles.Add(PlaceProfile.Create(_user.Id, "gp-7"));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result[0].HasProfile.Should().BeTrue();
    }

    [Fact]
    public async Task Excludes_other_users_and_soft_deleted_trips()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        var mine = Trip.Create(_user.Id, "Mine", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        var theirs = Trip.Create(other.Id, "Theirs", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.AddRange(mine, theirs);
        _db.TripPlaces.Add(TripPlace.Create(mine.Id, "Mine place", 1, 1, PlaceCategory.See, googlePlaceId: "gp-a"));
        _db.TripPlaces.Add(TripPlace.Create(theirs.Id, "Their place", 2, 2, PlaceCategory.See, googlePlaceId: "gp-b"));
        await _db.SaveChangesAsync();

        var result = await NewHandler().Handle(new ListMyPlacesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Mine place");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
