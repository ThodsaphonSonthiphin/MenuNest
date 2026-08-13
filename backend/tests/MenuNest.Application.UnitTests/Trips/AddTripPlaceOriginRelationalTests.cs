using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>ADR-156 §2/§4: the handler stores the origin key VERBATIM (no lookup, no
/// flattening) and applies the copied enrichment per-field -- only the fields the master
/// left empty, so a master that holds a value still wins.</summary>
public sealed class AddTripPlaceOriginRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users = new();
    private readonly IValidator<AddTripPlaceCommand> _validator = new AddTripPlaceValidator();
    private readonly Trip _trip;

    public AddTripPlaceOriginRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(_trip);
        _db.SaveChanges();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private AddTripPlaceHandler NewAdd() => new(_db, _users.Object, _validator);

    [Fact]
    public async Task Stores_the_origin_key_verbatim()
    {
        var root = Guid.NewGuid();
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                null, null, null, null, null, OriginTripPlaceId: root),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.OriginTripPlaceId.Should().Be(root, "the client already sent the ROOT; the handler must not resolve it");
    }

    [Fact]
    public async Task Copies_enrichment_when_there_is_no_master()
    {
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                null, null, null, null, null,
                Notes: "shady 06:30-09:00",
                ReviewLinks: new[] { new ReviewLinkDto("https://www.tiktok.com/@a/video/1", "clip") },
                BestTimeWindows: new[] { new BestTimeWindowDto(new TimeOnly(6, 30), new TimeOnly(9, 0), "cool") },
                SeasonPeriods: new[] { new SeasonPeriodDto(SeasonKind.Good, new[] { 10, 11 }, "dry season") }),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("shady 06:30-09:00");
        saved.ReviewLinks.Should().HaveCount(1);
        saved.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@a/video/1");
        saved.BestTimeWindows.Should().HaveCount(1);
        saved.BestTimeWindows[0].Start.Should().Be(new TimeOnly(6, 30));
        saved.BestTimeWindows[0].End.Should().Be(new TimeOnly(9, 0));
        saved.SeasonPeriods.Should().HaveCount(1);
        saved.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Good);
        saved.SeasonPeriods[0].Months.Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task Master_wins_over_the_copied_enrichment()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/MASTER");
        profile.SetNotes("from the master");
        _db.PlaceProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                "places/MASTER", null, null, null, null,
                Notes: "from the copy"),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("from the master", "SeedIntoAsync returned true, so the copy is not applied");
    }

    [Fact]
    public async Task Master_exists_but_empty_for_a_field_does_not_block_that_fields_copy()
    {
        // The master has notes but no best-time windows and no season periods -- entirely
        // ordinary, since those two are push-only (UpdateTripPlaceHandler never writes them
        // through). SeedIntoAsync still returns true because a master row exists.
        var profile = PlaceProfile.Create(_user.Id, "places/MASTER");
        profile.SetNotes("from the master");
        _db.PlaceProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                "places/MASTER", null, null, null, null,
                Notes: "from the copy",
                BestTimeWindows: new[] { new BestTimeWindowDto(new TimeOnly(6, 30), new TimeOnly(9, 0), "cool") },
                SeasonPeriods: new[] { new SeasonPeriodDto(SeasonKind.Good, new[] { 10, 11 }, "dry season") }),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("from the master", "the master DOES hold a note, so it stays canonical");
        saved.BestTimeWindows.Should().HaveCount(1, "the master's windows were empty, so the copy must fill them in");
        saved.BestTimeWindows[0].Start.Should().Be(new TimeOnly(6, 30));
        saved.SeasonPeriods.Should().HaveCount(1, "the master's seasons were empty, so the copy must fill them in");
        saved.SeasonPeriods[0].Kind.Should().Be(SeasonKind.Good);
    }

    [Fact]
    public async Task Master_that_holds_windows_still_wins_over_the_copy()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/MASTER");
        profile.SetBestTimeWindows(new[] { BestTimeWindow.Create(new TimeOnly(7, 0), new TimeOnly(8, 0), "master window") });
        _db.PlaceProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Viewpoint", 18.79, 98.96, PlaceCategory.See,
                "places/MASTER", null, null, null, null,
                BestTimeWindows: new[] { new BestTimeWindowDto(new TimeOnly(6, 30), new TimeOnly(9, 0), "from the copy") }),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.BestTimeWindows.Should().HaveCount(1);
        saved.BestTimeWindows[0].Start.Should().Be(new TimeOnly(7, 0), "the master holds a value, so it stays canonical");
        saved.BestTimeWindows[0].Note.Should().Be("master window");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
