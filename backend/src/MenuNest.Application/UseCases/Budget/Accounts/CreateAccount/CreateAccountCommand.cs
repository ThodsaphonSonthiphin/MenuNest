using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

public sealed record CreateAccountCommand(
    string Name, BudgetAccountType Type, decimal OpeningBalance, int SortOrder)
    : ICommand<BudgetAccountDto>;
