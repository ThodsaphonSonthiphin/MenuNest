using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class EntraClientTests
{
    private static EntraClient Build(params (string Key, string Value)[] settings)
    {
        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:ClientId"] = "e65fd81b-7a28-439b-a2ea-98734b5b5a36",
            ["AzureAd:ClientSecret"] = "secret",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111", // org tenant — must be ignored
            ["MCP:ServerUrl"] = "https://menunest.azurewebsites.net/mcp",
        };
        foreach (var (k, v) in settings) dict[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new EntraClient(new HttpClient(), config);
    }

    [Fact]
    public void Authorize_url_uses_SignInTenant_not_org_TenantId()
    {
        var sut = Build(("AzureAd:SignInTenant", "common"));

        var url = sut.BuildAuthorizeUrl("state123", "challenge123");

        url.Should().Contain("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
        url.Should().NotContain("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Authorize_url_defaults_to_common_when_SignInTenant_absent()
    {
        var sut = Build(); // no AzureAd:SignInTenant set

        var url = sut.BuildAuthorizeUrl("state123", "challenge123");

        url.Should().Contain("/common/oauth2/v2.0/authorize");
        url.Should().NotContain("11111111-1111-1111-1111-111111111111");
    }
}
