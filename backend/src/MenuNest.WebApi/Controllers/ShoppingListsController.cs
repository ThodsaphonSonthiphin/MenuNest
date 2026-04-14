using Mediator;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
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

    // POST, DELETE, /complete, /items, /items/{itemId}, /buy, /unbuy, /regenerate
    // are wired in subsequent tasks as the handlers are created.
}
