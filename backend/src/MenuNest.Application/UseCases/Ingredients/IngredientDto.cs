namespace MenuNest.Application.UseCases.Ingredients;

public sealed record IngredientDto(
    Guid Id,
    string Name,
    string Unit);
