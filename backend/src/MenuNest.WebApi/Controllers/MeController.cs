using Mediator;
using MenuNest.Application.UseCases.Me;
using MenuNest.Application.UseCases.Me.GetMe;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
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

    /// <summary>
    /// Sets the caller's active target grammar rule — the in-app half of
    /// mcp-tool-contract's set_active_target_rule (the MCP tool writes the same
    /// value). Deliberately separate from PUT settings, whose full-snapshot
    /// body does not carry the rule.
    /// </summary>
    [HttpPut("target-rule")]
    public async Task<ActionResult<ActiveTargetRuleResponse>> SetTargetRule(
        [FromBody] SetTargetRuleRequest request, CancellationToken ct)
    {
        var rule = await _mediator.Send(new SetActiveTargetRuleCommand(request.Rule), ct);
        return Ok(new ActiveTargetRuleResponse(rule));
    }
}

public sealed record SetTargetRuleRequest(string? Rule);

public sealed record ActiveTargetRuleResponse(string? ActiveTargetRule);
