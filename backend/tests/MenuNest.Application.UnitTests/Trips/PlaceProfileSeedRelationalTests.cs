using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileSeedRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Trip _trip;

    public PlaceProfileSeedRelationalTests()
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

    private AddTripPlaceHandler NewAdd()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new AddTripPlaceHandler(_db, users.Object, new AddTripPlaceValidator());
    }

    private async Task SeedProfile(string placeId)
    {
        var item = ChecklistItem.Create(_user.Id, "sunscreen");
        _db.ChecklistItems.Add(item);
        var profile = PlaceProfile.Create(_user.Id, placeId);
        profile.SetBestTimeWindows(new[] { BestTimeWindow.Create(new TimeOnly(16, 0), new TimeOnly(18, 0), null) });
        profile.SetReviewLinks(new[] { ReviewLink.Create("https://youtu.be/x", "clip") });
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfileChecklistItem>().Add(PlaceProfileChecklistItem.Create(profile.Id, item.Id));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Capture_seeds_best_time_reviews_and_checklist_from_an_existing_profile()
    {
        await SeedProfile("places/SEED");
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Beach", 1, 2, PlaceCategory.See, "places/SEED", null, null, null, null),
            default);

        dto.HasProfile.Should().BeTrue();
        dto.BestTimeWindows.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(16, 0));
        dto.ReviewLinks.Should().ContainSingle();
        dto.Checklist.Should().ContainSingle().Which.Name.Should().Be("sunscreen");
    }

    [Fact]
    public async Task Capture_without_a_profile_seeds_nothing_and_HasProfile_is_false()
    {
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "New", 1, 2, PlaceCategory.See, "places/NONE", null, null, null, null),
            default);
        dto.HasProfile.Should().BeFalse();
        dto.BestTimeWindows.Should().BeEmpty();
        dto.Checklist.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_without_a_google_place_id_never_seeds()
    {
        await SeedProfile("places/SEED2");
        var dto = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Manual", 1, 2, PlaceCategory.See, null, null, null, null, null),
            default);
        dto.HasProfile.Should().BeFalse();
        dto.Checklist.Should().BeEmpty();
    }

    [Fact]
    public async Task List_reports_HasProfile_true_only_for_places_with_a_profile()
    {
        await SeedProfile("places/HASP");
        await NewAdd().Handle(new AddTripPlaceCommand(_trip.Id, "WithP", 1, 2, PlaceCategory.See, "places/HASP", null, null, null, null), default);
        await NewAdd().Handle(new AddTripPlaceCommand(_trip.Id, "NoP", 1, 2, PlaceCategory.See, "places/OTHER", null, null, null, null), default);

        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var list = await new ListTripPlacesHandler(_db, users.Object).Handle(new ListTripPlacesQuery(_trip.Id), default);

        list.Single(p => p.Name == "WithP").HasProfile.Should().BeTrue();
        list.Single(p => p.Name == "NoP").HasProfile.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}