using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Writing;

public class ListPendingWritingEntriesHandlerTests
{
    private static readonly DateTime CorrectedAt = new(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);

    private static void Correct(WritingEntry e) =>
        e.RecordCorrection(CorrectedAt, "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

    [Fact]
    public async Task Returns_only_entries_with_no_correction_yet()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var pending = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>pending night here</p>", 41);
        var corrected = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 15), "<p>corrected night here</p>", 420);
        Correct(corrected);
        fx.Db.WritingEntries.AddRange(pending, corrected);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(pending.Id);
    }

    [Fact]
    public async Task Carries_the_five_contract_fields_with_the_real_computed_wpm()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        // Mirrors the real prod row: 4 whitespace tokens over 41 seconds.
        var entry = WritingEntry.Create(
            fx.User.Id, new DateOnly(2026, 8, 16), "<p>[หนึ่ง สอง สาม passione]</p>", 41);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        var dto = result.Single();
        dto.Id.Should().Be(entry.Id);
        dto.Date.Should().Be(new DateOnly(2026, 8, 16));
        dto.Text.Should().Be("<p>[หนึ่ง สอง สาม passione]</p>");
        dto.ElapsedSeconds.Should().Be(41);
        dto.WordsPerMinute.Should().BeApproximately(4 / (41 / 60.0), 0.000001);
    }

    [Fact]
    public async Task Excludes_soft_deleted_entries_even_when_still_pending()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var deletedPending = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 14), "<p>deleted and pending</p>", 420);
        deletedPending.SoftDelete();
        fx.Db.WritingEntries.Add(deletedPending);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_other_users_pending_entries()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var other = User.CreateFromExternalLogin("other-oid", "other@example.com", "Other", AuthProvider.Microsoft);
        fx.Db.Users.Add(other);
        fx.Db.WritingEntries.Add(
            WritingEntry.Create(other.Id, new DateOnly(2026, 8, 16), "<p>not mine at all</p>", 420));
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Orders_newest_date_first_and_breaks_same_date_ties_by_creation_time()
    {
        // Regression: commit 5b4b56d added this tie-break to ListWritingEntries.
        // A re-implemented pending query must not lose it.
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var sameDate = new DateOnly(2026, 8, 16);
        var earlier = WritingEntry.Create(fx.User.Id, sameDate, "<p>earlier sitting today</p>", 420);
        var later = WritingEntry.Create(fx.User.Id, sameDate, "<p>later sitting today</p>", 420);
        SetCreatedAt(earlier, new DateTime(2026, 8, 16, 20, 0, 0, DateTimeKind.Utc));
        SetCreatedAt(later, new DateTime(2026, 8, 16, 22, 0, 0, DateTimeKind.Utc));
        var older = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 10), "<p>an older night here</p>", 420);

        fx.Db.WritingEntries.AddRange(earlier, later, older);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Select(r => r.Id).Should().ContainInOrder(later.Id, earlier.Id, older.Id);
    }

    // CreatedAt is set by the Entity base class; reflection is how the existing
    // ListWritingEntriesHandlerTests controls it (see its `using System.Reflection`).
    private static void SetCreatedAt(WritingEntry entry, DateTime value) =>
        typeof(MenuNest.Domain.Common.Entity)
            .GetProperty(nameof(MenuNest.Domain.Common.Entity.CreatedAt))!
            .SetValue(entry, value);
}
