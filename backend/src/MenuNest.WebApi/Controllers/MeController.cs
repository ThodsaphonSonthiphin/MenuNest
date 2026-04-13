using Mediator;
using MenuNest.Application.UseCases.Me;
using MenuNest.Application.UseCases.Me.GetMe;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the current caller's profile. Auto-provisions a
    /// <c>User</c> row on first sign-in.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        var me = await _mediator.Send(new GetMeQuery(), ct);
        return Ok(me);
    }
}
