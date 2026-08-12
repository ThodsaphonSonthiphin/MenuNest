using MenuNest.Application.Abstractions;
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

    public static void MapAppSession(this WebApplication app)
    {
        // Exchange runs under the existing MultiAuth scheme, so the Microsoft/Google
        // bearer is still on the request. Provision HERE: CurrentUserService.Provider
        // reads `iss`, which on our own JWT is the server URL and would resolve to
        // null, silently recording a new Google user as Microsoft.
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
        });

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
