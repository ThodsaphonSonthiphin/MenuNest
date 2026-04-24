using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;

public sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).NotEqual(0m);
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
