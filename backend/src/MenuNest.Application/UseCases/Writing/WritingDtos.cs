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
