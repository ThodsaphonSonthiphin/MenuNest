using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;

public sealed record ListAccountsQuery : IQuery<IReadOnlyList<BudgetAccountDto>>;
