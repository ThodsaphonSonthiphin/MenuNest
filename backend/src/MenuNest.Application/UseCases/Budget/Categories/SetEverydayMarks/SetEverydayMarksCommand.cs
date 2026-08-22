using Mediator;

namespace MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;

/// <summary>One envelope's requested everyday mark/unmark.</summary>
public sealed record EverydayMark(Guid CategoryId, bool IsEveryday);

/// <summary>
/// Bulk-applies every mark in <paramref name="Marks"/> as a single Budgeting
/// event (menunest-184) — marking six envelopes re-freezes the Daily allowance
/// exactly once, not six times.
/// </summary>
public sealed record SetEverydayMarksCommand(IReadOnlyList<EverydayMark> Marks) : ICommand<Unit>;
