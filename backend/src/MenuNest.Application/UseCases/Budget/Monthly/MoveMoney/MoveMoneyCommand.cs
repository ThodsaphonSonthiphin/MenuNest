using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;

/// <summary>
/// <paramref name="TimeZoneId"/> (menunest-189) is only actually resolved when
/// the move touches an everyday envelope and a re-freeze fires — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record MoveMoneyCommand(
    Guid FromCategoryId, Guid ToCategoryId, int Year, int Month, decimal Amount, string? TimeZoneId)
    : ICommand<Unit>;
