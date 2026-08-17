using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

public sealed class RecordWritingCorrectionValidator : AbstractValidator<RecordWritingCorrectionCommand>
{
    public RecordWritingCorrectionValidator()
    {
        RuleFor(x => x.EntryId).NotEmpty();

        RuleFor(x => x.TargetRule).NotEmpty()
            .MaximumLength(200).WithMessage("TargetRule must be 200 characters or less.");

        // Same ceiling as the entry Text it annotates (and markedText is always
        // longer than that text).
        RuleFor(x => x.MarkedText).NotEmpty()
            .MaximumLength(50_000).WithMessage("MarkedText must be 50,000 characters or less.");

        RuleFor(x => x.ThaiWhyLine).NotEmpty()
            .MaximumLength(2000).WithMessage("ThaiWhyLine must be 2,000 characters or less.");

        RuleFor(x => x.HitCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MissCount).GreaterThanOrEqualTo(0);

        // The contract asks for 3-4 items, but the minimum is NOT enforced: a
        // Thai-only night has no English sentences to combine (and the sole real
        // prod entry is exactly that). Only the upper bound is a rule.
        // Cascade(Stop) is required here: FluentValidation's default rule-level
        // cascade mode is Continue, so without it .Must() still runs after
        // .NotNull() fails and a null collection throws NullReferenceException
        // (from items.Count) instead of producing a clean ValidationException.
        RuleFor(x => x.SentenceCombiningItems).Cascade(CascadeMode.Stop).NotNull()
            .Must(items => items.Count <= 4)
            .WithMessage("SentenceCombiningItems must contain 4 items or fewer.");
        RuleForEach(x => x.SentenceCombiningItems).ChildRules(item =>
        {
            item.RuleFor(i => i.Source).NotEmpty().MaximumLength(1000);
            item.RuleFor(i => i.Combined).NotEmpty().MaximumLength(1000);
        });

        RuleFor(x => x.StuckWords).Cascade(CascadeMode.Stop).NotNull()
            .Must(words => words.Count <= 50)
            .WithMessage("StuckWords must contain 50 items or fewer.");
        RuleForEach(x => x.StuckWords).ChildRules(word =>
        {
            word.RuleFor(w => w.Thai).NotEmpty().MaximumLength(200);
            word.RuleFor(w => w.English).NotEmpty().MaximumLength(200);
        });
    }
}
