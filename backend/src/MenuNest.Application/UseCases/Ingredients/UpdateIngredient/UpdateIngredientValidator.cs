using FluentValidation;

namespace MenuNest.Application.UseCases.Ingredients.UpdateIngredient;

public sealed class UpdateIngredientValidator : AbstractValidator<UpdateIngredientCommand>
{
    public UpdateIngredientValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(40);
    }
}
