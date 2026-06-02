using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.CompleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;
using MenuNest.Domain.Enums;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class ShoppingListTools(IMediator mediator)
{
    [McpServerTool, Description("List shopping lists, optionally filtered by status")]
    public async Task<IReadOnlyList<ShoppingListDto>> list_shopping_lists(
        [Description("Optional status filter: Active, Completed, or Archived")] ShoppingListStatus? status,
        CancellationToken ct)
        => await mediator.Send(new ListShoppingListsQuery(status), ct);

    [McpServerTool, Description("Get full details of a shopping list including all items")]
    public async Task<ShoppingListDetailDto> get_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new GetShoppingListDetailQuery(id), ct);

    [McpServerTool, Description("Create a new shopping list, optionally generated from a meal plan date range. Dates must be supplied as a pair or omitted entirely.")]
    public async Task<ShoppingListDto> create_shopping_list(
        [Description("Name for the shopping list")] string name,
        [Description("Optional start date for meal plan generation — must be paired with toDate")] DateOnly? fromDate,
        [Description("Optional end date for meal plan generation — must be paired with fromDate")] DateOnly? toDate,
        CancellationToken ct)
        => await mediator.Send(new CreateShoppingListCommand(name, fromDate, toDate), ct);

    [McpServerTool, Description("Delete a shopping list by ID")]
    public async Task delete_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteShoppingListCommand(id), ct);

    [McpServerTool, Description("Mark a shopping list as completed")]
    public async Task<ShoppingListDto> complete_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new CompleteShoppingListCommand(id), ct);

    [McpServerTool, Description("Add an ingredient to the shopping list. If the ingredient already exists, its quantity is increased by the given amount (not replaced).")]
    public async Task<ShoppingListItemDto> add_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Ingredient ID")] Guid ingredientId,
        [Description("Quantity to purchase")] decimal quantity,
        CancellationToken ct)
        => await mediator.Send(new AddShoppingListItemCommand(listId, ingredientId, quantity), ct);

    [McpServerTool, Description("Remove an item from a shopping list")]
    public async Task delete_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Shopping list item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new DeleteShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Mark a shopping list item as bought")]
    public async Task<ShoppingListItemDto> buy_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Shopping list item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new BuyShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Unmark a shopping list item as bought (revert to not bought)")]
    public async Task<ShoppingListItemDto> unbuy_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Shopping list item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new UnbuyShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Regenerate a shopping list from its linked meal plan entries. All unbought items are removed and replaced — manually added unbought items will be lost.")]
    public async Task<ShoppingListDetailDto> regenerate_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new RegenerateShoppingListCommand(id), ct);
}
