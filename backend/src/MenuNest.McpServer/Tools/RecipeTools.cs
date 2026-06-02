using MenuNest.Application.UseCases.Recipes;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Application.UseCases.Recipes.DeleteRecipe;
using MenuNest.Application.UseCases.Recipes.GetRecipe;
using MenuNest.Application.UseCases.Recipes.ListRecipes;
using MenuNest.Application.UseCases.Recipes.UpdateRecipe;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class RecipeTools(IMediator mediator)
{
    [McpServerTool, Description("List all recipes in the family")]
    public async Task<IReadOnlyList<RecipeSummaryDto>> list_recipes(CancellationToken ct)
        => await mediator.Send(new ListRecipesQuery(), ct);

    [McpServerTool, Description("Get full details of a recipe by ID")]
    public async Task<RecipeDetailDto> get_recipe(
        [Description("Recipe ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new GetRecipeQuery(id), ct);

    [McpServerTool, Description("Create a new recipe with ingredients")]
    public async Task<RecipeDetailDto> create_recipe(
        [Description("Recipe name")] string name,
        [Description("Optional description")] string? description,
        [Description("Ingredient list — each item needs ingredientId and quantity")] RecipeIngredientInput[] ingredients,
        CancellationToken ct)
        => await mediator.Send(new CreateRecipeCommand(name, description, ingredients), ct);

    [McpServerTool, Description("Update an existing recipe")]
    public async Task<RecipeDetailDto> update_recipe(
        [Description("Recipe ID")] Guid id,
        [Description("New name")] string name,
        [Description("New description (optional)")] string? description,
        [Description("Updated ingredient list")] RecipeIngredientInput[] ingredients,
        CancellationToken ct)
        => await mediator.Send(new UpdateRecipeCommand(id, name, description, ingredients), ct);

    [McpServerTool, Description("Delete a recipe by ID")]
    public async Task delete_recipe(
        [Description("Recipe ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteRecipeCommand(id), ct);
}
