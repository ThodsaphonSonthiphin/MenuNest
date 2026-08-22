using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

/// <summary>
/// <paramref name="TimeZoneId"/> (menunest-189) is only actually resolved when
/// covering touches an everyday envelope and a re-freeze fires — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record CoverOverspendingCommand(
    Guid OverspentCategoryId, Guid FromCategoryId, int Year, int Month, decimal Amount, string? TimeZoneId)
    : ICommand<Unit>;
