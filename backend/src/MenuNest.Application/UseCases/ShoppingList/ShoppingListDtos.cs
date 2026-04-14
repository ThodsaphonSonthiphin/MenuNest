using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.ShoppingList;

public sealed record ShoppingListDto(
    Guid Id,
    string Name,
    ShoppingListStatus Status,
    int TotalCount,
    int BoughtCount,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record ShoppingListItemDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Quantity,
    bool IsBought,
    DateTime? BoughtAt,
    IReadOnlyList<Guid>? SourceMealPlanEntryIds);

public sealed record ShoppingListDetailDto(
    Guid Id,
    string Name,
    ShoppingListStatus Status,
    int TotalCount,
    int BoughtCount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ShoppingListItemDto> Items);
