using Mediator;

namespace MenuNest.Application.UseCases.Writing.DeleteWritingEntry;

/// <summary>
/// Soft-deletes a WritingEntry. Allowed even after a correction has locked
/// the text (entry-mutability / ADR-169) -- the lock only blocks edits.
/// </summary>
public sealed record DeleteWritingEntryCommand(Guid Id) : ICommand;
