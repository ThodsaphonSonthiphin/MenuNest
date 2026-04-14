using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;

public sealed record AddShoppingListItemCommand(
    Guid ListId,
    Guid IngredientId,
    decimal Quantity) : ICommand<ShoppingListItemDto>;
