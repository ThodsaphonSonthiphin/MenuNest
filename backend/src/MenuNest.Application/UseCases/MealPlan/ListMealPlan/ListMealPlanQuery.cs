using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.ListMealPlan;

public sealed record ListMealPlanQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<MealPlanEntryDto>>;
