using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;

/// <summary>
/// <paramref name="TimeZoneId"/> is the viewer's IANA time zone (menunest-189).
/// It is required, not optional: every read decides whether the Daily
/// allowance card is for "the current month" against the viewer's local
/// today, so a missing/unknown id is rejected rather than silently read as
/// UTC — see <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record GetMonthlySummaryQuery(int Year, int Month, string? TimeZoneId) : IQuery<MonthlySummaryDto>;
