using Mediator;

namespace MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;

public sealed record UpdateTransactionCommand(
    Guid Id, Guid AccountId, Guid? CategoryId, decimal Amount, DateOnly Date, string? Notes)
    : ICommand<BudgetTransactionDto>;
