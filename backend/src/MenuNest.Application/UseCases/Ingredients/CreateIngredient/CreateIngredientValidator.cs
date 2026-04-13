using FluentValidation;

namespace MenuNest.Application.UseCases.Ingredients.CreateIngredient;

public sealed class CreateIngredientValidator : AbstractValidator<CreateIngredientCommand>
{
    public CreateIngredientValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ingredient name is required.")
            .MaximumLength(120);

        RuleFor(x => x.Unit)
            .NotEmpty().WithMessage("Ingredient unit is required.")
            .MaximumLength(40);
    }
}
