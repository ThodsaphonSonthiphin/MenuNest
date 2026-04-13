using Mediator;

namespace MenuNest.Application.UseCases.Ingredients.ListIngredients;

public sealed record ListIngredientsQuery : IQuery<IReadOnlyList<IngredientDto>>;
