using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileAutoCreateRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileAutoCreateRelationalTests()
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

    private (Guid placeId, Guid stopId) AddPlace(string? placeId)
    {
        var place = TripPlace.Create(_trip.Id, "P", 1, 2, PlaceCategory.See, placeId);
        _db.TripPlaces.Add(place);
        var day = ItineraryDay.Create(_trip.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(stop);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return (place.Id, stop.Id);
    }

    [Fact]
    public async Task First_save_with_enrichment_auto_creates_the_master()
    {
        var (placeId, stopId) = AddPlace("places/AC1");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new[] { new BestTimeWindowDto(new TimeOnly(9, 0), new TimeOnly(11, 0), null) }, Array.Empty<ReviewLinkDto>(), Array.Empty<SeasonPeriodDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstOrDefaultAsync(p => p.GooglePlaceId == "places/AC1");
        profile.Should().NotBeNull();
        profile!.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Subsequent_saves_update_the_master()
    {
        var (placeId, stopId) = AddPlace("places/AC2");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new[] { new BestTimeWindowDto(new TimeOnly(9, 0), new TimeOnly(11, 0), null) }, Array.Empty<ReviewLinkDto>(), Array.Empty<SeasonPeriodDto>()), default);
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new[] { new BestTimeWindowDto(new TimeOnly(14, 0), new TimeOnly(15, 0), null) }, Array.Empty<ReviewLinkDto>(), Array.Empty<SeasonPeriodDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/AC2");
        profile.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Save_with_no_place_id_creates_no_master()
    {
        var (placeId, _) = AddPlace(null);
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(new UpdateTripPlaceCommand(_trip.Id, placeId, "P", PlaceCategory.See, null, null, null,
            new[] { new BestTimeWindowDto(new TimeOnly(9, 0), new TimeOnly(11, 0), null) }, Array.Empty<ReviewLinkDto>(), Array.Empty<SeasonPeriodDto>()), default);
        (await _db.Set<PlaceProfile>().CountAsync()).Should().Be(0);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}