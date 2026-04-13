using FluentValidation;

namespace MenuNest.Application.UseCases.Recipes.CreateRecipe;

public sealed class CreateRecipeValidator : AbstractValidator<CreateRecipeCommand>
{
    public CreateRecipeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleForEach(x => x.Ingredients).ChildRules(line =>
        {
            line.RuleFor(l => l.IngredientId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
        });
    }
}
