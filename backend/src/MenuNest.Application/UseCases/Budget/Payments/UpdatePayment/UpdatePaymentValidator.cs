using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;

public sealed class UpdatePaymentValidator : AbstractValidator<UpdatePaymentCommand>
{
    public UpdatePaymentValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m)
            .WithMessage("Payment amount must be positive.");
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.ToAccountId).NotEmpty()
            .NotEqual(x => x.FromAccountId)
            .WithMessage("An account cannot pay itself.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
