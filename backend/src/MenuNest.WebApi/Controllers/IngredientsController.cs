using Mediator;
using MenuNest.Application.UseCases.Ingredients;
using MenuNest.Application.UseCases.Ingredients.CreateIngredient;
using MenuNest.Application.UseCases.Ingredients.DeleteIngredient;
using MenuNest.Application.UseCases.Ingredients.ListIngredients;
using MenuNest.Application.UseCases.Ingredients.UpdateIngredient;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/ingredients")]
public sealed class IngredientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IngredientsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> List(CancellationToken ct)
    {
        var items = await _mediator.Send(new ListIngredientsQuery(), ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<IngredientDto>> Create(
        [FromBody] CreateIngredientRequest request,
        CancellationToken ct)
    {
        var ingredient = await _mediator.Send(
            new CreateIngredientCommand(request.Name, request.Unit),
            ct);
        return CreatedAtAction(nameof(List), new { id = ingredient.Id }, ingredient);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<IngredientDto>> Update(
        Guid id,
        [FromBody] UpdateIngredientRequest request,
        CancellationToken ct)
    {
        var ingredient = await _mediator.Send(
            new UpdateIngredientCommand(id, request.Name, request.Unit),
            ct);
        return Ok(ingredient);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteIngredientCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateIngredientRequest(string Name, string Unit);
public sealed record UpdateIngredientRequest(string Name, string Unit);
