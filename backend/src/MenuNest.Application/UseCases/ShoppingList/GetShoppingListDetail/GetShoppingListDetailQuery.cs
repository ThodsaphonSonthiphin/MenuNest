using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;

public sealed record GetShoppingListDetailQuery(Guid Id) : IQuery<ShoppingListDetailDto>;
