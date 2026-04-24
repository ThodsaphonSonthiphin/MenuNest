using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome;

public sealed class SetMonthlyIncomeValidator : AbstractValidator<SetMonthlyIncomeCommand>
{
    public SetMonthlyIncomeValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    }
}
