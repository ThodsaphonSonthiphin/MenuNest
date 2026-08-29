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

    /// <summary>
    /// Pushes a plain title/body to every active subscription belonging to
    /// <paramref name="userId"/>. Returns the count reached — 0 when the user
    /// has granted no permission, which is a normal outcome, not an error.
    ///
    /// <para><paramref name="url"/> is where tapping the notification lands;
    /// without it the Service Worker falls back to /health, which is the wrong
    /// destination for anything that is not a follow-up ping.</para>
    ///
    /// <para>Added for menunest-201: when the family head undoes a member's
    /// change, that member is told. Best-effort by design — requiring push
    /// would block a legitimate correction on a permission the member may
    /// never have granted.</para>
    /// </summary>
    Task<int> SendToUserAsync(Guid userId, string title, string body, string url, CancellationToken ct = default);
}
