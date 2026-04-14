using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;

public sealed record RegenerateShoppingListCommand(Guid Id) : ICommand<ShoppingListDetailDto>;
