using Mediator;

namespace MenuNest.Application.UseCases.Health.Episodes.GetEpisode;

public sealed record GetEpisodeQuery(Guid Id) : IQuery<EpisodeDetailDto>;
