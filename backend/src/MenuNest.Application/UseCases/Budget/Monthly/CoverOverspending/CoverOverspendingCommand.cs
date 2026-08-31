using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

/// <summary>
/// <paramref name="FromCategoryId"/> is NULL when the cover comes from Ready to
/// Assign rather than from another envelope (menunest-215). Ready to Assign is
/// derived — <c>sum(accounts) − sum(envelope.available)</c> — so it owns no
/// <see cref="MenuNest.Domain.Entities.MonthlyAssignment"/> row to decrement;
/// covering from it is a one-sided increment of the overspent envelope, which
/// is exactly what makes the derived figure fall.
///
/// <para><paramref name="TimeZoneId"/> (menunest-189) is only actually resolved
/// when covering touches an everyday envelope and a re-freeze fires — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.</para>
/// </summary>
public sealed record CoverOverspendingCommand(
    Guid OverspentCategoryId, Guid? FromCategoryId, int Year, int Month, decimal Amount, string? TimeZoneId)
    : ICommand<Unit>;
