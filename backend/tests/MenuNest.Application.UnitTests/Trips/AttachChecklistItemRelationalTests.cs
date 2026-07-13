using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class AttachChecklistItemRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;
    private readonly IValidator<AttachChecklistItemCommand> _validator = new AttachChecklistItemValidator();

    public AttachChecklistItemRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
        _users = new Mock<IUserProvisioner>();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private (Guid tripId, Guid placeId) Seed()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        _db.SaveChanges();
        return (trip.Id, place.Id);
    }

    private AttachChecklistItemHandler Handler() => new(_db, _users.Object, _validator);

    [Fact]
    public async Task New_name_creates_library_item_and_entry()
    {
        var (tripId, placeId) = Seed();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, " ร่ม "), CancellationToken.None);
        dto.Name.Should().Be("ร่ม");
        dto.IsChecked.Should().BeFalse();
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id && i.Name == "ร่ม")).Should().Be(1);
        (await _db.PlaceChecklistEntries.CountAsync(e => e.TripPlaceId == placeId)).Should().Be(1);
    }

    [Fact]
    public async Task Existing_name_is_reused_case_insensitively_no_duplicate_item()
    {
        var (tripId, placeId) = Seed();
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "Umbrella"));
        await _db.SaveChangesAsync();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "umbrella"), CancellationToken.None);
        dto.Name.Should().Be("Umbrella");
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Attaching_same_item_twice_is_idempotent()
    {
        var (tripId, placeId) = Seed();
        var first = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "ร่ม"), CancellationToken.None);
        var second = await Handler().Handle(new AttachChecklistItemCommand(tripId, placeId, "ร่ม"), CancellationToken.None);
        second.Id.Should().Be(first.Id);
        (await _db.PlaceChecklistEntries.CountAsync(e => e.TripPlaceId == placeId)).Should().Be(1);
    }

    [Fact]
    public async Task Rejects_trip_not_owned()
    {
        var (_, placeId) = Seed();
        var stranger = new Mock<IUserProvisioner>();
        stranger.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateFromExternalLogin("oidX", "x@x.com", "X", AuthProvider.Microsoft));
        var handler = new AttachChecklistItemHandler(_db, stranger.Object, _validator);
        var act = () => handler.Handle(new AttachChecklistItemCommand(Guid.NewGuid(), placeId, "ร่ม"), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}