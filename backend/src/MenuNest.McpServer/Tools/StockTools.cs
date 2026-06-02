using MenuNest.Application.UseCases.Stock;
using MenuNest.Application.UseCases.Stock.ListStock;
using MenuNest.Application.UseCases.Stock.UpsertStock;
using MenuNest.Application.UseCases.Stock.DeleteStock;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class StockTools(IMediator mediator)
{
    [McpServerTool, Description("List all current stock items for the family")]
    public async Task<IReadOnlyList<StockItemDto>> list_stock(CancellationToken ct)
        => await mediator.Send(new ListStockQuery(), ct);

    [McpServerTool, Description("Set the stock quantity for an ingredient (creates or updates)")]
    public async Task<StockItemDto> upsert_stock(
        [Description("Ingredient ID")] Guid ingredientId,
        [Description("Quantity on hand")] decimal quantity,
        CancellationToken ct)
        => await mediator.Send(new UpsertStockCommand(ingredientId, quantity), ct);

    [McpServerTool, Description("Remove a stock entry by ID")]
    public async Task delete_stock(
        [Description("Stock entry ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteStockCommand(id), ct);
}
