using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

public sealed class SubmitWritingEntryValidator : AbstractValidator<SubmitWritingEntryCommand>
{
    public SubmitWritingEntryValidator()
    {
        RuleFor(x => x.Text).NotEmpty();
        // A generous ceiling for a 7-minute freewrite — guards against an unbounded/
        // runaway client payload without encoding any real product rule about length.
        RuleFor(x => x.Text).MaximumLength(50_000);
        RuleFor(x => x.ElapsedSeconds).GreaterThan(0);
        // A generous ceiling — guards against a garbage/runaway client value
        // without encoding any real product rule about session length.
        RuleFor(x => x.ElapsedSeconds).LessThanOrEqualTo(3600);
    }
}
