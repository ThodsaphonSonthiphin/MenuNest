using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileWriteThroughRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileWriteThroughRelationalTests()
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

    private UpdateTripPlaceCommand Cmd(Guid placeId, string? notes, ReviewLinkDto[] links, TimeOnly? bestStart = null, TimeOnly? bestEnd = null)
        => new(_trip.Id, placeId, "P", PlaceCategory.See, null, null, notes, bestStart, bestEnd, links, Array.Empty<SeasonPeriodDto>());

    [Fact]
    public async Task First_save_snapshots_note_into_the_new_master()
    {
        var placeId = AddPlace("places/W1");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "hello", Array.Empty<ReviewLinkDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/W1");
        profile.Notes.Should().Be("hello");
    }

    [Fact]
    public async Task Second_save_write_throughs_notes_and_reviewlinks_but_not_best_time()
    {
        var placeId = AddPlace("places/W2");
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "A",
            new[] { new ReviewLinkDto("https://tiktok.com/a", null) }, new TimeOnly(9, 0), new TimeOnly(11, 0)), default);
        await handler.Handle(Cmd(placeId, "B",
            new[] { new ReviewLinkDto("https://youtu.be/b", "clip") }, new TimeOnly(14, 0), new TimeOnly(15, 0)), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/W2");
        profile.Notes.Should().Be("B");                                   // write-through
        profile.ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://youtu.be/b"); // write-through
        profile.BestTimeStart.Should().Be(new TimeOnly(9, 0));            // push-only: unchanged from first save
    }

    [Fact]
    public async Task Save_with_no_place_id_creates_no_master()
    {
        var placeId = AddPlace(null);
        var handler = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        await handler.Handle(Cmd(placeId, "note", Array.Empty<ReviewLinkDto>()), default);
        (await _db.Set<PlaceProfile>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capture_seeds_note_from_an_existing_master()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/W3");
        profile.SetNotes("seeded note");
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var add = new AddTripPlaceHandler(_db, Users().Object, new AddTripPlaceValidator());
        // AddTripPlaceCommand order (verified): (TripId, Name, Lat, Lng, Category, GooglePlaceId, Address, PriceLevel, PhotoUrl, OpeningHoursJson)
        var dto = await add.Handle(new AddTripPlaceCommand(_trip.Id, "P", 1, 2, PlaceCategory.See, "places/W3", null, null, null, null), default);

        dto.Notes.Should().Be("seeded note");
    }

    // Documents the intentional last-write-wins semantics (ADR-103): update_trip_place is
    // full-replace, so a later save from ANY trip sharing the same google place overwrites the
    // shared master's Notes/ReviewLinks — even when that save's own note/links are empty. This
    // was accepted deliberately over a non-destructive (merge) variant.
    [Fact]
    public async Task Write_through_is_last_write_wins_across_trips()
    {
        var t2 = Trip.Create(_user.Id, "Trip B", new DateOnly(2026, 11, 2), 1, TravelMode.Drive);
        _db.Trips.Add(t2);
        await _db.SaveChangesAsync();
        var a = TripPlace.Create(_trip.Id, "P", 1, 2, PlaceCategory.See, "places/LW");
        var b = TripPlace.Create(t2.Id, "P", 1, 2, PlaceCategory.See, "places/LW");
        _db.TripPlaces.AddRange(a, b);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var h = new UpdateTripPlaceHandler(_db, Users().Object, new UpdateTripPlaceValidator());
        // Trip A enriches the shared master
        await h.Handle(new UpdateTripPlaceCommand(_trip.Id, a.Id, "P", PlaceCategory.See, null, null, "great view",
            null, null, new[] { new ReviewLinkDto("https://tiktok.com/a", null) }, Array.Empty<SeasonPeriodDto>()), default);
        // Trip B saves with empty note/links (e.g. only category changed) -> overwrites the shared master (accepted last-write-wins)
        await h.Handle(new UpdateTripPlaceCommand(t2.Id, b.Id, "P", PlaceCategory.See, null, null, null,
            null, null, Array.Empty<ReviewLinkDto>(), Array.Empty<SeasonPeriodDto>()), default);

        var profile = await _db.Set<PlaceProfile>().FirstAsync(p => p.GooglePlaceId == "places/LW");
        profile.Notes.Should().BeNull();
        profile.ReviewLinks.Should().BeEmpty();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
