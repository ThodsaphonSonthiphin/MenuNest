using Mediator;

namespace MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;

public sealed record DeleteTransactionCommand(Guid Id) : ICommand<Unit>;
