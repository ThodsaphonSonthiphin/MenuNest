using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;

public sealed record CreateMealPlanEntryCommand(
    DateOnly Date,
    MealSlot MealSlot,
    Guid RecipeId,
    string? Notes) : ICommand<MealPlanEntryDto>;
