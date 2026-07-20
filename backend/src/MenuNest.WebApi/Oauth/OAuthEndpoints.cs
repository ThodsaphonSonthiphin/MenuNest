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
        app.MapPost("/oauth/register", async (DcrRequest body, ClientStore clients) =>
        {
            var uris = body.redirect_uris ?? Array.Empty<string>();
            if (uris.Length == 0 || uris.Any(u => !RedirectAllowlist.IsAllowed(u)))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var id = await clients.RegisterAsync(body.client_name ?? "mcp-client", uris, body.scope);
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
        app.MapGet("/oauth/authorize", async (
            [FromQuery] string client_id,
            [FromQuery] string redirect_uri,
            [FromQuery] string code_challenge,
            [FromQuery] string? code_challenge_method,
            [FromQuery] string? state,
            [FromQuery] string? scope,
            ClientStore clients, PkceStateStore flows, EntraClient entra) =>
        {
            var reg = await clients.GetAsync(client_id);
            if (reg is null || !reg.RedirectUris.Contains(redirect_uri))
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
            var target = $"{flow.ClientRedirectUri}{sep}code={Uri.EscapeDataString(proxyCode)}";
            if (!string.IsNullOrEmpty(flow.ClientState))
                target += $"&state={Uri.EscapeDataString(flow.ClientState)}";
            return Results.Redirect(target);
        }).AllowAnonymous();

        // --- Token: mint our JWT (authorization_code) or refresh ---
        app.MapPost("/oauth/token", async (
            HttpRequest req, TokenStore tokens, EntraClient entra, OAuthJwt jwt, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();

            if (grant == "authorization_code")
            {
                var code = form["code"].ToString();
                var verifier = form["code_verifier"].ToString();
                var data = tokens.TakeAuthCode(code);
                if (data is null) return Results.BadRequest(new { error = "invalid_grant" });
                if (!PkceUtil.Verify(verifier, data.ClientCodeChallenge))
                    return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE failed" });

                return Results.Ok(IssueTokens(jwt, tokens, data.Subject, form["client_id"].ToString(), data.Scope, data.Name, data.Email, data.EntraRefreshToken));
            }

            if (grant == "refresh_token")
            {
                var refreshCode = form["refresh_token"].ToString();
                var entraRt = tokens.TakeRefresh(refreshCode);
                if (entraRt is null) return Results.BadRequest(new { error = "invalid_grant" });

                var refreshed = await entra.RefreshAsync(entraRt, ct);
                var newEntraRt = refreshed.RefreshToken ?? entraRt;
                var id = refreshed.IdToken is not null ? ClaimExtractor.FromIdToken(refreshed.IdToken) : null;
                if (id is null) return Results.BadRequest(new { error = "invalid_grant", error_description = "no id_token on refresh" });
                return Results.Ok(IssueTokens(jwt, tokens, id.Oid, form["client_id"].ToString(), "", id.Name, id.Email, newEntraRt));
            }

            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }).AllowAnonymous();
    }

    private static object IssueTokens(OAuthJwt jwt, TokenStore tokens, string subject, string clientId,
        string scope, string? name, string? email, string entraRefreshToken)
    {
        var extra = new List<System.Security.Claims.Claim>();
        if (name is not null) extra.Add(new("name", name));
        if (email is not null) { extra.Add(new("email", email)); extra.Add(new("preferred_username", email)); }

        var accessToken = jwt.Mint(subject, clientId, scope, extra);
        var refreshCode = tokens.SaveRefresh(entraRefreshToken);
        return new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = refreshCode,
            scope,
        };
    }

    public record DcrRequest(string[]? redirect_uris, string? client_name, string? scope);
}
