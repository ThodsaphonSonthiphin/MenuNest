using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.Share.CreateShareLink;
using MenuNest.Application.UseCases.Health.Share.ListMyShareLinks;
using MenuNest.Application.UseCases.Health.Share.RevokeShareLink;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

/// <summary>
/// Authenticated endpoints the user uses to mint and manage their
/// doctor-share links. The public report endpoint lives on
/// <see cref="PublicReportController"/>.
/// </summary>
[ApiController]
[Route("api/share-links")]
public sealed class ShareLinksController : ControllerBase
{
    private readonly IMediator _mediator;

    public ShareLinksController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<ActionResult<CreateShareLinkResultDto>> Create(
        [FromBody] CreateShareLinkCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShareLinkSummaryDto>>> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyShareLinksQuery(), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RevokeShareLinkCommand(id), ct);
        return NoContent();
    }
}
