using Mediator;

namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

public sealed record ListChangesQuery(int Year, int Month) : IQuery<IReadOnlyList<BudgetChangeDto>>;
