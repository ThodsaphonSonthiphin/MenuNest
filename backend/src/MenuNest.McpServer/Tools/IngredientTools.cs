using MenuNest.Application.UseCases.Ingredients;
using MenuNest.Application.UseCases.Ingredients.CreateIngredient;
using MenuNest.Application.UseCases.Ingredients.DeleteIngredient;
using MenuNest.Application.UseCases.Ingredients.ListIngredients;
using MenuNest.Application.UseCases.Ingredients.UpdateIngredient;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class IngredientTools(IMediator mediator)
{
    [McpServerTool, Description("List all ingredients available to the family")]
    public async Task<IReadOnlyList<IngredientDto>> list_ingredients(CancellationToken ct)
        => await mediator.Send(new ListIngredientsQuery(), ct);

    [McpServerTool, Description("Create a new ingredient with a name and unit of measurement")]
    public async Task<IngredientDto> create_ingredient(
        [Description("Ingredient name")] string name,
        [Description("Unit of measurement (e.g. g, ml, pcs)")] string unit,
        CancellationToken ct)
        => await mediator.Send(new CreateIngredientCommand(name, unit), ct);

    [McpServerTool, Description("Update an ingredient's name and unit")]
    public async Task<IngredientDto> update_ingredient(
        [Description("Ingredient ID")] Guid id,
        [Description("New name")] string name,
        [Description("New unit")] string unit,
        CancellationToken ct)
        => await mediator.Send(new UpdateIngredientCommand(id, name, unit), ct);

    [McpServerTool, Description("Delete an ingredient by ID")]
    public async Task delete_ingredient(
        [Description("Ingredient ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteIngredientCommand(id), ct);
}
