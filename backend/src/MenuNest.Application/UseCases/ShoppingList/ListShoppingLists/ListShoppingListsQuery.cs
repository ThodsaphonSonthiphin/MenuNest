using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;

public sealed record ListShoppingListsQuery(ShoppingListStatus? Status = null)
    : IQuery<IReadOnlyList<ShoppingListDto>>;
