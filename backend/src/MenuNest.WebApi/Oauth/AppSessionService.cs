using System.Security.Claims;

namespace MenuNest.WebApi.Oauth;

/// <summary>The token pair handed to the SPA. Field names match the JSON the client reads.</summary>
public sealed record AppSessionTokens(string AccessToken, int ExpiresIn, string RefreshToken);

/// <summary>
/// Mints and rotates the SPA's app session (ADR-161). Refresh is deliberately
/// self-contained: it re-mints from the stored subject and never calls Entra or
/// Google, so one mechanism serves both sign-in buttons (ADR-160).
/// </summary>
public sealed class AppSessionService(AppSessionStore sessions, OAuthJwt jwt)
{
    public const string ClientId = "menunest-spa";
    private const int AccessTokenSeconds = 3600;

    public async Task<AppSessionTokens> IssueAsync(
        string subject, string? name, string? email, CancellationToken ct = default)
    {
        var extra = new List<Claim>();
        if (name is not null) extra.Add(new Claim("name", name));
        if (email is not null)
        {
            extra.Add(new Claim("email", email));
            extra.Add(new Claim("preferred_username", email));
        }

        var accessToken = jwt.Mint(subject, ClientId, string.Empty, extra, AccessTokenSeconds);
        var refreshCode = await sessions.IssueAsync(subject, ct);
        return new AppSessionTokens(accessToken, AccessTokenSeconds, refreshCode);
    }

    /// <summary>
    /// Rotates the session. Name/email are not carried across a refresh on purpose:
    /// they are only ever read to provision a NEW user, and provisioning happens at
    /// exchange time while the real provider token is still on the request.
    /// </summary>
    public async Task<AppSessionTokens?> RefreshAsync(string refreshCode, CancellationToken ct = default)
    {
        var subject = await sessions.TakeAsync(refreshCode, ct);
        if (subject is null) return null;
        return await IssueAsync(subject, name: null, email: null, ct);
    }
}
