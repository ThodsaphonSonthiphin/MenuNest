namespace MenuNest.Application.UseCases.Recipes;

/// <summary>
/// Line item sent when creating or updating a recipe.
/// </summary>
public sealed record RecipeIngredientInput(Guid IngredientId, decimal Quantity);

/// <summary>
/// Line item returned with a recipe, with the ingredient name/unit
/// denormalised so the UI doesn't need a second lookup.
/// </summary>
public sealed record RecipeIngredientDto(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Quantity);

/// <summary>
/// Lightweight card view for the recipes list.
/// </summary>
public sealed record RecipeSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string? ImageBlobPath,
    int IngredientCount);

/// <summary>
/// Full recipe, including every ingredient line.
/// </summary>
public sealed record RecipeDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? ImageBlobPath,
    IReadOnlyList<RecipeIngredientDto> Ingredients);
