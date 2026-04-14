using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;

public sealed record DeleteShoppingListCommand(Guid Id) : ICommand<Unit>;
