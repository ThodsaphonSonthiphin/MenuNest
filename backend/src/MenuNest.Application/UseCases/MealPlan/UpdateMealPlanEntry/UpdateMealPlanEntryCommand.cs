using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;

public sealed record UpdateMealPlanEntryCommand(
    Guid Id,
    Guid RecipeId,
    string? Notes) : ICommand<MealPlanEntryDto>;
