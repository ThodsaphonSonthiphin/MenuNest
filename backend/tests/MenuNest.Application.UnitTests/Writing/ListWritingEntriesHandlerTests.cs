using System.Reflection;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.ListWritingEntries;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Writing;

public class ListWritingEntriesHandlerTests
{
    [Fact]
    public async Task Returns_only_current_users_non_deleted_entries_newest_first()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var older = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 10), "<p>older entry today</p>", 420);
        var newer = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 15), "<p>newer entry today</p>", 420);
        var deleted = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 14), "<p>deleted entry today</p>", 420);
        deleted.SoftDelete();

        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var othersEntry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), "<p>not mine today</p>", 420);

        fx.Db.WritingEntries.AddRange(older, newer, deleted, othersEntry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListWritingEntriesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(newer.Id);
        result[1].Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task Same_date_entries_are_ordered_by_creation_time_newest_first()
    {
        // No uniqueness decision was made for (UserId, Date) — a return-night
        // rewrite or a second sitting should not be silently blocked. This test
        // verifies that when two entries share the same Date, they are ordered
        // by CreatedAt (most recent first) to ensure deterministic ordering on
        // the History screen (pending-correction-visibility).
        using var fx = new HandlerTestFixture();
        var handler = new ListWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var sameDate = new DateOnly(2026, 8, 15);
        var olderCreated = WritingEntry.Create(fx.User.Id, sameDate, "<p>created first</p>", 420);
        var newerCreated = WritingEntry.Create(fx.User.Id, sameDate, "<p>created second</p>", 420);

        // Use reflection to set distinct CreatedAt times (since Create() initializes both to DateTime.UtcNow)
        var createdAtProperty = typeof(WritingEntry).BaseType!.GetProperty("CreatedAt", BindingFlags.Public | BindingFlags.Instance)!;
        var olderTime = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
        var newerTime = new DateTime(2026, 8, 15, 10, 0, 1, DateTimeKind.Utc);
        createdAtProperty.SetValue(olderCreated, olderTime);
        createdAtProperty.SetValue(newerCreated, newerTime);

        fx.Db.WritingEntries.AddRange(olderCreated, newerCreated);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListWritingEntriesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(newerCreated.Id, "newer entry created at 10:00:01 should come first");
        result[0].CreatedAt.Should().Be(newerTime);
        result[1].Id.Should().Be(olderCreated.Id, "older entry created at 10:00:00 should come second");
        result[1].CreatedAt.Should().Be(olderTime);
    }
}
