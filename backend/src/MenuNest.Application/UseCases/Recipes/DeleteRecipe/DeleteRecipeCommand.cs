using Mediator;

namespace MenuNest.Application.UseCases.Recipes.DeleteRecipe;

public sealed record DeleteRecipeCommand(Guid Id) : ICommand;
