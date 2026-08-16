using Mediator;

namespace MenuNest.Application.UseCases.Writing.ListWritingEntries;

/// <summary>
/// Lists every non-deleted WritingEntry for the current user, newest first.
/// Feeds the "ประวัติ" (History) screen -- filtering by pending/corrected
/// status happens client-side over this full list
/// (pending-correction-visibility).
/// </summary>
public sealed record ListWritingEntriesQuery : IQuery<IReadOnlyList<WritingEntryDto>>;
