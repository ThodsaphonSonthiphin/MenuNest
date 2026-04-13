using Mediator;

namespace MenuNest.Application.UseCases.Recipes.CreateRecipe;

public sealed record CreateRecipeCommand(
    string Name,
    string? Description,
    IReadOnlyList<RecipeIngredientInput> Ingredients) : ICommand<RecipeDetailDto>;
