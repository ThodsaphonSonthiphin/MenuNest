using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.DeleteWritingEntry;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class DeleteWritingEntryHandlerTests
{
    [Fact]
    public async Task Soft_deletes_the_entry()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>to be deleted today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        var stored = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Allows_deleting_an_already_corrected_locked_entry()
    {
        // entry-mutability (ADR-169): the lock only blocks edits, not deletion.
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>corrected today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();
        typeof(WritingEntry).GetProperty(nameof(WritingEntry.CorrectedAt))!
            .SetValue(entry, DateTime.UtcNow);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Throws_when_entry_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var entry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), "<p>not mine today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_entry_already_deleted()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>already gone today</p>", 420);
        entry.SoftDelete();
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
