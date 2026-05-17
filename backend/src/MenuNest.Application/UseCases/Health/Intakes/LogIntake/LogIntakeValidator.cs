using FluentValidation;

namespace MenuNest.Application.UseCases.Health.Intakes.LogIntake;

public sealed class LogIntakeValidator : AbstractValidator<LogIntakeCommand>
{
    public LogIntakeValidator()
    {
        RuleFor(x => x.DrugId).NotEmpty();
        RuleFor(x => x.DoseAmount).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
