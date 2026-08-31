using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

public sealed class CoverOverspendingValidator : AbstractValidator<CoverOverspendingCommand>
{
    public CoverOverspendingValidator()
    {
        // menunest-215: a NULL source means Ready to Assign and is legal. An
        // explicitly EMPTY Guid is not — that is a caller that meant to name an
        // envelope and sent nothing, and letting it through would silently
        // create money out of the derived figure instead of failing loudly.
        RuleFor(x => x.FromCategoryId)
            .Must(id => id != Guid.Empty)
            .WithMessage("Source category must be a real envelope, or null for Ready to Assign.");
        RuleFor(x => x.OverspentCategoryId).NotEmpty()
            .Must((cmd, overspent) => overspent != cmd.FromCategoryId)
            .WithMessage("Source and overspent category must differ.");
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
