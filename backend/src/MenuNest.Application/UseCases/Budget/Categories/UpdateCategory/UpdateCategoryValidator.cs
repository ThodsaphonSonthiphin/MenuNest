using FluentValidation;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Emoji).MaximumLength(8);
        RuleFor(x => x.TargetAmount)
            .NotNull().GreaterThan(0)
            .When(x => x.TargetType == BudgetTargetType.MonthlyAmount
                    || x.TargetType == BudgetTargetType.ByDate
                    || x.TargetType == BudgetTargetType.MonthlySavingsBuilder);
        RuleFor(x => x.TargetDueDate)
            .NotNull().When(x => x.TargetType == BudgetTargetType.ByDate);
    }
}
