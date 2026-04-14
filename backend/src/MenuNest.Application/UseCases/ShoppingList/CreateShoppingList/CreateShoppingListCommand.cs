using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed record CreateShoppingListCommand(
    string Name,
    DateOnly? FromDate,
    DateOnly? ToDate) : ICommand<ShoppingListDto>;
