using Mediator;

namespace MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;

public sealed record ListTransactionsQuery(int Year, int Month, Guid? CategoryId)
    : IQuery<IReadOnlyList<BudgetTransactionDto>>;
