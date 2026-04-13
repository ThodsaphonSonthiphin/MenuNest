using Mediator;

namespace MenuNest.Application.UseCases.Recipes.GetRecipe;

public sealed record GetRecipeQuery(Guid Id) : IQuery<RecipeDetailDto>;
