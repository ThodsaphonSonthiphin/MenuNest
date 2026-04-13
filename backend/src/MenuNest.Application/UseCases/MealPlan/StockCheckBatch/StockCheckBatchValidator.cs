using FluentValidation;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

public sealed class StockCheckBatchValidator : AbstractValidator<StockCheckBatchQuery>
{
    public StockCheckBatchValidator()
    {
        // EntryIds may be empty — the handler returns an empty result so
        // the UI can call this unconditionally as the user toggles
        // checkboxes. We still reject empty Guids to surface client bugs.
        RuleForEach(x => x.EntryIds).NotEqual(Guid.Empty);
    }
}
