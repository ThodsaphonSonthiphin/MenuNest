using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>ADR-156 §2/§4: the handler stores the origin key VERBATIM (no lookup, no
/// flattening) and applies the copied enrichment only when no PlaceProfile master did.</summary>
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
                ReviewLinks: new[] { new ReviewLinkDto("https://www.tiktok.com/@a/video/1", "clip") }),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == dto.Id);
        saved.Notes.Should().Be("shady 06:30-09:00");
        saved.ReviewLinks.Should().HaveCount(1);
        saved.ReviewLinks[0].Url.Should().Be("https://www.tiktok.com/@a/video/1");
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

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
