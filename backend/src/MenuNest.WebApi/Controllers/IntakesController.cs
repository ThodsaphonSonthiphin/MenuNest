using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.Intakes.GetTakeMedicationContext;
using MenuNest.Application.UseCases.Health.Intakes.LogIntake;
using MenuNest.Application.UseCases.Health.Intakes.LogNoDrug;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class IntakesController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntakesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Records a medication intake, optionally linked to an active episode.
    /// </summary>
    [HttpPost("api/intakes")]
    public async Task<ActionResult<IntakeDto>> LogIntake(
        [FromBody] LogIntakeCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Records a "no drug" decision for an active episode and schedules a
    /// self-resolving follow-up. Routed under episodes for URL clarity.
    /// </summary>
    [HttpPost("api/episodes/{episodeId:guid}/no-drug")]
    public async Task<IActionResult> LogNoDrug(
        Guid episodeId,
        [FromBody] LogNoDrugRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new LogNoDrugCommand(episodeId, request.Reason), ct);
        return NoContent();
    }

    /// <summary>
    /// Returns the Take Medication picker's 3-category context for the
    /// given episode (active / takeable / blocked drugs).
    /// </summary>
    [HttpGet("api/episodes/{episodeId:guid}/take-medication-context")]
    public async Task<ActionResult<TakeMedicationContextDto>> GetTakeMedicationContext(
        Guid episodeId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTakeMedicationContextQuery(episodeId), ct);
        return Ok(result);
    }
}

public sealed record LogNoDrugRequest(NoDrugReason Reason);
