namespace MenuNest.Application.UseCases.Ingredients;

public sealed record IngredientDto(
    Guid IngredientId,
    string Name,
    string Unit);
