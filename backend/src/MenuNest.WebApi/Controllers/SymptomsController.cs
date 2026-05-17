using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.Symptoms.CreateCustomSymptom;
using MenuNest.Application.UseCases.Health.Symptoms.ListSymptoms;
using MenuNest.Application.UseCases.Health.Triggers.CreateCustomTrigger;
using MenuNest.Application.UseCases.Health.Triggers.ListTriggers;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class SymptomsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SymptomsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/symptoms")]
    public async Task<ActionResult<IReadOnlyList<SymptomDto>>> ListSymptoms(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSymptomsQuery(), ct);
        return Ok(result);
    }

    [HttpPost("api/symptoms")]
    public async Task<ActionResult<SymptomDto>> CreateSymptom(
        [FromBody] CreateCustomSymptomCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(ListSymptoms), null, result);
    }

    [HttpGet("api/triggers")]
    public async Task<ActionResult<IReadOnlyList<TriggerDto>>> ListTriggers(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTriggersQuery(), ct);
        return Ok(result);
    }

    [HttpPost("api/triggers")]
    public async Task<ActionResult<TriggerDto>> CreateTrigger(
        [FromBody] CreateCustomTriggerCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(ListTriggers), null, result);
    }
}
