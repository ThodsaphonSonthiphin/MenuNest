using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

/// <summary>
/// ADR-167's cascade, on a RELATIONAL context: StopChecklistEntry follows its Stop through a
/// database-level cascade the InMemory provider silently ignores, and Stop → TripPlace is
/// NoAction, so delete ORDER inside the one SaveChanges is load-bearing.
/// </summary>
public sealed class DeleteTripPlaceCascadeTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public DeleteTripPlaceCascadeTests()
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

    private DeleteTripPlaceHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new DeleteTripPlaceHandler(_db, users.Object);
    }

    private Trip NewTrip(string name = "Trip")
    {
        var t = Trip.Create(_user.Id, name, new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        _db.Trips.Add(t);
        return t;
    }

    [Fact]
    public async Task Without_cascade_a_scheduled_place_is_still_refused()
    {
        var t = NewTrip();
        var place = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(place);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await FluentActions
            .Awaiting(() => NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, place.Id), CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>().WithMessage("*ถูกจัดลงตาราง*");

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Cascade_removes_the_stop_and_closes_the_gap_it_left()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        var other1 = TripPlace.Create(t.Id, "Cafe", 12.9, 99.4, PlaceCategory.Eat);
        var other2 = TripPlace.Create(t.Id, "Museum", 13.0, 99.5, PlaceCategory.See);
        _db.TripPlaces.AddRange(target, other1, other2);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, other1.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(day.Id, target.Id, 1, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(day.Id, other2.Id, 2, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == target.Id).Should().BeFalse();
        _db.Stops.Any(s => s.TripPlaceId == target.Id).Should().BeFalse();
        _db.Stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence)
           .Select(s => s.Sequence).Should().BeEquivalentTo(new[] { 0, 1 }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Cascade_handles_a_place_scheduled_on_two_days_and_resequences_both()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        var other = TripPlace.Create(t.Id, "Cafe", 12.9, 99.4, PlaceCategory.Eat);
        _db.TripPlaces.AddRange(target, other);
        var d1 = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        var d2 = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 2));
        _db.ItineraryDays.AddRange(d1, d2);
        _db.Stops.Add(Stop.Create(d1.Id, target.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d1.Id, other.Id, 1, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d2.Id, target.Id, 0, 60, TravelMode.Drive));
        _db.Stops.Add(Stop.Create(d2.Id, other.Id, 1, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.Stops.Count().Should().Be(2);
        _db.Stops.Single(s => s.ItineraryDayId == d1.Id).Sequence.Should().Be(0);
        _db.Stops.Single(s => s.ItineraryDayId == d2.Id).Sequence.Should().Be(0);
    }

    [Fact]
    public async Task Cascade_takes_the_stops_checklist_entries_with_it()
    {
        var t = NewTrip();
        var target = TripPlace.Create(t.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(target);
        var day = ItineraryDay.Create(t.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        var stop = Stop.Create(day.Id, target.Id, 0, 60, TravelMode.Drive);
        _db.Stops.Add(stop);
        var item = ChecklistItem.Create(_user.Id, "พาสปอร์ต");
        _db.ChecklistItems.Add(item);
        _db.StopChecklistEntries.Add(StopChecklistEntry.Create(stop.Id, item.Id));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, target.Id, Cascade: true), CancellationToken.None);

        _db.StopChecklistEntries.Any(e => e.StopId == stop.Id).Should().BeFalse();
        _db.ChecklistItems.Any(i => i.Id == item.Id).Should().BeTrue(); // the library item survives
    }

    [Fact]
    public async Task Cascade_on_an_unscheduled_place_just_deletes_the_row()
    {
        var t = NewTrip();
        var place = TripPlace.Create(t.Id, "Museum", 13.0, 99.5, PlaceCategory.See);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t.Id, place.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Cascade_does_not_reach_another_users_trip()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        var theirs = Trip.Create(other.Id, "Theirs", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        _db.Trips.Add(theirs);
        var place = TripPlace.Create(theirs.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay);
        _db.TripPlaces.Add(place);
        await _db.SaveChangesAsync();

        await FluentActions
            .Awaiting(() => NewHandler().Handle(new DeleteTripPlaceCommand(theirs.Id, place.Id, Cascade: true), CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>().WithMessage("Trip not found.");

        _db.TripPlaces.Any(p => p.Id == place.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Cascade_leaves_another_trips_copy_of_the_same_place_alone()
    {
        var t1 = NewTrip("Kanchanaburi");
        var t2 = NewTrip("Japan");
        var p1 = TripPlace.Create(t1.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        var p2 = TripPlace.Create(t2.Id, "Hotel", 12.8, 99.3, PlaceCategory.Stay, googlePlaceId: "gp-h");
        _db.TripPlaces.AddRange(p1, p2);
        var day = ItineraryDay.Create(t2.Id, new DateOnly(2026, 11, 1));
        _db.ItineraryDays.Add(day);
        _db.Stops.Add(Stop.Create(day.Id, p2.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();

        await NewHandler().Handle(new DeleteTripPlaceCommand(t1.Id, p1.Id, Cascade: true), CancellationToken.None);

        _db.TripPlaces.Any(p => p.Id == p2.Id).Should().BeTrue();
        _db.Stops.Any(s => s.TripPlaceId == p2.Id).Should().BeTrue();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
