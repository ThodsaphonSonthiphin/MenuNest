using Mediator;

namespace MenuNest.Application.UseCases.Recipes.ListRecipes;

public sealed record ListRecipesQuery : IQuery<IReadOnlyList<RecipeSummaryDto>>;
