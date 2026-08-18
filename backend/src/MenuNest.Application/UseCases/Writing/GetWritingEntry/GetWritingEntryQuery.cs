using Mediator;

namespace MenuNest.Application.UseCases.Writing.GetWritingEntry;

/// <summary>
/// Reads one writing entry with its Correction for the ผลตรวจ screen
/// (ADR-177/ADR-179). Scoped to the calling user; a missing, deleted or
/// foreign id all answer with the same "not found" message.
/// </summary>
public sealed record GetWritingEntryQuery(Guid Id) : IQuery<WritingEntryDetailDto>;
