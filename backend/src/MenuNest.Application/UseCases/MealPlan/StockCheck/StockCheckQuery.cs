using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.StockCheck;

public sealed record StockCheckQuery(Guid MealPlanEntryId) : IQuery<StockCheckDto>;
