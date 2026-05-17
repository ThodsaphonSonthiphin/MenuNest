using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.FollowUps.RecordPingResponse;

/// <summary>
/// Records the user's response to a follow-up ping. Drives the episode
/// state machine: a <see cref="PingResponse.Resolved"/> answer closes
/// the episode, while <see cref="PingResponse.Improved"/>/
/// <see cref="PingResponse.Same"/>/<see cref="PingResponse.Worse"/>
/// schedules another check-in at +30 min (capped at 3 total pings per
/// episode to avoid spam).
/// </summary>
public sealed record RecordPingResponseCommand(
    Guid PingId,
    PingResponse Response,
    int? SeverityAtCheck = null) : ICommand<Unit>;
