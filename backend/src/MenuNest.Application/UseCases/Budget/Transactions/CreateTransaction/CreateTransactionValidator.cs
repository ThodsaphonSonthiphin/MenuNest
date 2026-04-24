using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;

public sealed class CreateTransactionValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).NotEqual(0m);
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
