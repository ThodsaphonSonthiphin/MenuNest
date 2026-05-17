using FluentValidation;

namespace MenuNest.Application.UseCases.Health.Episodes.UpdateEpisode;

public sealed class UpdateEpisodeValidator : AbstractValidator<UpdateEpisodeCommand>
{
    public UpdateEpisodeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Severity)
            .InclusiveBetween(1, 10)
            .When(x => x.Severity.HasValue);
        RuleFor(x => x.AuraDurationMin)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AuraDurationMin.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
