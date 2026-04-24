using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;

public sealed record SetAssignedAmountCommand(Guid CategoryId, int Year, int Month, decimal Amount)
    : ICommand<Unit>;
