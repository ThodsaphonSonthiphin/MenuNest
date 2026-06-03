namespace MenuNest.WebApi.Oauth;

/// <summary>RFC 9728 (protected resource) + RFC 8414 (authorization server) metadata documents.</summary>
public static class OAuthDiscovery
{
    public static object ProtectedResource(string baseUrl, string clientId) => new
    {
        resource = $"{baseUrl}/mcp",
        authorization_servers = new[] { baseUrl },
        scopes_supported = new[] { $"api://{clientId}/access_as_user", "openid", "profile", "email" },
        bearer_methods_supported = new[] { "header" },
    };

    public static object AuthorizationServer(string baseUrl) => new
    {
        issuer = baseUrl,
        authorization_endpoint = $"{baseUrl}/oauth/authorize",
        token_endpoint = $"{baseUrl}/oauth/token",
        registration_endpoint = $"{baseUrl}/oauth/register",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" },
    };
}
