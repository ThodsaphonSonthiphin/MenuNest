using FluentValidation;

namespace MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;

public sealed class CreateMealPlanEntryValidator : AbstractValidator<CreateMealPlanEntryCommand>
{
    public CreateMealPlanEntryValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.MealSlot).IsInEnum();
        RuleFor(x => x.RecipeId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
