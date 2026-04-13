using Mediator;

namespace MenuNest.Application.UseCases.Stock.UpsertStock;

/// <summary>
/// Creates or updates the stock row for a given ingredient in the
/// current family. Idempotent on (FamilyId, IngredientId).
/// </summary>
public sealed record UpsertStockCommand(Guid IngredientId, decimal Quantity) : ICommand<StockItemDto>;
