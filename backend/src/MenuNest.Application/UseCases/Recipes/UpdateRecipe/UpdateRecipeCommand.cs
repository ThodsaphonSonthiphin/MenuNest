using Mediator;

namespace MenuNest.Application.UseCases.Recipes.UpdateRecipe;

public sealed record UpdateRecipeCommand(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<RecipeIngredientInput> Ingredients) : ICommand<RecipeDetailDto>;
