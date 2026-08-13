using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-149 §1: an exact place_id match is idempotent -- the existing row is returned, nothing
/// is inserted and nothing is merged. Relational, because the filtered unique index
/// (TripId, GooglePlaceId) WHERE GooglePlaceId IS NOT NULL is the backstop under this policy
/// and the InMemory provider ignores it.
/// </summary>
public sealed class AddTripPlaceIdempotencyRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users = new();
    private readonly IValidator<AddTripPlaceCommand> _validator = new AddTripPlaceValidator();
    private readonly Trip _trip;

    public AddTripPlaceIdempotencyRelationalTests()
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

    private AddTripPlaceCommand Cmd(string? gpid, string name = "Cafe") =>
        new(_trip.Id, name, 18.79, 98.96, PlaceCategory.Cafe, gpid, null, null, null, null);

    [Fact]
    public async Task Same_place_id_twice_returns_the_existing_row_and_inserts_nothing()
    {
        var first = await NewAdd().Handle(Cmd("places/ChIJabc"), default);
        var second = await NewAdd().Handle(Cmd("places/ChIJabc", "Cafe renamed"), default);

        second.Id.Should().Be(first.Id, "the existing row is returned, not a second one");
        (await _db.TripPlaces.CountAsync(p => p.TripId == _trip.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Nothing_is_merged_onto_the_existing_row()
    {
        var first = await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Cafe", 18.79, 98.96, PlaceCategory.Cafe,
                "places/ChIJabc", null, null, null, null, Notes: "original"),
            default);

        await NewAdd().Handle(
            new AddTripPlaceCommand(_trip.Id, "Cafe", 18.79, 98.96, PlaceCategory.Eat,
                "places/ChIJabc", null, null, null, null, Notes: "should be discarded"),
            default);

        _db.ChangeTracker.Clear();
        var saved = await _db.TripPlaces.SingleAsync(p => p.Id == first.Id);
        saved.Notes.Should().Be("original", "a capture is not an edit");
        saved.Category.Should().Be(PlaceCategory.Cafe);
    }

    [Fact]
    public async Task Two_place_id_less_captures_of_one_spot_remain_two_rows()
    {
        // ADR-149 §2: the filtered unique index excludes NULL place ids, deliberately -- the
        // 100 m proximity NOTICE is the only signal here, and it only warns.
        await NewAdd().Handle(Cmd(null, "Stall A"), default);
        await NewAdd().Handle(Cmd(null, "Stall B"), default);

        (await _db.TripPlaces.CountAsync(p => p.TripId == _trip.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Master_exists_the_idempotent_response_reports_HasProfile_true()
    {
        _db.PlaceProfiles.Add(PlaceProfile.Create(_user.Id, "places/ChIJabc"));
        await _db.SaveChangesAsync();

        await NewAdd().Handle(Cmd("places/ChIJabc"), default);
        var second = await NewAdd().Handle(Cmd("places/ChIJabc", "Cafe renamed"), default);

        second.HasProfile.Should().BeTrue("a master already exists for this place_id");
    }

    [Fact]
    public async Task No_master_the_idempotent_response_reports_HasProfile_false()
    {
        await NewAdd().Handle(Cmd("places/ChIJabc"), default);
        var second = await NewAdd().Handle(Cmd("places/ChIJabc", "Cafe renamed"), default);

        second.HasProfile.Should().BeFalse("no master was ever created for this place_id");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
