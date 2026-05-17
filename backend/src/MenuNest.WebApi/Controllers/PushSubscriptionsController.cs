using Mediator;
using MenuNest.Application.UseCases.Health.PushSubscriptions.SubscribeWebPush;
using MenuNest.Application.UseCases.Health.PushSubscriptions.UnsubscribeWebPush;
using MenuNest.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MenuNest.WebApi.Controllers;

/// <summary>
/// Manages browser <c>PushSubscription</c> rows for the current user
/// and exposes the VAPID public key so the SPA can initialise
/// <c>PushManager.subscribe</c>.
/// </summary>
[ApiController]
[Route("api/push-subscriptions")]
public sealed class PushSubscriptionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly WebPushOptions _options;

    public PushSubscriptionsController(
        IMediator mediator,
        IOptions<WebPushOptions> options)
    {
        _mediator = mediator;
        _options = options.Value;
    }

    /// <summary>
    /// Registers a Service Worker push subscription for the current user.
    /// Body fields mirror <c>PushSubscription.toJSON()</c>.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SubscribeWebPushResultDto>> Subscribe(
        [FromBody] SubscribeWebPushCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Removes a push subscription by endpoint. Idempotent.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Unsubscribe(
        [FromBody] UnsubscribeWebPushRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new UnsubscribeWebPushCommand(request.Endpoint), ct);
        return NoContent();
    }

    /// <summary>
    /// Returns the VAPID public key the SPA needs to call
    /// <c>PushManager.subscribe({ applicationServerKey })</c>. Anonymous
    /// so the SPA can fetch it before the user signs in — the key is
    /// already public by design (it's embedded in subscription requests).
    /// </summary>
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public ActionResult<VapidPublicKeyDto> GetVapidPublicKey()
    {
        return Ok(new VapidPublicKeyDto(_options.VapidPublicKey ?? string.Empty));
    }
}

public sealed record UnsubscribeWebPushRequest(string Endpoint);

public sealed record VapidPublicKeyDto(string PublicKey);
