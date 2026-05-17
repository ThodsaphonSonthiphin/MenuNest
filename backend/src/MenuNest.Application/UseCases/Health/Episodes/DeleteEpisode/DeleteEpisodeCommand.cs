using Mediator;

namespace MenuNest.Application.UseCases.Health.Episodes.DeleteEpisode;

public sealed record DeleteEpisodeCommand(Guid Id) : ICommand;
