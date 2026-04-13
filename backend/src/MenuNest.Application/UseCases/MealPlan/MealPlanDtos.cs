using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.MealPlan;

public sealed record MealPlanEntryDto(
    Guid Id,
    DateOnly Date,
    MealSlot MealSlot,
    Guid RecipeId,
    string RecipeName,
    string? Notes,
    MealEntryStatus Status,
    DateTime? CookedAt,
    string? CookNotes);

public sealed record StockCheckLineDto(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Required,
    decimal Available,
    decimal Missing);

public sealed record StockCheckDto(
    Guid MealPlanEntryId,
    Guid RecipeId,
    string RecipeName,
    IReadOnlyList<StockCheckLineDto> Lines,
    bool IsSufficient)
{
    public int MissingCount => Lines.Count(l => l.Missing > 0);
}
