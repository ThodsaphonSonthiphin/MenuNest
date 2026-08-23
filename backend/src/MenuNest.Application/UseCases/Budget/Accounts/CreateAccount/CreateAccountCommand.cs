using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

/// <summary>
/// <paramref name="TimeZoneId"/> (menunest-189) is only actually resolved when
/// <paramref name="OpeningBalance"/> is non-zero and an opening-balance
/// transaction is written — see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>.
/// </summary>
public sealed record CreateAccountCommand(
    string Name, BudgetAccountType Type, decimal OpeningBalance, string? TimeZoneId)
    : ICommand<BudgetAccountDto>;
