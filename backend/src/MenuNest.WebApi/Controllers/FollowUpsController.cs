using Mediator;
using MenuNest.Application.UseCases.Health.FollowUps.RecordPingResponse;
using MenuNest.Application.UseCases.Health.FollowUps.RetroCloseEpisode;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class FollowUpsController : ControllerBase
{
    private readonly IMediator _mediator;

    public FollowUpsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Records a follow-up ping response. Service-worker actions and
    /// in-app modal both POST here. <c>Resolved</c> closes the episode;
    /// <c>Improved</c>/<c>Same</c>/<c>Worse</c> schedules another ping.
    /// </summary>
    [HttpPost("api/followups/{pingId:guid}/respond")]
    public async Task<IActionResult> Respond(
        Guid pingId,
        [FromBody] RecordPingResponseRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(
            new RecordPingResponseCommand(pingId, request.Response, request.SeverityAtCheck),
            ct);
        return NoContent();
    }

    /// <summary>
    /// Retro-closes an episode the user forgot to mark resolved live.
    /// Triggered by the retro-close modal that pops up on next app open.
    /// </summary>
    [HttpPost("api/episodes/{episodeId:guid}/retro-close")]
    public async Task<IActionResult> RetroClose(
        Guid episodeId,
        [FromBody] RetroCloseEpisodeRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(
            new RetroCloseEpisodeCommand(episodeId, request.EstimatedDuration, request.Outcome),
            ct);
        return NoContent();
    }
}

public sealed record RecordPingResponseRequest(
    PingResponse Response,
    int? SeverityAtCheck = null);

public sealed record RetroCloseEpisodeRequest(
    string? EstimatedDuration,
    PingResponse Outcome);
