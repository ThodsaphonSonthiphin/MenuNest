using Mediator;

namespace MenuNest.Application.UseCases.Health.PushSubscriptions.SubscribeWebPush;

/// <summary>
/// Persists a browser <c>PushSubscription</c> for the current user.
/// The fields map directly to <c>PushSubscription.toJSON()</c> in the
/// Service Worker registration result. Idempotent — re-subscribing the
/// same endpoint returns the existing row's id.
/// </summary>
public sealed record SubscribeWebPushCommand(
    string Endpoint,
    string P256dh,
    string Auth,
    DateTime? ExpiresAt) : ICommand<SubscribeWebPushResultDto>;

/// <summary>
/// Returns the persisted subscription's id so the frontend can correlate
/// later unsubscribe calls.
/// </summary>
public sealed record SubscribeWebPushResultDto(Guid Id);
