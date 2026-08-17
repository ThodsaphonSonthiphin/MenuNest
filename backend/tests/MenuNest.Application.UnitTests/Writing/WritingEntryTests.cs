using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class WritingEntryTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Fact]
    public void Create_computes_words_per_minute_from_stripped_text_and_elapsed_seconds()
    {
        var userId = Guid.NewGuid();
        // 10 words of visible text, 7 minutes (420s) elapsed -> ~1.4286 wpm.
        var entry = WritingEntry.Create(
            userId,
            Today,
            "<p>one two three four five six seven eight nine ten</p>",
            elapsedSeconds: 420);

        entry.UserId.Should().Be(userId);
        entry.Date.Should().Be(Today);
        entry.Text.Should().Be("<p>one two three four five six seven eight nine ten</p>");
        entry.ElapsedSeconds.Should().Be(420);
        entry.WordsPerMinute.Should().BeApproximately(10.0 / 7.0, 0.001);
        entry.CorrectedAt.Should().BeNull();
        entry.TargetRule.Should().BeNull();
    }

    [Fact]
    public void Create_throws_when_user_id_is_empty()
    {
        var act = () => WritingEntry.Create(Guid.Empty, Today, "<p>hi there</p>", 60);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_throws_when_text_is_empty_or_whitespace_only_html()
    {
        var act = () => WritingEntry.Create(Guid.NewGuid(), Today, "<p>   </p>", 60);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_throws_when_elapsed_seconds_is_not_positive()
    {
        var act = () => WritingEntry.Create(Guid.NewGuid(), Today, "<p>hi there</p>", 0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Words_per_minute_is_zero_when_stripped_text_has_no_words()
    {
        // HTML-only content (e.g. an empty formatted paragraph) with no visible
        // words would divide-by-zero if not guarded — assert it does not throw
        // and instead the earlier "empty text" guard rejects it first, OR (if
        // some non-empty-but-wordless edge case exists) WordsPerMinute is 0,
        // never NaN/Infinity.
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>a</p>", 60);
        double.IsNaN(entry.WordsPerMinute).Should().BeFalse();
        double.IsInfinity(entry.WordsPerMinute).Should().BeFalse();
    }

    [Fact]
    public void Create_throws_when_text_is_only_a_non_breaking_space_entity()
    {
        // <p>&nbsp;</p> is visually/effectively empty content -- without entity
        // normalization the tag-stripped text is "&nbsp;", which is non-empty
        // after Trim() and would defeat the "must contain at least one word"
        // guard. It must be rejected exactly like literal whitespace already is.
        var act = () => WritingEntry.Create(Guid.NewGuid(), Today, "<p>&nbsp;</p>", 60);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_treats_nbsp_as_a_word_separator_in_words_per_minute()
    {
        // "one&nbsp;two" is two words joined by a non-breaking space, not one
        // token -- if &nbsp; isn't normalized to a real space, this would count
        // as a single "word" and understate the WPM signal.
        var entry = WritingEntry.Create(
            Guid.NewGuid(),
            Today,
            "<p>one&nbsp;two</p>",
            elapsedSeconds: 60);

        entry.WordsPerMinute.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void SoftDelete_sets_DeletedAt()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>a night to delete</p>", 420);
        entry.DeletedAt.Should().BeNull();

        entry.SoftDelete();

        entry.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateText_changes_text_when_not_yet_corrected()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);

        entry.UpdateText("<p>edited text here</p>");

        entry.Text.Should().Be("<p>edited text here</p>");
    }

    [Fact]
    public void UpdateText_throws_when_text_is_empty_or_whitespace_only_html()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);

        var act = () => entry.UpdateText("<p>   </p>");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateText_throws_when_the_entry_is_already_corrected()
    {
        // CorrectedAt has no public setter yet -- it stays reserved until Phase
        // 2's record_writing_correction MCP tool lands (mcp-tool-contract).
        // Set it via reflection here purely to exercise the lock guard; this is
        // not how CorrectedAt gets set in production.
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);
        typeof(WritingEntry).GetProperty(nameof(WritingEntry.CorrectedAt))!
            .SetValue(entry, DateTime.UtcNow);

        var act = () => entry.UpdateText("<p>trying to edit this now</p>");

        act.Should().Throw<DomainException>();
    }


    private static WritingEntry APendingEntry() =>
        WritingEntry.Create(Guid.NewGuid(), new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420);

    [Fact]
    public void RecordCorrection_sets_every_correction_column()
    {
        var entry = APendingEntry();
        var at = new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc);

        entry.RecordCorrection(
            correctedAtUtc: at,
            targetRule: "third-person singular -s",
            markedText: "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            hitCount: 0,
            missCount: 1,
            thaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            sentenceCombiningItemsJson: "[]",
            stuckWordsJson: "[]");

        entry.CorrectedAt.Should().Be(at);
        entry.TargetRule.Should().Be("third-person singular -s");
        entry.MarkedText.Should().Contain("→ goes");
        entry.HitCount.Should().Be(0);
        entry.MissCount.Should().Be(1);
        entry.ThaiWhyLine.Should().Contain("เติม -s");
        entry.SentenceCombiningItemsJson.Should().Be("[]");
        entry.StuckWordsJson.Should().Be("[]");
    }

    [Fact]
    public void RecordCorrection_leaves_the_text_and_words_per_minute_untouched()
    {
        var entry = APendingEntry();
        var originalText = entry.Text;
        var originalWpm = entry.WordsPerMinute;

        entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc),
            "third-person singular -s",
            new string('x', 5_000),   // a marked text far longer than the original
            0, 1, "เหตุผล", "[]", "[]");

        entry.Text.Should().Be(originalText);
        entry.WordsPerMinute.Should().Be(originalWpm);
    }

    [Fact]
    public void RecordCorrection_a_second_time_overwrites_every_column_together()
    {
        var entry = APendingEntry();
        var first = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

        entry.RecordCorrection(first, "third-person singular -s", "first", 0, 3, "แรก", "[]", "[]");
        entry.RecordCorrection(second, "plural -s", "second", 1, 2, "สอง", "[{\"a\":1}]", "[{\"b\":2}]");

        // Last write wins, and no column is left describing the previous pass.
        entry.CorrectedAt.Should().Be(second);
        entry.TargetRule.Should().Be("plural -s");
        entry.MarkedText.Should().Be("second");
        entry.HitCount.Should().Be(1);
        entry.MissCount.Should().Be(2);
        entry.ThaiWhyLine.Should().Be("สอง");
        entry.SentenceCombiningItemsJson.Should().Be("[{\"a\":1}]");
        entry.StuckWordsJson.Should().Be("[{\"b\":2}]");
    }

    [Fact]
    public void RecordCorrection_still_locks_the_text_against_editing()
    {
        var entry = APendingEntry();
        entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

        var act = () => entry.UpdateText("<p>edited after the correction</p>");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot edit text after a correction has been recorded.");
    }

    [Fact]
    public void RecordCorrection_rejects_negative_counts()
    {
        var entry = APendingEntry();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", -1, 0, "เหตุผล", "[]", "[]");

        act.Should().Throw<DomainException>()
            .WithMessage("HitCount and MissCount cannot be negative.");
    }

    [Fact]
    public void RecordCorrection_requires_a_target_rule()
    {
        var entry = APendingEntry();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "   ", "marked", 0, 1, "เหตุผล", "[]", "[]");

        act.Should().Throw<DomainException>()
            .WithMessage("TargetRule is required to record a correction.");
    }

    [Fact]
    public void RecordCorrection_is_allowed_on_a_soft_deleted_entry_only_via_the_handler_guard()
    {
        // The entity itself does not block it — the handler's DeletedAt == null
        // filter is what refuses (WMCP-14). This test documents that boundary so
        // nobody "fixes" it in the wrong layer.
        var entry = APendingEntry();
        entry.SoftDelete();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

        act.Should().NotThrow();
    }
}
