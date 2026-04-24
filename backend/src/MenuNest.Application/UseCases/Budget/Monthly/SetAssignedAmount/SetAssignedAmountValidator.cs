using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;

public sealed class SetAssignedAmountValidator : AbstractValidator<SetAssignedAmountCommand>
{
    public SetAssignedAmountValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        // Amount may be negative — see MonthlyAssignment doc comment:
        // move-money and cover-overspending flows can drive the envelope below zero.
    }
}
