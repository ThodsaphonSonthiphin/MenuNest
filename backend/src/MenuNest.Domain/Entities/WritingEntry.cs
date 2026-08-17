using System.Text.RegularExpressions;
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One night's 7-minute freewrite entry (issue #97, Phase 1). Storage-only
/// today — the AI correction fields below are reserved for Phase 2
/// (WritingTools MCP, ai-correction-invocation) and stay null until that
/// phase lands, so no second migration is needed then.
/// </summary>
public sealed partial class WritingEntry : Entity
{
    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int ElapsedSeconds { get; private set; }
    public double WordsPerMinute { get; private set; }

    // Phase 2 (mcp-tool-contract's record_writing_correction) — reserved, unset here.
    public DateTime? CorrectedAt { get; private set; }
    public string? MarkedText { get; private set; }
    public string? TargetRule { get; private set; }
    public int? HitCount { get; private set; }
    public int? MissCount { get; private set; }
    public string? ThaiWhyLine { get; private set; }
    public string? SentenceCombiningItemsJson { get; private set; }
    public string? StuckWordsJson { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    // EF Core
    private WritingEntry() { }

    public static WritingEntry Create(
        Guid userId,
        DateOnly date,
        string text,
        int elapsedSeconds)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (elapsedSeconds <= 0)
            throw new DomainException("ElapsedSeconds must be positive.");

        var wordCount = CountWords(text);
        if (wordCount == 0)
            throw new DomainException("Text must contain at least one word.");

        var minutes = elapsedSeconds / 60.0;

        return new WritingEntry
        {
            UserId = userId,
            Date = date,
            Text = text,
            ElapsedSeconds = elapsedSeconds,
            WordsPerMinute = minutes > 0 ? wordCount / minutes : 0
        };
    }

    /// <summary>
    /// Edits the text of an entry that has not yet been corrected. Once
    /// CorrectedAt is set, the recorded HitCount/MissCount/ThaiWhyLine
    /// describe specific text -- letting that text drift under them would
    /// make the correction lie, so entry-mutability (ADR-169) locks it.
    /// WordsPerMinute is deliberately left unchanged: it measures the
    /// original timed writing session, not a later typo fix.
    /// </summary>
    public void UpdateText(string text)
    {
        if (CorrectedAt is not null)
            // This exact string is load-bearing OUTSIDE the backend: the SPA's
            // saveErrorMessage.ts matches on it (it arrives as ProblemDetails.Detail)
            // to tell the writer their night was corrected rather than falsely
            // offering a retry. Editing the wording here means editing that helper
            // and its tests too.
            throw new DomainException("Cannot edit text after a correction has been recorded.");

        var wordCount = CountWords(text);
        if (wordCount == 0)
            throw new DomainException("Text must contain at least one word.");

        Text = text;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records (or re-records) the AI correction for this entry — the five
    /// blocks of mcp-tool-contract's record_writing_correction. Called only
    /// from RecordWritingCorrectionHandler, which owns the DeletedAt guard and
    /// the clock.
    ///
    /// Re-recording OVERWRITES all seven columns together (writer's decision,
    /// 2026-08-17): a bad AI pass must be repairable. That is not the revision
    /// loop the mock forbids — UpdateText stays locked, so the corrected text
    /// itself can never drift.
    ///
    /// Text and WordsPerMinute are deliberately untouched: WordsPerMinute
    /// measures the original timed session, and markedText is strictly longer
    /// than the text it annotates.
    /// </summary>
    public void RecordCorrection(
        DateTime correctedAtUtc,
        string targetRule,
        string markedText,
        int hitCount,
        int missCount,
        string thaiWhyLine,
        string sentenceCombiningItemsJson,
        string stuckWordsJson)
    {
        if (string.IsNullOrWhiteSpace(targetRule))
            throw new DomainException("TargetRule is required to record a correction.");
        if (hitCount < 0 || missCount < 0)
            throw new DomainException("HitCount and MissCount cannot be negative.");

        CorrectedAt = correctedAtUtc;
        TargetRule = targetRule.Trim();
        MarkedText = markedText;
        HitCount = hitCount;
        MissCount = missCount;
        ThaiWhyLine = thaiWhyLine;
        SentenceCombiningItemsJson = sentenceCombiningItemsJson;
        StuckWordsJson = stuckWordsJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Soft-deletes the entry (ADR-169) -- allowed even when the text is
    /// locked by a correction; the lock only blocks edits, not deletion.
    /// The row stays so the monthly old-vs-new comparison (progress-signal)
    /// can still resolve a deleted night if the rotation lands on it.
    /// </summary>
    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Approximate word count of RTE-produced HTML: strips tags, normalizes
    /// HTML whitespace entities (&nbsp;) to real spaces, collapses
    /// whitespace, splits on spaces. Good enough for a words-per-minute
    /// signal — not a precise linguistic tokenizer.
    /// </summary>
    private static int CountWords(string html)
    {
        var stripped = TagRegex().Replace(html, " ");
        var withoutEntities = NbspRegex().Replace(stripped, " ");
        var normalized = withoutEntities.Trim();
        if (normalized.Length == 0) return 0;
        return WhitespaceRegex().Split(normalized).Length;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("&nbsp;|&#160;|&#xa0;", RegexOptions.IgnoreCase)]
    private static partial Regex NbspRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
