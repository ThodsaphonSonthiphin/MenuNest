using MenuNest.Application.Abstractions;
using MenuNest.WebApi.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// The SPA's durable-session endpoints (ADR-161). Deliberately separate from
/// /oauth/* : that is the MCP proxy's OAuth 2.1 contract, whose refresh grant is
/// anchored on a server-held Entra refresh token this session does not have.
/// </summary>
public static class AppSessionEndpoints
{
    public sealed record RefreshRequest(string refresh_token);

    /// <summary>
    /// Authenticated by a Microsoft or Google bearer only — deliberately excludes
    /// <see cref="BearerSchemeSelector.AppIssued"/>, the scheme that validates this
    /// app's own JWTs.
    /// </summary>
    private static readonly AuthorizationPolicy ProviderTokenOnly =
        new AuthorizationPolicyBuilder(BearerSchemeSelector.Microsoft, BearerSchemeSelector.Google)
            .RequireAuthenticatedUser()
            .Build();

    public static void MapAppSession(this WebApplication app)
    {
        // Exchange accepts ONLY a provider bearer, never a token this app minted.
        //
        // The scheme list is the enforcement, not documentation. BearerSchemeSelector
        // routes anything whose `iss` equals MCP:ServerUrl to the McpProxy scheme,
        // which is a registered scheme and therefore satisfies Program.cs's
        // FallbackPolicy on its own — so under the default MultiAuth scheme a 1-hour
        // app access token would authenticate here and mint a *fresh* 365-day session,
        // repeatedly, outliving any upstream revocation. Naming the two provider
        // schemes explicitly makes the authorization middleware authenticate against
        // them and them alone, so an app-minted token is rejected with a 401.
        //
        // It also keeps provisioning honest: CurrentUserService.Provider reads `iss`,
        // which on our own JWT is the server URL and resolves to null — a new Google
        // user provisioned on such a request would be recorded as Microsoft with an
        // "<externalId>@unknown" email, permanently. With this policy that path is
        // unreachable, because provisioning only ever runs under a real provider token.
        app.MapPost("/api/session/exchange", async (
            IUserProvisioner provisioner,
            ICurrentUserService currentUser,
            AppSessionService sessions,
            CancellationToken ct) =>
        {
            var user = await provisioner.GetOrProvisionCurrentAsync(ct);
            var tokens = await sessions.IssueAsync(
                currentUser.RequireExternalId(), user.DisplayName, user.Email, ct);
            return Results.Ok(tokens);
        }).RequireAuthorization(ProviderTokenOnly);

        // AllowAnonymous is required, not optional: Program.cs sets a FallbackPolicy
        // demanding an authenticated user, and by refresh time the access token is
        // expired by definition.
        app.MapPost("/api/session/refresh", async (
            [FromBody] RefreshRequest body,
            AppSessionService sessions,
            CancellationToken ct) =>
        {
            var tokens = await sessions.RefreshAsync(body.refresh_token, ct);
            return tokens is null
                ? Results.BadRequest(new { error = "invalid_grant" })
                : Results.Ok(tokens);
        }).AllowAnonymous();

        // Revokes only the presented session (ADR-159). Idempotent: an unknown or
        // already-revoked code still reports success, so sign-out never fails.
        app.MapPost("/api/session/logout", async (
            [FromBody] RefreshRequest body,
            AppSessionStore sessions,
            CancellationToken ct) =>
        {
            await sessions.RevokeAsync(body.refresh_token, ct);
            return Results.NoContent();
        }).AllowAnonymous();
    }
}
