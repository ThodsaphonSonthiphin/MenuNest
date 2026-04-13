using Mediator;
using MenuNest.Application.UseCases.Stock;
using MenuNest.Application.UseCases.Stock.DeleteStock;
using MenuNest.Application.UseCases.Stock.ListStock;
using MenuNest.Application.UseCases.Stock.UpsertStock;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/stock")]
public sealed class StockController : ControllerBase
{
    private readonly IMediator _mediator;

    public StockController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StockItemDto>>> List(CancellationToken ct)
    {
        var items = await _mediator.Send(new ListStockQuery(), ct);
        return Ok(items);
    }

    /// <summary>
    /// Upserts the stock row for the given ingredient — creates a new
    /// row if none exists, otherwise sets the absolute quantity.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<StockItemDto>> Upsert(
        [FromBody] UpsertStockRequest request,
        CancellationToken ct)
    {
        var stock = await _mediator.Send(
            new UpsertStockCommand(request.IngredientId, request.Quantity),
            ct);
        return Ok(stock);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteStockCommand(id), ct);
        return NoContent();
    }
}

public sealed record UpsertStockRequest(Guid IngredientId, decimal Quantity);
