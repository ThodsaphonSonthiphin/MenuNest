using Mediator;

namespace MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;

public sealed record CreateTransactionCommand(
    Guid AccountId, Guid? CategoryId, decimal Amount, DateOnly Date, string? Notes)
    : ICommand<BudgetTransactionDto>;
