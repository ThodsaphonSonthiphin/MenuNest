using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

public sealed class SubmitWritingEntryValidator : AbstractValidator<SubmitWritingEntryCommand>
{
    public SubmitWritingEntryValidator()
    {
        RuleFor(x => x.Text).NotEmpty();
        RuleFor(x => x.ElapsedSeconds).GreaterThan(0);
        // A generous ceiling — guards against a garbage/runaway client value
        // without encoding any real product rule about session length.
        RuleFor(x => x.ElapsedSeconds).LessThanOrEqualTo(3600);
    }
}
