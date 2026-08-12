using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.Auth;

/// <summary>
/// Chooses the JWT bearer scheme for an incoming Authorization header. Extracted
/// from Program.cs so the branching is unit-testable (issue #5).
/// </summary>
public static class BearerSchemeSelector
{
    public const string Google = "Google";
    public const string Microsoft = "Microsoft";

    /// <summary>
    /// Scheme for tokens this app minted itself. Named "McpProxy" because it is the
    /// existing scheme already configured with OAuthJwt.ValidationParameters(); the
    /// SPA's app session (ADR-161) is validated by exactly the same parameters.
    /// </summary>
    public const string AppIssued = "McpProxy";

    /// <param name="appIssuer">MCP:ServerUrl verbatim — the issuer OAuthJwt stamps.</param>
    public static string Select(string? authorizationHeader, string appIssuer)
    {
        if (authorizationHeader is null
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Microsoft;
        }

        var token = authorizationHeader["Bearer ".Length..];
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return Microsoft;

        var issuer = handler.ReadJwtToken(token).Issuer;
        if (issuer == "https://accounts.google.com") return Google;
        if (issuer == appIssuer) return AppIssued;
        return Microsoft;
    }
}
