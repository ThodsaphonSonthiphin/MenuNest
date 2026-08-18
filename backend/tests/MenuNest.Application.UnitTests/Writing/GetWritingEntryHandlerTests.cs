using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.GetWritingEntry;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class GetWritingEntryHandlerTests
{
    private const string SevenWordText = "<p>one two three four five six seven</p>";

    private static WritingEntry SeedCorrected(
        HandlerTestFixture fx,
        string text = SevenWordText,
        int hitCount = 2,
        int missCount = 3,
        string sentenceCombiningJson = "[]",
        string stuckWordsJson = "[]")
    {
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), text, 420);
        entry.RecordCorrection(
            correctedAtUtc: new DateTime(2026, 8, 17, 14, 57, 23, DateTimeKind.Utc),
            targetRule: "articles (a/an/the)",
            markedText: "<p><span class=\"hit\">one</span> two</p>",
            hitCount: hitCount,
            missCount: missCount,
            thaiWhyLine: "คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ",
            sentenceCombiningItemsJson: sentenceCombiningJson,
            stuckWordsJson: stuckWordsJson);
        fx.Db.WritingEntries.Add(entry);
        return entry;
    }

    [Fact]
    public async Task Returns_a_null_correction_for_a_night_that_was_never_corrected()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), SevenWordText, 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Id.Should().Be(entry.Id);
        result.Text.Should().Be(SevenWordText);
        result.CorrectedAt.Should().BeNull();
        result.Correction.Should().BeNull("a pending night has no correction to carry");
    }

    [Fact]
    public async Task Carries_all_five_blocks_of_a_recorded_correction()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(
            fx,
            sentenceCombiningJson: "[{\"Source\":\"Traffic is bad. + We arrive late.\",\"Combined\":\"Traffic was bad, so we arrived late.\"}]",
            stuckWordsJson: "[{\"Thai\":\"ข้าวต้ม\",\"English\":\"rice porridge / congee\"}]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction.Should().NotBeNull();
        var c = result.Correction!;
        c.TargetRule.Should().Be("articles (a/an/the)");
        c.MarkedText.Should().Be("<p><span class=\"hit\">one</span> two</p>");
        c.HitCount.Should().Be(2);
        c.MissCount.Should().Be(3);
        c.ThaiWhyLine.Should().Be("คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ");
        c.SentenceCombiningItems.Should().HaveCount(1);
        c.SentenceCombiningItems[0].Source.Should().Be("Traffic is bad. + We arrive late.");
        c.SentenceCombiningItems[0].Combined.Should().Be("Traffic was bad, so we arrived late.");
        c.StuckWords.Should().HaveCount(1);
        c.StuckWords[0].Thai.Should().Be("ข้าวต้ม");
        c.StuckWords[0].English.Should().Be("rice porridge / congee");
    }

    [Fact]
    public async Task Deserialises_the_pascal_case_json_the_recorder_actually_writes()
    {
        // RecordWritingCorrectionHandler serialises the C# records with default
        // naming, so the stored text is PascalCase. A camelCase policy here would
        // deserialise every field to an empty string without failing.
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(
            fx,
            stuckWordsJson: "[{\"Thai\":\"ซุซิสายพาน\",\"English\":\"conveyor-belt sushi\"}]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.StuckWords[0].Thai.Should().Be("ซุซิสายพาน");
        result.Correction!.StuckWords[0].English.Should().Be("conveyor-belt sushi");
    }

    [Fact]
    public async Task An_empty_json_array_becomes_an_empty_list_not_a_null()
    {
        // The only real production correction has SentenceCombiningItemsJson = "[]"
        // (a Thai-only night). The screen renders an empty block for it, so the
        // list must arrive empty rather than null.
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, sentenceCombiningJson: "[]", stuckWordsJson: "[]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.SentenceCombiningItems.Should().BeEmpty();
        result.Correction!.StuckWords.Should().BeEmpty();
    }

    [Fact]
    public async Task Derives_errors_per_100_words_to_one_decimal_place()
    {
        // 3 misses over 7 words = 42.857... -> 42.9
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, text: SevenWordText, missCount: 3);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.ErrorsPer100Words.Should().Be(42.9);
    }

    [Fact]
    public async Task A_thai_only_night_with_no_misses_derives_zero()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, text: "<p>[วันนี้พาลูกสาวไปกินข้าวเย็น]</p>", hitCount: 0, missCount: 0);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.HitCount.Should().Be(0);
        result.Correction!.MissCount.Should().Be(0);
        result.Correction!.ErrorsPer100Words.Should().Be(0);
    }

    [Fact]
    public async Task Refuses_an_unknown_id_with_the_standard_message()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await handler.Handle(new GetWritingEntryQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_a_soft_deleted_entry()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx);
        entry.SoftDelete();
        await fx.Db.SaveChangesAsync();

        var act = async () => await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_another_users_entry_with_the_same_message()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var othersEntry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), SevenWordText, 420);
        fx.Db.WritingEntries.Add(othersEntry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await handler.Handle(new GetWritingEntryQuery(othersEntry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }
}
