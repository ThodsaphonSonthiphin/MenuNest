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
}
