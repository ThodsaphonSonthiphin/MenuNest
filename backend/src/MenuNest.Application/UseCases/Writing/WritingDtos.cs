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
