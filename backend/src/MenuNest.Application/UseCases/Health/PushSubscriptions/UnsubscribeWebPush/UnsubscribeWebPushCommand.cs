using Mediator;

namespace MenuNest.Application.UseCases.Health.PushSubscriptions.UnsubscribeWebPush;

/// <summary>
/// Removes a push subscription identified by its push-service endpoint
/// for the current user. Idempotent — returns success even if no row
/// matched (the frontend calls this from a Service Worker unregister
/// flow and shouldn't fail when the row is already gone).
/// </summary>
public sealed record UnsubscribeWebPushCommand(string Endpoint) : ICommand;
