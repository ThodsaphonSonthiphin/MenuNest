using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;

/// <summary>
/// <paramref name="TimeZoneId"/> (menunest-189) is only actually resolved when
/// assigning touches an everyday envelope and a re-freeze fires — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record SetAssignedAmountCommand(
    Guid CategoryId, int Year, int Month, decimal Amount, string? TimeZoneId)
    : ICommand<Unit>;
