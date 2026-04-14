using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.CompleteShoppingList;

public sealed record CompleteShoppingListCommand(Guid Id) : ICommand<ShoppingListDto>;
