using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Oauth;

public static class OAuthEndpoints
{
    private static string BaseUrl(HttpRequest r) => $"{r.Scheme}://{r.Host}";

    public static void MapOAuthProxy(this WebApplication app)
    {
        var clientId = app.Configuration["AzureAd:ClientId"]!;

        // --- Discovery (anonymous) ---
        app.MapGet("/.well-known/oauth-protected-resource",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.ProtectedResource(BaseUrl(r), clientId))).AllowAnonymous();
        app.MapGet("/.well-known/oauth-protected-resource/mcp",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.ProtectedResource(BaseUrl(r), clientId))).AllowAnonymous();
        app.MapGet("/.well-known/oauth-authorization-server",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.AuthorizationServer(BaseUrl(r)))).AllowAnonymous();

        // --- DCR (RFC 7591) ---
        app.MapPost("/oauth/register", (DcrRequest body, ClientStore clients) =>
        {
            var uris = body.redirect_uris ?? Array.Empty<string>();
            if (uris.Length == 0 || uris.Any(u => !RedirectAllowlist.IsAllowed(u)))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var id = clients.Register(body.client_name ?? "mcp-client", uris, body.scope);
            return Results.Created((string?)null, new
            {
                client_id = id,
                client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                token_endpoint_auth_method = "none",
                redirect_uris = uris,
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
            });
        }).AllowAnonymous();
    }

    public record DcrRequest(string[]? redirect_uris, string? client_name, string? scope);
}
