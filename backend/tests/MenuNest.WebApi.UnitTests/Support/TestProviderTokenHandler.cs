using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MenuNest.WebApi.UnitTests.Support;

/// <summary>
/// Stands in for the Microsoft / Google JWT bearer handlers, which cannot run in a
/// test host: validating a real provider token needs live OIDC metadata and a real
/// signature. Everything downstream of the handler is the production code —
/// <c>MultiAuth</c> still forwards here, the authorization <c>FallbackPolicy</c> still
/// evaluates the result, and <c>CurrentUserService</c> reads these claims off
/// <c>HttpContext.User</c> exactly as it reads a real token's.
///
/// The claim shape is deliberately the same one the real handlers produce: both JWT
/// bearers set <c>MapInboundClaims = false</c>, so payload members keep their raw
/// names (<c>sub</c>, <c>oid</c>, <c>iss</c>, <c>name</c>, <c>email</c>) — and
/// <c>iss</c>, which <c>CurrentUserService.Provider</c> switches on, is a payload
/// member of every real Microsoft and Google token.
/// </summary>
public sealed class TestProviderTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestProviderToken";
    private const string Prefix = "Bearer ";

    public const string GoogleIssuer = "https://accounts.google.com";
    public const string MicrosoftIssuer = "https://login.microsoftonline.com/common/v2.0";

    /// <summary>Builds the <c>Authorization</c> header value carrying the given claims.</summary>
    public static string Header(params (string Type, string Value)[] claims)
    {
        var payload = claims.ToDictionary(c => c.Type, c => c.Value);
        var json = JsonSerializer.Serialize(payload);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>A Google sign-in token: <c>sub</c> only, no <c>oid</c> — as Google issues.</summary>
    public static string GoogleHeader(string subject, string name, string email) =>
        Header(("iss", GoogleIssuer), ("sub", subject), ("name", name), ("email", email));

    /// <summary>A Microsoft Entra v2 token: carries <c>oid</c> alongside <c>sub</c>.</summary>
    public static string MicrosoftHeader(string objectId, string name, string email) =>
        Header(("iss", MicrosoftIssuer), ("oid", objectId), ("sub", objectId),
               ("name", name), ("preferred_username", email));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            // No credential presented — the same NoResult a JWT bearer returns, which
            // is what lets the FallbackPolicy produce a 401 on protected endpoints.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Dictionary<string, string>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, string>>(
                Encoding.UTF8.GetString(Convert.FromBase64String(header[Prefix.Length..].Trim())));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail(ex));
        }

        if (payload is null || payload.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty test token payload."));
        }

        var identity = new ClaimsIdentity(
            payload.Select(kv => new Claim(kv.Key, kv.Value)),
            authenticationType: SchemeName,
            nameType: "name",
            roleType: "roles");

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
