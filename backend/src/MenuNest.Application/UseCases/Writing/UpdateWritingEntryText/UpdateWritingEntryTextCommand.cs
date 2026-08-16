using Mediator;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

/// <summary>
/// Edits an existing WritingEntry's text. Only allowed while CorrectedAt is
/// null -- entry-mutability (ADR-169) locks the text the moment a
/// correction is recorded.
/// </summary>
public sealed record UpdateWritingEntryTextCommand(Guid Id, string Text) : ICommand<WritingEntryDto>;
