using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ListTripPlacesChecklistRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListTripPlacesChecklistRelationalTests()
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
    public async Task ListTripPlaces_embeds_checklist_entries_with_name_and_checked()
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

        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var handler = new ListTripPlacesHandler(_db, users.Object);

        var result = await handler.Handle(new ListTripPlacesQuery(trip.Id), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Checklist.Should().ContainSingle();
        result[0].Checklist[0].Name.Should().Be("ร่ม");
        result[0].Checklist[0].ChecklistItemId.Should().Be(item.Id);
        result[0].Checklist[0].IsChecked.Should().BeTrue();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}