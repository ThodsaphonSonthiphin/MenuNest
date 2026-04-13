using Mediator;

namespace MenuNest.Application.UseCases.Ingredients.CreateIngredient;

public sealed record CreateIngredientCommand(string Name, string Unit) : ICommand<IngredientDto>;
