using MenuNest.Domain.Entities;

namespace MenuNest.Application.Abstractions;

/// <summary>
/// Sends Web Push notifications via VAPID. Implementation in Infrastructure
/// wraps the <c>WebPush</c> NuGet package. <c>FollowUpDispatcher</c>
/// background service is the primary caller.
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    /// Pushes the payload to every active subscription registered by the user
    /// who owns <paramref name="ping"/>'s episode. Returns the count of
    /// subscriptions successfully reached (0 if user has no devices subscribed
    /// or all sends failed).
    /// </summary>
    Task<int> SendFollowUpAsync(FollowUpPing ping, CancellationToken ct = default);
}
