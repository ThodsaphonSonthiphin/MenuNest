using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MenuNest.Domain.Enums;
using MenuNest.WebApi.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

/// <summary>
/// HTTP-level tests for the SPA's durable-session endpoints (ADR-161), driven through
/// a test host running the real <c>Program.cs</c>. They exist to guard the two
/// behaviours whose loss is silent and expensive:
///
/// 1. <c>exchange</c> provisions the caller <i>while the provider token is still on the
///    request</i>. The app's own JWT carries <c>iss = MCP:ServerUrl</c>, which
///    <c>CurrentUserService.Provider</c> does not recognise, so a first-time Google user
///    provisioned any later would be recorded as Microsoft — permanently.
/// 2. <c>refresh</c> and <c>logout</c> are anonymous. <c>Program.cs</c> sets an
///    authorization <c>FallbackPolicy</c> requiring an authenticated user on every
///    endpoint, and by refresh time the access token is expired by definition, so
///    losing <c>AllowAnonymous</c> strands every returning user at the login screen.
/// 3. <c>exchange</c> takes a Microsoft/Google bearer and <i>only</i> that. An
///    app-minted access token authenticating here would upgrade a 1-hour credential
///    into a fresh 365-day one, repeatedly.
/// 4. A minted access token really is a credential for <c>/api/*</c>, resolving to the
///    same user the provider token did — the premise the whole feature rests on.
///
/// Everything is asserted over real HTTP: the routing table, the fallback policy, the
/// DI graph and the app's own JSON configuration all participate.
/// </summary>
public sealed class AppSessionEndpointHttpTests : IClassFixture<AppSessionApiFactory>
{
    private readonly AppSessionApiFactory _factory;

    public AppSessionEndpointHttpTests(AppSessionApiFactory factory) => _factory = factory;

    // ------------------------------------------------------------------
    // AllowAnonymous — the FallbackPolicy regression guard
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_is_reachable_without_an_authorization_header()
    {
        var client = _factory.CreateClient();

        var response = await PostJsonAsync(client, "/api/session/refresh", new { refresh_token = "no-such-code" });

        // The point is that authorization did not reject it. The endpoint ran and
        // refused the unknown grant on its own terms.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Logout_is_reachable_without_an_authorization_header()
    {
        var client = _factory.CreateClient();

        var response = await PostJsonAsync(client, "/api/session/logout", new { refresh_token = "no-such-code" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        // Logout is idempotent: an unknown code still reports success.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Exchange_rejects_an_unauthenticated_caller()
    {
        var client = _factory.CreateClient();

        var response = await PostJsonAsync(client, "/api/session/exchange", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // Exchange — provisioning happens here, under the provider token
    // ------------------------------------------------------------------

    [Fact]
    public async Task Exchange_provisions_a_first_time_Google_user_as_Google()
    {
        var subject = NewSubject();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            TestProviderTokenHandler.GoogleHeader(subject, "Google Person", "google.person@gmail.com"));

        var response = await PostJsonAsync(client, "/api/session/exchange", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The row must exist *now* — created by exchange itself, while the Google
        // bearer was on the request. If provisioning ever moves to a later request
        // (which carries the app's own JWT), this row is absent here and the user is
        // eventually created as Microsoft.
        var user = _factory.Query(db => db.Users.AsNoTracking().SingleOrDefault(u => u.ExternalId == subject));
        user.Should().NotBeNull();
        user!.AuthProvider.Should().Be(AuthProvider.Google, "the request's token was issued by Google");
        user.Email.Should().Be("google.person@gmail.com");
        user.DisplayName.Should().Be("Google Person");
    }

    [Fact]
    public async Task Exchange_provisions_a_first_time_Microsoft_user_as_Microsoft()
    {
        var subject = NewSubject();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            TestProviderTokenHandler.MicrosoftHeader(subject, "Entra Person", "entra.person@contoso.com"));

        var response = await PostJsonAsync(client, "/api/session/exchange", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = _factory.Query(db => db.Users.AsNoTracking().SingleOrDefault(u => u.ExternalId == subject));
        user.Should().NotBeNull();
        user!.AuthProvider.Should().Be(AuthProvider.Microsoft);
    }

    [Fact]
    public async Task Exchange_refuses_a_token_the_app_minted_itself()
    {
        // Privilege-escalation guard. The app's own access token lives 1 hour; the
        // refresh row it comes with lives 365 days. BearerSchemeSelector routes an
        // app-minted token to the McpProxy scheme, which is a registered scheme and so
        // satisfies Program.cs's FallbackPolicy on its own — meaning that without the
        // exchange endpoint's provider-only policy, whoever held a 1-hour access token
        // could mint a *fresh* year-long session from it, again and again, surviving
        // any revocation upstream.
        var subject = NewSubject();
        var issued = await ExchangeAsync(AuthenticatedClient(subject));

        var appTokenClient = _factory.CreateClient();
        appTokenClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {issued.AccessToken}");

        var response = await PostJsonAsync(appTokenClient, "/api/session/exchange", new { });

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "only a Microsoft or Google bearer may be traded for a durable session");

        // Nothing new was minted: the one session created above is still the only one.
        _factory.Query(db => db.AppSessions.Count(s => s.Subject == subject)).Should().Be(1);
    }

    [Fact]
    public async Task Exchange_still_accepts_a_provider_token()
    {
        // The other half of the guard above: restricting the schemes must not lock out
        // the callers the endpoint exists for.
        var microsoft = _factory.CreateClient();
        microsoft.DefaultRequestHeaders.Add(
            "Authorization",
            TestProviderTokenHandler.MicrosoftHeader(NewSubject(), "Entra Person", "entra@contoso.com"));
        (await PostJsonAsync(microsoft, "/api/session/exchange", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var google = _factory.CreateClient();
        google.DefaultRequestHeaders.Add(
            "Authorization",
            TestProviderTokenHandler.GoogleHeader(NewSubject(), "Google Person", "google@gmail.com"));
        (await PostJsonAsync(google, "/api/session/exchange", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Exchange_never_provisions_a_placeholder_user()
    {
        // UserProvisioner falls back to email "<externalId>@unknown", displayName ==
        // email and provider Microsoft when the request carries no recognisable
        // provider claims. That junk-user path was reachable only by exchanging under
        // an app-minted token; the provider-only policy closes it. No User row may be
        // created by the refused request above.
        var subject = NewSubject();
        var issued = await ExchangeAsync(AuthenticatedClient(subject));

        var appTokenClient = _factory.CreateClient();
        appTokenClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {issued.AccessToken}");
        await PostJsonAsync(appTokenClient, "/api/session/exchange", new { });

        _factory.Query(db => db.Users.AsNoTracking().Count(u => u.Email.EndsWith("@unknown")))
            .Should().Be(0, "no request can provision a user without real provider claims");
    }

    // ------------------------------------------------------------------
    // The round trip: a minted token really is a credential for /api/*
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Google")]
    public async Task A_minted_access_token_resolves_to_the_same_user_as_the_provider_token(string provider)
    {
        // The premise of the whole feature, asserted end to end over real HTTP:
        // sign in with a provider token, exchange it, then use ONLY the minted access
        // token. It must be routed by the production BearerSchemeSelector, validated by
        // the real McpProxy JWT bearer with the real OAuthJwt parameters, and resolve
        // through CurrentUserService to the very same user row.
        var subject = NewSubject();
        var providerClient = _factory.CreateClient();
        providerClient.DefaultRequestHeaders.Add(
            "Authorization",
            provider == "Google"
                ? TestProviderTokenHandler.GoogleHeader(subject, "Round Trip", "round.trip@gmail.com")
                : TestProviderTokenHandler.MicrosoftHeader(subject, "Round Trip", "round.trip@contoso.com"));

        var underProviderToken = await GetMeAsync(providerClient);

        var issued = await ExchangeAsync(providerClient);

        var appClient = _factory.CreateClient();
        appClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {issued.AccessToken}");

        var underAppToken = await GetMeAsync(appClient);

        underAppToken.UserId.Should().Be(
            underProviderToken.UserId,
            "the minted token stands in for the provider token, not for a different identity");
        underAppToken.AuthProvider.Should().Be(
            provider,
            "a Google sign-in must never come back as a Microsoft user");

        // And exactly one row exists for the subject — the app token did not provision
        // a second, provider-less user alongside it.
        _factory.Query(db => db.Users.AsNoTracking().Count(u => u.ExternalId == subject)).Should().Be(1);

        // The signature really is checked: flip one character of it and the same
        // request is refused. Without this, a stubbed-out handler would pass the
        // assertions above just as happily.
        var tampered = _factory.CreateClient();
        tampered.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {issued.AccessToken[..^1]}{(issued.AccessToken[^1] == 'A' ? 'B' : 'A')}");
        (await tampered.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exchange_returns_the_property_names_the_spa_reads()
    {
        var client = AuthenticatedClient(NewSubject());

        var response = await PostJsonAsync(client, "/api/session/exchange", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Asserted on the wire, through the application's own JSON configuration —
        // not a serializer the test constructed for itself.
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("expiresIn").GetInt32().Should().Be(3600);
        doc.RootElement.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();

        doc.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("accessToken", "expiresIn", "refreshToken");

        // The minted access token is a JWT the SPA can present as a bearer.
        doc.RootElement.GetProperty("accessToken").GetString()!
            .Split('.').Should().HaveCount(3);
    }

    [Fact]
    public async Task Refresh_binds_the_snake_case_field_the_spa_sends()
    {
        // The SPA posts {"refresh_token": …}; the model binder must accept exactly that.
        var client = AuthenticatedClient(NewSubject());
        var issued = await ExchangeAsync(client);

        var raw = new StringContent(
            $$"""{"refresh_token":"{{issued.RefreshToken}}"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _factory.CreateClient().PostAsync("/api/session/refresh", raw);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // Rotation and revocation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_rotates_the_refresh_token_and_retires_the_old_one()
    {
        var client = AuthenticatedClient(NewSubject());
        var issued = await ExchangeAsync(client);

        var anonymous = _factory.CreateClient();

        var rotated = await RefreshAsync(anonymous, issued.RefreshToken);
        rotated.Should().NotBeNull();
        rotated!.RefreshToken.Should().NotBe(issued.RefreshToken, "rotation must mint a new code");

        // The presented code is single-use.
        var replay = await PostJsonAsync(anonymous, "/api/session/refresh", new { refresh_token = issued.RefreshToken });
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The new code works.
        var again = await RefreshAsync(anonymous, rotated.RefreshToken);
        again.Should().NotBeNull();
        again!.RefreshToken.Should().NotBe(rotated.RefreshToken);
    }

    [Fact]
    public async Task Logout_revokes_only_the_presented_session()
    {
        var subject = NewSubject();
        var client = AuthenticatedClient(subject);

        var phone = await ExchangeAsync(client);
        var laptop = await ExchangeAsync(client);
        phone.RefreshToken.Should().NotBe(laptop.RefreshToken);

        _factory.Query(db => db.AppSessions.Count(s => s.Subject == subject)).Should().Be(2);

        var anonymous = _factory.CreateClient();

        var logout = await PostJsonAsync(anonymous, "/api/session/logout", new { refresh_token = phone.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The signed-out device is done…
        var revoked = await PostJsonAsync(anonymous, "/api/session/refresh", new { refresh_token = phone.RefreshToken });
        revoked.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // …the other one is untouched.
        var survivor = await RefreshAsync(anonymous, laptop.RefreshToken);
        survivor.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_reissues_for_the_same_subject_without_touching_the_provider()
    {
        var subject = NewSubject();
        var client = AuthenticatedClient(subject);
        var issued = await ExchangeAsync(client);

        var rotated = await RefreshAsync(_factory.CreateClient(), issued.RefreshToken);

        // Refresh never re-provisions: the user row created at exchange is still the
        // only one, and it still holds the provider the sign-in token implied.
        var users = _factory.Query(db => db.Users.AsNoTracking().Where(u => u.ExternalId == subject).ToList());
        users.Should().ContainSingle();
        users[0].AuthProvider.Should().Be(AuthProvider.Google);

        rotated!.AccessToken.Split('.').Should().HaveCount(3);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private sealed record TokenPair(string AccessToken, int ExpiresIn, string RefreshToken);

    /// <summary>The slice of <c>GET /api/me</c> these tests assert on.</summary>
    private sealed record MeResponse(Guid UserId, string Email, string AuthProvider);

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private HttpClient AuthenticatedClient(string subject)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            TestProviderTokenHandler.GoogleHeader(subject, $"User {subject[..12]}", $"{subject[..12]}@gmail.com"));
        return client;
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body);

    private static async Task<TokenPair> ExchangeAsync(HttpClient client)
    {
        var response = await PostJsonAsync(client, "/api/session/exchange", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenPair>())!;
    }

    private static async Task<MeResponse> GetMeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the presented bearer must authenticate /api/*");
        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    private static async Task<TokenPair?> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await PostJsonAsync(client, "/api/session/refresh", new { refresh_token = refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<TokenPair>();
    }
}
