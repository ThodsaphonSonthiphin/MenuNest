using FluentValidation;

namespace MenuNest.Application.UseCases.Health.Episodes.StartEpisode;

public sealed class StartEpisodeValidator : AbstractValidator<StartEpisodeCommand>
{
    public StartEpisodeValidator()
    {
        RuleFor(x => x.SymptomId).NotEmpty();
        RuleFor(x => x.Severity).InclusiveBetween(1, 10);
        RuleFor(x => x.AuraDurationMin)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AuraDurationMin.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
