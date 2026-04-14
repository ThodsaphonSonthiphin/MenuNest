using FluentValidation;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed class CreateShoppingListValidator : AbstractValidator<CreateShoppingListCommand>
{
    public CreateShoppingListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        When(x => x.FromDate.HasValue || x.ToDate.HasValue, () =>
        {
            RuleFor(x => x.FromDate).NotNull().WithMessage("Both dates are required when generating from meal plan.");
            RuleFor(x => x.ToDate).NotNull().WithMessage("Both dates are required when generating from meal plan.");
            RuleFor(x => x).Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
                .WithMessage("FromDate must be on or before ToDate.");
        });
    }
}
