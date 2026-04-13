using Mediator;
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.ListMealPlan;
using MenuNest.Application.UseCases.MealPlan.StockCheck;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/meal-plan")]
public sealed class MealPlanController : ControllerBase
{
    private readonly IMediator _mediator;

    public MealPlanController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MealPlanEntryDto>>> List(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var entries = await _mediator.Send(new ListMealPlanQuery(from, to), ct);
        return Ok(entries);
    }

    [HttpPost]
    public async Task<ActionResult<MealPlanEntryDto>> Create(
        [FromBody] CreateMealPlanEntryRequest request,
        CancellationToken ct)
    {
        var entry = await _mediator.Send(
            new CreateMealPlanEntryCommand(
                request.Date,
                request.MealSlot,
                request.RecipeId,
                request.Notes),
            ct);
        return CreatedAtAction(nameof(List), new { id = entry.Id }, entry);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MealPlanEntryDto>> Update(
        Guid id,
        [FromBody] UpdateMealPlanEntryRequest request,
        CancellationToken ct)
    {
        var entry = await _mediator.Send(
            new UpdateMealPlanEntryCommand(id, request.RecipeId, request.Notes),
            ct);
        return Ok(entry);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteMealPlanEntryCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/stock-check")]
    public async Task<ActionResult<StockCheckDto>> StockCheck(Guid id, CancellationToken ct)
    {
        var check = await _mediator.Send(new StockCheckQuery(id), ct);
        return Ok(check);
    }

    [HttpPost("stock-check-batch")]
    public async Task<ActionResult<StockCheckBatchDto>> StockCheckBatch(
        [FromBody] StockCheckBatchRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new StockCheckBatchQuery(request.EntryIds), ct);
        return Ok(result);
    }
}

public sealed record CreateMealPlanEntryRequest(
    DateOnly Date,
    MealSlot MealSlot,
    Guid RecipeId,
    string? Notes);

public sealed record UpdateMealPlanEntryRequest(Guid RecipeId, string? Notes);

public sealed record StockCheckBatchRequest(IReadOnlyList<Guid> EntryIds);
