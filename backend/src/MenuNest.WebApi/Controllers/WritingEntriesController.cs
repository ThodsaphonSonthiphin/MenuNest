using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class WritingEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WritingEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Submits tonight's 7-minute freewrite entry. Marks the day "done" —
    /// no correction happens here (see docs/decision-map/writing-practice-build).
    /// </summary>
    [HttpPost("api/writing-entries")]
    public async Task<ActionResult<WritingEntryDto>> Submit(
        [FromBody] SubmitWritingEntryCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
