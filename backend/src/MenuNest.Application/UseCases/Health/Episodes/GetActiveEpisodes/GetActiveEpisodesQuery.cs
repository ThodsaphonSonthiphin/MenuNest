using Mediator;

namespace MenuNest.Application.UseCases.Health.Episodes.GetActiveEpisodes;

/// <summary>
/// Returns the current user's active (not-yet-ended) symptom episodes
/// in start-time-descending order. Powers the Home page "active banner".
/// </summary>
public sealed record GetActiveEpisodesQuery() : IQuery<IReadOnlyList<EpisodeDto>>;
