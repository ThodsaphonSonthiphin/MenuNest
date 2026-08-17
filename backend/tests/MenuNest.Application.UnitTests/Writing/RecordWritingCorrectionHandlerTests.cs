using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class RecordWritingCorrectionHandlerTests
{
    private static RecordWritingCorrectionHandler Handler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new RecordWritingCorrectionValidator(), fx.Clock);

    private static RecordWritingCorrectionCommand ACommand(
        Guid entryId,
        int hit = 0,
        int miss = 1,
        IReadOnlyList<SentenceCombiningItemDto>? items = null,
        IReadOnlyList<StuckWordDto>? stuck = null,
        string? markedText = null) =>
        new(
            EntryId: entryId,
            TargetRule: "third-person singular -s",
            MarkedText: markedText ?? "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            HitCount: hit,
            MissCount: miss,
            ThaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            SentenceCombiningItems: items ?? new List<SentenceCombiningItemDto>(),
            StuckWords: stuck ?? new List<StuckWordDto>());

    private static async Task<WritingEntry> SeedPending(HandlerTestFixture fx)
    {
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task Records_the_correction_and_stamps_CorrectedAt_from_the_clock()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc);
        var entry = await SeedPending(fx);

        var dto = await Handler(fx).Handle(ACommand(entry.Id), CancellationToken.None);

        dto.CorrectedAt.Should().Be(new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc));
        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.TargetRule.Should().Be("third-person singular -s");
        saved.HitCount.Should().Be(0);
        saved.MissCount.Should().Be(1);
        saved.MarkedText.Should().Contain("→ goes");
        saved.ThaiWhyLine.Should().Contain("เติม -s");
    }

    [Fact]
    public async Task Serialises_the_two_json_blocks_with_unescaped_thai()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        await Handler(fx).Handle(
            ACommand(
                entry.Id,
                items: new List<SentenceCombiningItemDto>
                {
                    new("Traffic is very bad. + We arrive late.", "Traffic was very bad, so we arrived late."),
                },
                stuck: new List<StuckWordDto>
                {
                    new("ข้าวต้ม", "rice porridge / congee"),
                    new("ห้าง", "shopping mall"),
                }),
            CancellationToken.None);

        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.SentenceCombiningItemsJson.Should().Contain("Traffic was very bad");
        // Codepoint-exact Thai, NOT \\u0E02-escaped.
        saved.StuckWordsJson.Should().Contain("ข้าวต้ม");
        saved.StuckWordsJson.Should().Contain("ห้าง");
        saved.StuckWordsJson.Should().NotContain("\\u0E");
        saved.StuckWordsJson.Should().NotContain("\\u0e");
    }

    [Fact]
    public async Task Accepts_a_thai_only_entry_with_zero_hits_and_zero_misses()
    {
        // The only real prod entry is Thai-only: no instance of an English rule
        // exists to hit or miss, and there are no English sentences to combine.
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(
            fx.User.Id, new DateOnly(2026, 8, 16), "<p>[หนึ่ง สอง สาม passione]</p>", 41);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var dto = await Handler(fx).Handle(
            ACommand(entry.Id, hit: 0, miss: 0, items: new List<SentenceCombiningItemDto>()),
            CancellationToken.None);

        dto.CorrectedAt.Should().NotBeNull();
        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.HitCount.Should().Be(0);
        saved.MissCount.Should().Be(0);
        saved.SentenceCombiningItemsJson.Should().Be("[]");
    }

    [Fact]
    public async Task A_second_correction_overwrites_the_first()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var entry = await SeedPending(fx);
        await Handler(fx).Handle(ACommand(entry.Id, hit: 0, miss: 3), CancellationToken.None);

        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        await Handler(fx).Handle(ACommand(entry.Id, hit: 1, miss: 2), CancellationToken.None);

        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.CorrectedAt.Should().Be(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));
        saved.HitCount.Should().Be(1);
        saved.MissCount.Should().Be(2);
    }

    [Fact]
    public async Task Refuses_an_unknown_entry_id_with_the_standard_message()
    {
        using var fx = new HandlerTestFixture();

        var act = async () => await Handler(fx).Handle(
            ACommand(Guid.NewGuid()), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_a_soft_deleted_entry()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);
        entry.SoftDelete();
        await fx.Db.SaveChangesAsync();

        var act = async () => await Handler(fx).Handle(ACommand(entry.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
        fx.Db.WritingEntries.Single(w => w.Id == entry.Id).CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_another_users_entry_with_the_same_not_found_message()
    {
        using var fx = new HandlerTestFixture();
        var other = User.CreateFromExternalLogin("other-oid", "other@example.com", "Other", AuthProvider.Microsoft);
        fx.Db.Users.Add(other);
        var theirs = WritingEntry.Create(other.Id, new DateOnly(2026, 8, 16), "<p>not mine at all</p>", 420);
        fx.Db.WritingEntries.Add(theirs);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Handler(fx).Handle(ACommand(theirs.Id), CancellationToken.None);

        // "not found", never "forbidden" — a forbidden would confirm the id exists.
        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
        fx.Db.WritingEntries.Single(w => w.Id == theirs.Id).CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_marked_text_over_50000_characters()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, markedText: new string('x', 50_001)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Accepts_a_marked_text_of_exactly_50000_characters()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, markedText: new string('x', 50_000)), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Rejects_more_than_four_sentence_combining_items()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);
        var five = Enumerable.Range(1, 5)
            .Select(i => new SentenceCombiningItemDto($"A{i}. + B{i}.", $"A{i} and B{i}."))
            .ToList();

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, items: five), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_negative_counts()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, miss: -1), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Accepts_exactly_four_sentence_combining_items()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);
        var four = Enumerable.Range(1, 4)
            .Select(i => new SentenceCombiningItemDto($"A{i}. + B{i}.", $"A{i} and B{i}."))
            .ToList();

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, items: four), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Rejects_a_null_sentence_combining_items_collection_as_a_validation_error()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id) with { SentenceCombiningItems = null! }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_a_null_stuck_words_collection_as_a_validation_error()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id) with { StuckWords = null! }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
