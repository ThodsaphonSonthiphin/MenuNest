using Mediator;

namespace MenuNest.Application.UseCases.Ingredients.UpdateIngredient;

public sealed record UpdateIngredientCommand(Guid Id, string Name, string Unit) : ICommand<IngredientDto>;
