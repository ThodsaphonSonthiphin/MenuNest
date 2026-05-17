namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Configuration for the HMAC-signed doctor-report share tokens. Bound
/// from the <c>Share</c> section in <c>appsettings.json</c> (overridden in
/// production via App Service config). Missing/empty
/// <see cref="TokenSigningKey"/> causes <c>HmacShareTokenService</c> to
/// throw at construction so we never silently issue tokens with a weak
/// or default key.
/// </summary>
public sealed class ShareOptions
{
    public const string SectionName = "Share";

    /// <summary>
    /// Base64-encoded HMAC-SHA256 signing key. Recommended size is at
    /// least 32 random bytes. Secret — never leaves the server.
    /// </summary>
    public string? TokenSigningKey { get; set; }

    /// <summary>
    /// JWT issuer claim ("iss"). Used by the verifier to reject tokens
    /// minted by an unrelated system.
    /// </summary>
    public string TokenIssuer { get; set; } = "menunest-share";

    /// <summary>
    /// JWT audience claim ("aud"). Used by the verifier to reject tokens
    /// minted for an unrelated audience.
    /// </summary>
    public string TokenAudience { get; set; } = "menunest-doctor";

    /// <summary>
    /// Optional fully-qualified base URL used to compose the user-visible
    /// share link (<c>{BaseUrl}/share/{token}</c>). When unset the result
    /// falls back to a relative path so the SPA can resolve it against
    /// its own origin.
    /// </summary>
    public string? BaseUrl { get; set; }
}
