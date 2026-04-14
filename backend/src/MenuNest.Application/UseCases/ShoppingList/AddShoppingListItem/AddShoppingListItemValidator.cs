using FluentValidation;

namespace MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;

public sealed class AddShoppingListItemValidator : AbstractValidator<AddShoppingListItemCommand>
{
    public AddShoppingListItemValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0m);
    }
}
