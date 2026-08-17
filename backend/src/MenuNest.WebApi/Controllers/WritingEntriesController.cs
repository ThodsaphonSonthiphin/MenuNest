using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.DeleteWritingEntry;
using MenuNest.Application.UseCases.Writing.ListWritingEntries;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;
using MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/writing-entries")]
public sealed class WritingEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WritingEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Submits tonight's 7-minute freewrite entry. Marks the day "done" --
    /// no correction happens here (see docs/decision-map/writing-practice-build).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WritingEntryDto>> Submit(
        [FromBody] SubmitWritingEntryCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lists every non-deleted entry for the current user, newest first --
    /// feeds the "ประวัติ" (History) screen.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WritingEntryDto>>> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListWritingEntriesQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Edits an entry's text. Rejected once a correction has locked it
    /// (entry-mutability / ADR-169).
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WritingEntryDto>> UpdateText(
        Guid id,
        [FromBody] UpdateWritingEntryTextRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateWritingEntryTextCommand(id, request.Text), ct);
        return Ok(result);
    }

    /// <summary>
    /// Soft-deletes an entry -- allowed even when its text is locked.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteWritingEntryCommand(id), ct);
        return NoContent();
    }
}

public sealed record UpdateWritingEntryTextRequest(string Text);
