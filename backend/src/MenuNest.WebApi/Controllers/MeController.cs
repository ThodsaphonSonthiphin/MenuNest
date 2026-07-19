using Mediator;
using MenuNest.Application.UseCases.Me;
using MenuNest.Application.UseCases.Me.GetMe;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
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

    /// <summary>Returns the current caller's profile.</summary>
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        var me = await _mediator.Send(new GetMeQuery(), ct);
        return Ok(me);
    }

    /// <summary>Updates the current caller's settings (Home page).</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(
        [FromBody] UpdateUserSettingsCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
