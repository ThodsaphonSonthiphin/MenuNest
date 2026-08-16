namespace MenuNest.Application.UseCases.Writing;

/// <summary>
/// A submitted writing-practice entry — returned after
/// <c>POST /api/writing-entries</c>.
/// </summary>
public sealed record WritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime CreatedAt);
