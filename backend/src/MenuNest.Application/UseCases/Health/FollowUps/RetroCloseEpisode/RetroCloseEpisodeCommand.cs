using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.FollowUps.RetroCloseEpisode;

/// <summary>
/// Retro-closes an episode the user forgot to mark resolved live.
/// Triggered by the retro-close modal that pops up on next app open
/// when the user has missed 3+ pings.
/// </summary>
/// <param name="EpisodeId">The episode to close.</param>
/// <param name="EstimatedDuration">User-supplied free-text duration
/// estimate ("within_1h", "1_to_3h", "hours", "not_sure"). Persisted
/// verbatim on the episode.</param>
/// <param name="Outcome">Must be <see cref="PingResponse.RetroResolved"/>
/// or <see cref="PingResponse.RetroUnknown"/>. Any other value is
/// rejected so live-pings cannot back-door into retro-close.</param>
public sealed record RetroCloseEpisodeCommand(
    Guid EpisodeId,
    string? EstimatedDuration,
    PingResponse Outcome) : ICommand<Unit>;
