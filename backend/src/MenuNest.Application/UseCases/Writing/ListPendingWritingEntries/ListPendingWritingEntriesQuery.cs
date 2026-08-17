using Mediator;

namespace MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;

/// <summary>
/// Every entry of the current user that has no correction yet, newest first.
/// This is how Claude Code answers "did I write anything since the last
/// correction?" without the writer naming a date (mcp-tool-contract).
/// </summary>
public sealed record ListPendingWritingEntriesQuery : IQuery<IReadOnlyList<PendingWritingEntryDto>>;
