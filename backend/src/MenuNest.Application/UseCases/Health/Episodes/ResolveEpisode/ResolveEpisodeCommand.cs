using Mediator;

namespace MenuNest.Application.UseCases.Health.Episodes.ResolveEpisode;

/// <summary>
/// Marks an active episode as resolved. Also cancels any pending
/// follow-up pings so the dispatcher won't send them.
/// </summary>
public sealed record ResolveEpisodeCommand(
    Guid Id,
    int SeverityAfter = 0,
    DateTime? EndedAt = null) : ICommand<EpisodeDetailDto>;
