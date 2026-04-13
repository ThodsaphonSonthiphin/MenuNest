using Mediator;
using MenuNest.Application.UseCases.Recipes;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Application.UseCases.Recipes.DeleteRecipe;
using MenuNest.Application.UseCases.Recipes.GetRecipe;
using MenuNest.Application.UseCases.Recipes.ListRecipes;
using MenuNest.Application.UseCases.Recipes.UpdateRecipe;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/recipes")]
public sealed class RecipesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RecipesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeSummaryDto>>> List(CancellationToken ct)
    {
        var recipes = await _mediator.Send(new ListRecipesQuery(), ct);
        return Ok(recipes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecipeDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var recipe = await _mediator.Send(new GetRecipeQuery(id), ct);
        return Ok(recipe);
    }

    [HttpPost]
    public async Task<ActionResult<RecipeDetailDto>> Create(
        [FromBody] CreateRecipeRequest request,
        CancellationToken ct)
    {
        var recipe = await _mediator.Send(
            new CreateRecipeCommand(
                request.Name,
                request.Description,
                request.Ingredients),
            ct);
        return CreatedAtAction(nameof(Get), new { id = recipe.Id }, recipe);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RecipeDetailDto>> Update(
        Guid id,
        [FromBody] UpdateRecipeRequest request,
        CancellationToken ct)
    {
        var recipe = await _mediator.Send(
            new UpdateRecipeCommand(
                id,
                request.Name,
                request.Description,
                request.Ingredients),
            ct);
        return Ok(recipe);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRecipeCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateRecipeRequest(
    string Name,
    string? Description,
    IReadOnlyList<RecipeIngredientInput> Ingredients);

public sealed record UpdateRecipeRequest(
    string Name,
    string? Description,
    IReadOnlyList<RecipeIngredientInput> Ingredients);
