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

        // --- Authorize: validate, store flow, redirect to Entra (NO resource param) ---
        app.MapGet("/oauth/authorize", (
            [FromQuery] string client_id,
            [FromQuery] string redirect_uri,
            [FromQuery] string code_challenge,
            [FromQuery] string? code_challenge_method,
            [FromQuery] string? state,
            [FromQuery] string? scope,
            ClientStore clients, PkceStateStore flows, EntraClient entra) =>
        {
            if (!clients.TryGet(client_id, out var reg) || !reg.RedirectUris.Contains(redirect_uri))
                return Results.BadRequest(new { error = "invalid_client" });
            if (!RedirectAllowlist.IsAllowed(redirect_uri))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });
            if (!string.Equals(code_challenge_method, "S256", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "invalid_request", error_description = "code_challenge_method must be S256" });

            var ourVerifier = PkceUtil.GenerateVerifier();
            var flowId = flows.Save(new PkceFlow(redirect_uri, state ?? "", code_challenge, ourVerifier, scope ?? ""));
            return Results.Redirect(entra.BuildAuthorizeUrl(flowId, PkceUtil.Challenge(ourVerifier)));
        }).AllowAnonymous();

        // --- Callback: Entra returns code -> exchange -> store -> redirect proxy code to claude ---
        app.MapGet("/oauth/callback", async (
            [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
            PkceStateStore flows, TokenStore tokens, EntraClient entra, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(error)) return Results.BadRequest(new { error });
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Results.BadRequest(new { error = "invalid_request" });

            var flow = flows.Take(state);
            if (flow is null) return Results.BadRequest(new { error = "invalid_state" });

            var entraTokens = await entra.ExchangeCodeAsync(code, flow.OurVerifier, ct);
            if (string.IsNullOrEmpty(entraTokens.RefreshToken) || string.IsNullOrEmpty(entraTokens.IdToken))
                return Results.BadRequest(new { error = "server_error", error_description = "Entra did not return refresh/id token" });

            var id = ClaimExtractor.FromIdToken(entraTokens.IdToken);
            var proxyCode = tokens.SaveAuthCode(new AuthCodeData(
                ClientCodeChallenge: flow.ClientCodeChallenge,
                Subject: id.Oid, ClientId: "", Scope: flow.Scope,
                Name: id.Name, Email: id.Email, EntraRefreshToken: entraTokens.RefreshToken));

            var sep = flow.ClientRedirectUri.Contains('?') ? '&' : '?';
            var target = $"{flow.ClientRedirectUri}{sep}code={Uri.EscapeDataString(proxyCode)}&state={Uri.EscapeDataString(flow.ClientState)}";
            return Results.Redirect(target);
        }).AllowAnonymous();
    }

    public record DcrRequest(string[]? redirect_uris, string? client_name, string? scope);
}
