using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;

public sealed record UnbuyShoppingListItemCommand(Guid ListId, Guid ItemId)
    : ICommand<ShoppingListItemDto>;
