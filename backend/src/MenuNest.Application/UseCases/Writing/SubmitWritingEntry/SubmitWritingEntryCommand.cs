using Mediator;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

/// <summary>
/// Submits tonight's 7-minute freewrite entry. Per done-day-redefinition
/// (docs/decision-map/writing-practice-build), this alone marks the day
/// "done" — no correction step happens here.
/// </summary>
public sealed record SubmitWritingEntryCommand(
    DateOnly Date,
    string Text,
    int ElapsedSeconds) : ICommand<WritingEntryDto>;
