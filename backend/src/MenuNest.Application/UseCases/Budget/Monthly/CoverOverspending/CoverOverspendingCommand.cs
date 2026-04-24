using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

public sealed record CoverOverspendingCommand(
    Guid OverspentCategoryId, Guid FromCategoryId, int Year, int Month, decimal Amount)
    : ICommand<Unit>;
