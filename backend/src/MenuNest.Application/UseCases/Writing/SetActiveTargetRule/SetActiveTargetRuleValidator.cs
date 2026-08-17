using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

public sealed class SetActiveTargetRuleValidator : AbstractValidator<SetActiveTargetRuleCommand>
{
    public SetActiveTargetRuleValidator()
    {
        // Blank is legal (it clears the rule). Only the ceiling is enforced —
        // 200 to match WritingEntries.TargetRule, which snapshots it.
        RuleFor(x => x.Rule)
            .MaximumLength(200).WithMessage("Rule must be 200 characters or less.");
    }
}
