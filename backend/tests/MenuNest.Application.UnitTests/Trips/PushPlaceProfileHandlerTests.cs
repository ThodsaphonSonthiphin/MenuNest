using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PushPlaceProfileHandlerTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PushPlaceProfileHandlerTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(_trip);
        _db.SaveChanges();
    }

    private Mock<IUserProvisioner> Users()
    {
        var m = new Mock<IUserProvisioner>();
        m.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return m;
    }

    private Guid AddPlace(string? placeId)
    {
        var place = TripPlace.Create(_trip.Id, "P", 1, 2, PlaceCategory.See, placeId);
        _db.TripPlaces.Add(place);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return place.Id;
    }

    [Fact]
    public async Task Push_overwrites_the_master_with_the_current_trip_values()
    {
        var placeId = AddPlace("places/PUSH");
        // seed a stale master via a first edit (auto-create at 09:00)
        await new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator())
            .Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
                new TimeOnly(9, 0), new TimeOnly(10, 0), Array.Empty<ReviewLinkDto>()), default);
        // change this trip only (override) — master still 09:00
        await new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator())
            .Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
                new TimeOnly(20, 0), new TimeOnly(21, 0), Array.Empty<ReviewLinkDto>()), default);

        var dto = await new PushPlaceProfileHandler(_db, Users().Object)
            .Handle(new PushPlaceProfileCommand(_trip.Id, placeId), default);

        dto.HasProfile.Should().BeTrue();
        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/PUSH");
        profile.BestTimeStart.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task Push_on_a_place_without_a_google_place_id_throws()
    {
        var placeId = AddPlace(null);
        var act = () => new PushPlaceProfileHandler(_db, Users().Object)
            .Handle(new PushPlaceProfileCommand(_trip.Id, placeId), default).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}