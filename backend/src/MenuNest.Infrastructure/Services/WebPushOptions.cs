namespace MenuNest.Infrastructure.Services;

/// <summary>
/// VAPID configuration bound from the <c>Push</c> section in
/// <c>appsettings.json</c> (and overridden via App Service config in
/// production). All three values are required for the
/// <see cref="WebPushSender"/> to actually send notifications — missing
/// keys cause the sender to log a warning and skip transport (it never
/// throws so the dispatcher stays healthy).
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "Push";

    /// <summary>
    /// The VAPID public key (base64url, 65 bytes uncompressed P-256). The
    /// frontend also receives this via the <c>/api/push-subscriptions/vapid-public-key</c>
    /// endpoint so the Service Worker can register a subscription with the
    /// same key the backend signs pushes with.
    /// </summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>
    /// The VAPID private key (base64url, 32 bytes scalar). Secret —
    /// never leaves the server. Set via App Service config in production.
    /// </summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>
    /// Contact subject embedded in the VAPID JWT — must be a
    /// <c>mailto:</c> or <c>https:</c> URL identifying the push origin.
    /// Push services use this to reach the operator if a subscription
    /// causes problems.
    /// </summary>
    public string VapidSubject { get; set; } = "mailto:admin@menunest.com";
}
