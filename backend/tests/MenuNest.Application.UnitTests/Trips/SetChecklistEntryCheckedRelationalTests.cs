using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class SetChecklistEntryCheckedRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;

    public SetChecklistEntryCheckedRelationalTests()
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
    public async Task Check_is_per_place_independent()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        _db.Trips.Add(trip);
        var placeA = TripPlace.Create(trip.Id, "Beach", 0, 0, PlaceCategory.See);
        var placeB = TripPlace.Create(trip.Id, "Market", 1, 1, PlaceCategory.Shop);
        _db.TripPlaces.AddRange(placeA, placeB);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entryA = PlaceChecklistEntry.Create(placeA.Id, item.Id);
        var entryB = PlaceChecklistEntry.Create(placeB.Id, item.Id);
        _db.PlaceChecklistEntries.AddRange(entryA, entryB);
        await _db.SaveChangesAsync();

        var handler = new SetChecklistEntryCheckedHandler(_db, _users.Object);

        var dto = await handler.Handle(new SetChecklistEntryCheckedCommand(trip.Id, placeA.Id, entryA.Id, true), CancellationToken.None);

        dto.IsChecked.Should().BeTrue();
        dto.Name.Should().Be("ร่ม");
        (await _db.PlaceChecklistEntries.FirstAsync(e => e.Id == entryA.Id)).IsChecked.Should().BeTrue();
        (await _db.PlaceChecklistEntries.FirstAsync(e => e.Id == entryB.Id)).IsChecked.Should().BeFalse();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}