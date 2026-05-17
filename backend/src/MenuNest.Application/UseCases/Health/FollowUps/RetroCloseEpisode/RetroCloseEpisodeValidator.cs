using FluentValidation;

namespace MenuNest.Application.UseCases.Health.FollowUps.RetroCloseEpisode;

public sealed class RetroCloseEpisodeValidator : AbstractValidator<RetroCloseEpisodeCommand>
{
    public RetroCloseEpisodeValidator()
    {
        RuleFor(x => x.EpisodeId).NotEmpty();
        RuleFor(x => x.EstimatedDuration).MaximumLength(64);
    }
}
