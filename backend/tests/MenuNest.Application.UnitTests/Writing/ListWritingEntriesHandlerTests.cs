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
}
