using Mediator;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/shopping-lists")]
public sealed class ShoppingListsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ShoppingListsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShoppingListDto>>> List(
        [FromQuery] ShoppingListStatus? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListShoppingListsQuery(status), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShoppingListDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetShoppingListDetailQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ShoppingListDto>> Create(
        [FromBody] CreateShoppingListRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateShoppingListCommand(request.Name, request.FromDate, request.ToDate), ct);
        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpPost("{listId:guid}/items/{itemId:guid}/buy")]
    public async Task<ActionResult<ShoppingListItemDto>> Buy(
        Guid listId, Guid itemId, CancellationToken ct)
    {
        var result = await _mediator.Send(new BuyShoppingListItemCommand(listId, itemId), ct);
        return Ok(result);
    }

    [HttpPost("{listId:guid}/items/{itemId:guid}/unbuy")]
    public async Task<ActionResult<ShoppingListItemDto>> Unbuy(
        Guid listId, Guid itemId, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnbuyShoppingListItemCommand(listId, itemId), ct);
        return Ok(result);
    }

    // DELETE, /complete, /items, /items/{itemId}, /regenerate
    // are wired in subsequent tasks as the handlers are created.
}

public sealed record CreateShoppingListRequest(string Name, DateOnly? FromDate, DateOnly? ToDate);
