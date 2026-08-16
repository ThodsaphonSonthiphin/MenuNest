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
