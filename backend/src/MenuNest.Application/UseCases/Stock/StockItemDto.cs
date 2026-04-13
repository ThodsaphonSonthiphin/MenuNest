namespace MenuNest.Application.UseCases.Stock;

public sealed record StockItemDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Quantity,
    DateTime UpdatedAt,
    Guid UpdatedByUserId);
