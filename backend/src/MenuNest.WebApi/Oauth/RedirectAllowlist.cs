namespace MenuNest.WebApi.Oauth;

/// <summary>Exact-match allowlist of MCP client callback URLs (claude.ai / claude.com).</summary>
public static class RedirectAllowlist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    };

    public static bool IsAllowed(string? redirectUri)
        => redirectUri is not null && Allowed.Contains(redirectUri);
}
