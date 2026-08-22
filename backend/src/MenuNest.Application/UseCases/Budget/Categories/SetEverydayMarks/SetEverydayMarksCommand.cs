using Mediator;

namespace MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;

/// <summary>One envelope's requested everyday mark/unmark.</summary>
public sealed record EverydayMark(Guid CategoryId, bool IsEveryday);

/// <summary>
/// Bulk-applies every mark in <paramref name="Marks"/> as a single Budgeting
/// event (menunest-184) — marking six envelopes re-freezes the Daily allowance
/// exactly once, not six times. <paramref name="TimeZoneId"/> (menunest-189) is
/// only actually resolved when something in the sheet actually changed and a
/// re-freeze fires — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record SetEverydayMarksCommand(IReadOnlyList<EverydayMark> Marks, string? TimeZoneId) : ICommand<Unit>;
