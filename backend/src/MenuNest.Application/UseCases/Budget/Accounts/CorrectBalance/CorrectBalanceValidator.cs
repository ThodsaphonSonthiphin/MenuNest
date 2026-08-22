using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Accounts.CorrectBalance;

public sealed class CorrectBalanceValidator : AbstractValidator<CorrectBalanceCommand>
{
    public CorrectBalanceValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
    }
}
