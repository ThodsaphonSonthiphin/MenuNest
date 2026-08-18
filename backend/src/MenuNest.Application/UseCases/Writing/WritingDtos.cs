namespace MenuNest.Application.UseCases.Writing;

/// <summary>
/// A writing-practice entry, as returned by submit, list, and update-text.
/// </summary>
public sealed record WritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime? CorrectedAt,
    DateTime CreatedAt);

/// <summary>
/// One un-corrected night, as returned by list_pending_writing_entries.
/// Exactly the five fields of mcp-tool-contract:51-53 — no CorrectedAt,
/// because every row in this list is pending by definition.
/// </summary>
public sealed record PendingWritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute);

/// <summary>
/// One sentence-combining item of the correction's block 3 — the writer's own
/// two short sentences and the combined version. Shape taken from the approved
/// mock (screens/writing-practice-critique-loop.html, frame 2 block 3):
/// "Traffic is very bad. + We arrive late." → "Traffic was very bad, so we arrived late."
/// </summary>
public sealed record SentenceCombiningItemDto(string Source, string Combined);

/// <summary>
/// One bracketed Thai word the writer got stuck on, with its English
/// translation — block 4 of the correction ("ข้าวต้ม → rice porridge / congee").
/// </summary>
public sealed record StuckWordDto(string Thai, string English);

/// <summary>
/// One recorded Correction, as the ผลตรวจ screen needs it: the five blocks of
/// mcp-tool-contract plus the numbers derived on the way out (ADR-179). The
/// two JSON columns arrive here already deserialised.
///
/// WordCount is the same counter behind ErrorsPer100Words (WritingEntry.CountWords),
/// surfaced so block 5's caption never reconstructs it client-side by inverting
/// WordsPerMinute x ElapsedSeconds -- ADR-179 rejected that arithmetic explicitly,
/// because UpdateText deliberately does not recompute WordsPerMinute, so an
/// edited-after-submit entry would show a word count that no longer matches the
/// text the server actually derived ErrorsPer100Words from.
/// </summary>
public sealed record WritingCorrectionDto(
    string TargetRule,
    string MarkedText,
    int HitCount,
    int MissCount,
    string ThaiWhyLine,
    IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems,
    IReadOnlyList<StuckWordDto> StuckWords,
    double ErrorsPer100Words,
    int WordCount);

/// <summary>
/// One writing entry with its Correction, as returned by
/// GET /api/writing-entries/{id}. Correction is null while the night is
/// pending. Deliberately NOT the shape of the list endpoint: MarkedText is
/// bounded at 50,000 characters and the History grid needs none of it.
/// </summary>
public sealed record WritingEntryDetailDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime? CorrectedAt,
    DateTime CreatedAt,
    WritingCorrectionDto? Correction);
