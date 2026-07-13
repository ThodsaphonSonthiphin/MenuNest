using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class PlaceProfileRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public PlaceProfileRelationalTests()
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
    public async Task Profile_round_trips_best_time_and_review_links()
    {
        var profile = PlaceProfile.Create(_user.Id, "places/ChIJ1");
        profile.SetBestTime(new TimeOnly(16, 0), new TimeOnly(18, 30));
        profile.SetReviewLinks(new[] { ReviewLink.Create("https://youtu.be/abc", "clip") });
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var read = await _db.Set<PlaceProfile>().AsNoTracking().FirstAsync(p => p.Id == profile.Id);
        read.BestTimeStart.Should().Be(new TimeOnly(16, 0));
        read.ReviewLinks.Should().ContainSingle().Which.Url.Should().Be("https://youtu.be/abc");
    }

    [Fact]
    public async Task Unique_index_blocks_a_second_profile_for_the_same_user_and_place()
    {
        _db.Set<PlaceProfile>().Add(PlaceProfile.Create(_user.Id, "places/DUP"));
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfile>().Add(PlaceProfile.Create(_user.Id, "places/DUP"));
        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Deleting_a_profile_cascades_its_checklist_junction_but_not_the_library_item()
    {
        var item = ChecklistItem.Create(_user.Id, "umbrella");
        _db.ChecklistItems.Add(item);
        var profile = PlaceProfile.Create(_user.Id, "places/CJ");
        _db.Set<PlaceProfile>().Add(profile);
        await _db.SaveChangesAsync();
        _db.Set<PlaceProfileChecklistItem>().Add(PlaceProfileChecklistItem.Create(profile.Id, item.Id));
        await _db.SaveChangesAsync();

        _db.Set<PlaceProfile>().Remove(profile);
        await _db.SaveChangesAsync();

        (await _db.Set<PlaceProfileChecklistItem>().CountAsync()).Should().Be(0);
        (await _db.ChecklistItems.CountAsync(i => i.Id == item.Id)).Should().Be(1);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
