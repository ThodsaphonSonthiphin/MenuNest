using System.Data.Common;
using System.Linq;
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

    [Fact]
    public async Task ListTripPlaces_returns_checklist_in_deterministic_created_then_id_order()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var i1 = ChecklistItem.Create(_user.Id, "a"); var i2 = ChecklistItem.Create(_user.Id, "b"); var i3 = ChecklistItem.Create(_user.Id, "c");
        _db.ChecklistItems.AddRange(i1, i2, i3);
        var e1 = PlaceChecklistEntry.Create(place.Id, i1.Id);
        var e2 = PlaceChecklistEntry.Create(place.Id, i2.Id);
        var e3 = PlaceChecklistEntry.Create(place.Id, i3.Id);
        _db.PlaceChecklistEntries.AddRange(e1, e2, e3);
        await _db.SaveChangesAsync();
        var expected = new[] { e1, e2, e3 }.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => e.Id).ToList();

        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var handler = new ListTripPlacesHandler(_db, users.Object);

        var result = await handler.Handle(new ListTripPlacesQuery(trip.Id), CancellationToken.None);

        result[0].Checklist.Select(x => x.Id).Should().Equal(expected);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}