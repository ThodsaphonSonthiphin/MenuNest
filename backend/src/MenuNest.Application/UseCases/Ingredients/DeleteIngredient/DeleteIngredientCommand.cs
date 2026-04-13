using Mediator;

namespace MenuNest.Application.UseCases.Ingredients.DeleteIngredient;

public sealed record DeleteIngredientCommand(Guid Id) : ICommand;
