using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.DeleteShoppingListItem;

public sealed record DeleteShoppingListItemCommand(Guid ListId, Guid ItemId) : ICommand<Unit>;
