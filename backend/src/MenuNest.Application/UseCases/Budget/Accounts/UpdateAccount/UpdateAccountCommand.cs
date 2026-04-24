using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;

public sealed record UpdateAccountCommand(
    Guid Id, string Name, int SortOrder, bool IsClosed, decimal? SetBalance)
    : ICommand<BudgetAccountDto>;
