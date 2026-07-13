using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ChecklistPersistenceRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ChecklistPersistenceRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Item_and_entry_round_trip()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var item = ChecklistItem.Create(_user.Id, "ร่ม");
        _db.ChecklistItems.Add(item);
        var entry = PlaceChecklistEntry.Create(place.Id, item.Id);
        entry.SetChecked(true);
        _db.PlaceChecklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var readItem = await _db.ChecklistItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
        var readEntry = await _db.PlaceChecklistEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id);
        readItem.Name.Should().Be("ร่ม");
        readItem.UserId.Should().Be(_user.Id);
        readEntry.TripPlaceId.Should().Be(place.Id);
        readEntry.ChecklistItemId.Should().Be(item.Id);
        readEntry.IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_item_name_for_same_user_violates_unique_index()
    {
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ร่ม"));
        await _db.SaveChangesAsync();
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ร่ม"));
        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}