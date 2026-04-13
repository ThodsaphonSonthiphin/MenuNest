using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;

public sealed record DeleteMealPlanEntryCommand(Guid Id) : ICommand;
