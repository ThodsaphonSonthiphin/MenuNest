using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;

public sealed record MoveMoneyCommand(
    Guid FromCategoryId, Guid ToCategoryId, int Year, int Month, decimal Amount) : ICommand<Unit>;
