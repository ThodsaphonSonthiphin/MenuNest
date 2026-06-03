using System.Text.Json;
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class OAuthDiscoveryTests
{
    private const string Base = "https://menunest.azurewebsites.net";
    private const string Cid = "e65fd81b-7a28-439b-a2ea-98734b5b5a36";

    private static JsonElement Json(object o)
        => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void ProtectedResource_points_authorization_servers_at_this_server()
    {
        var j = Json(OAuthDiscovery.ProtectedResource(Base, Cid));
        j.GetProperty("resource").GetString().Should().Be($"{Base}/mcp");
        j.GetProperty("authorization_servers")[0].GetString().Should().Be(Base);
        j.GetProperty("scopes_supported")[0].GetString().Should().Be($"api://{Cid}/access_as_user");
    }

    [Fact]
    public void AuthorizationServer_advertises_our_oauth_endpoints_and_dcr()
    {
        var j = Json(OAuthDiscovery.AuthorizationServer(Base));
        j.GetProperty("issuer").GetString().Should().Be(Base);
        j.GetProperty("authorization_endpoint").GetString().Should().Be($"{Base}/oauth/authorize");
        j.GetProperty("token_endpoint").GetString().Should().Be($"{Base}/oauth/token");
        j.GetProperty("registration_endpoint").GetString().Should().Be($"{Base}/oauth/register");
        j.GetProperty("code_challenge_methods_supported")[0].GetString().Should().Be("S256");
    }
}
