using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A browser push subscription (Web Push API). One user can have many —
/// one per browser/device combination. Subscriptions are passed verbatim
/// from <c>PushManager.subscribe()</c> in the client.
/// </summary>
public sealed class WebPushSubscription : Entity
{
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; } = null!;
    public string P256dh { get; private set; } = null!;
    public string Auth { get; private set; } = null!;
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public DateTime? LastFailureAt { get; private set; }

    // EF Core
    private WebPushSubscription() { }

    public static WebPushSubscription Create(
        Guid userId,
        string endpoint,
        string p256dh,
        string auth,
        DateTime? expiresAt = null)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new DomainException("Push endpoint is required.");
        if (string.IsNullOrWhiteSpace(p256dh))
            throw new DomainException("p256dh key is required.");
        if (string.IsNullOrWhiteSpace(auth))
            throw new DomainException("auth secret is required.");

        return new WebPushSubscription
        {
            UserId = userId,
            Endpoint = endpoint.Trim(),
            P256dh = p256dh.Trim(),
            Auth = auth.Trim(),
            ExpiresAt = expiresAt
        };
    }

    public void RecordSuccess()
    {
        LastSuccessAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordFailure()
    {
        LastFailureAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
