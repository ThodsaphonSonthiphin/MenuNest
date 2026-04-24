using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

public sealed class CoverOverspendingValidator : AbstractValidator<CoverOverspendingCommand>
{
    public CoverOverspendingValidator()
    {
        RuleFor(x => x.FromCategoryId).NotEmpty();
        RuleFor(x => x.OverspentCategoryId).NotEmpty()
            .Must((cmd, overspent) => overspent != cmd.FromCategoryId)
            .WithMessage("Source and overspent category must differ.");
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
