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

    private (Guid tripId, Guid StopId) Seed()
    {
        var trip = Trip.Create(_user.Id, "Trip", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var place = TripPlace.Create(trip.Id, "A place", 0, 0, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(stop);
        _db.SaveChanges();
        return (trip.Id, stop.Id);
    }

    private AttachChecklistItemHandler Handler() => new(_db, _users.Object, _validator);

    [Fact]
    public async Task New_name_creates_library_item_and_entry()
    {
        var (tripId, StopId) = Seed();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, " ร่ม "), CancellationToken.None);
        dto.Name.Should().Be("ร่ม");
        dto.IsChecked.Should().BeFalse();
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id && i.Name == "ร่ม")).Should().Be(1);
        (await _db.StopChecklistEntries.CountAsync(e => e.StopId == StopId)).Should().Be(1);
    }

    [Fact]
    public async Task Existing_name_is_reused_case_insensitively_no_duplicate_item()
    {
        var (tripId, StopId) = Seed();
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "Umbrella"));
        await _db.SaveChangesAsync();
        var dto = await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "umbrella"), CancellationToken.None);
        dto.Name.Should().Be("Umbrella");
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Attaching_same_item_twice_is_idempotent()
    {
        var (tripId, StopId) = Seed();
        var first = await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "ร่ม"), CancellationToken.None);
        var second = await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "ร่ม"), CancellationToken.None);
        second.Id.Should().Be(first.Id);
        (await _db.StopChecklistEntries.CountAsync(e => e.StopId == StopId)).Should().Be(1);
    }

    [Fact]
    public async Task Rejects_trip_not_owned()
    {
        var (_, StopId) = Seed();
        var stranger = new Mock<IUserProvisioner>();
        stranger.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateFromExternalLogin("oidX", "x@x.com", "X", AuthProvider.Microsoft));
        var handler = new AttachChecklistItemHandler(_db, stranger.Object, _validator);
        var act = () => handler.Handle(new AttachChecklistItemCommand(Guid.NewGuid(), StopId, "ร่ม"), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Attaching_beyond_max_per_place_throws()
    {
        var (tripId, StopId) = Seed();
        for (var i = 0; i < StopChecklistEntry.MaxPerStop; i++)
            await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, $"item {i}"), CancellationToken.None);

        var act = () => Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "one too many"), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<DomainException>();
        (await _db.StopChecklistEntries.CountAsync(e => e.StopId == StopId)).Should().Be(StopChecklistEntry.MaxPerStop);
    }

    [Fact]
    public async Task Whitespace_is_collapsed_and_reused()
    {
        var (tripId, StopId) = Seed();
        var first = await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "  ร่ม   ใหญ่  "), CancellationToken.None);
        first.Name.Should().Be("ร่ม ใหญ่");
        await Handler().Handle(new AttachChecklistItemCommand(tripId, StopId, "ร่ม ใหญ่"), CancellationToken.None);
        (await _db.ChecklistItems.CountAsync(i => i.UserId == _user.Id)).Should().Be(1);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
