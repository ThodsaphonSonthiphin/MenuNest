using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;

public sealed record BuyShoppingListItemCommand(Guid ListId, Guid ItemId)
    : ICommand<ShoppingListItemDto>;
