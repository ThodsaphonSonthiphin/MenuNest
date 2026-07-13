using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class DetachChecklistItemRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;

    public DetachChecklistItemRelationalTests()
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

    [Fact]
    public async Task Detach_removes_entry_but_keeps_library_item()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        var handler = new DetachChecklistItemHandler(_db, _users.Object);

        await handler.Handle(new DetachChecklistItemCommand(trip.Id, place.Id, entry.Id), CancellationToken.None);

        (await _db.PlaceChecklistEntries.AnyAsync(e => e.Id == entry.Id)).Should().BeFalse();
        (await _db.ChecklistItems.AnyAsync(i => i.Id == item.Id)).Should().BeTrue(); // library survives
    }

    [Fact]
    public async Task Rejects_trip_not_owned()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        var stranger = new Mock<IUserProvisioner>();
        stranger.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateFromExternalLogin("oidX", "x@x.com", "X", AuthProvider.Microsoft));
        var handler = new DetachChecklistItemHandler(_db, stranger.Object);

        var act = () => handler.Handle(new DetachChecklistItemCommand(trip.Id, place.Id, entry.Id), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();

        (await _db.PlaceChecklistEntries.AnyAsync(e => e.Id == entry.Id)).Should().BeTrue();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}