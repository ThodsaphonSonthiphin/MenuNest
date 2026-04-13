using FluentValidation;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

public sealed class CookBatchValidator : AbstractValidator<CookBatchCommand>
{
    public CookBatchValidator()
    {
        RuleFor(x => x.EntryIds)
            .NotEmpty().WithMessage("Select at least one entry to cook.");
        RuleForEach(x => x.EntryIds).NotEqual(Guid.Empty);
    }
}
