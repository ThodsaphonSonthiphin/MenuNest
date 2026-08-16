using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class UpdateWritingEntryTextHandlerTests
{
    private static UpdateWritingEntryTextHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new UpdateWritingEntryTextValidator());

    [Fact]
    public async Task Updates_text_when_not_yet_corrected()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>original text today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>edited text today</p>"),
            CancellationToken.None);

        result.Text.Should().Be("<p>edited text today</p>");
        var stored = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        stored.Text.Should().Be("<p>edited text today</p>");
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

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>trying to edit today</p>"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_entry_is_soft_deleted()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>gone today</p>", 420);
        entry.SoftDelete();
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>trying to edit today</p>"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Validator_rejects_empty_text()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>original text today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, ""),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
