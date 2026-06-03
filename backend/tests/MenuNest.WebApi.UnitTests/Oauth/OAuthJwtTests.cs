using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class OAuthJwtTests
{
    private const string ServerUrl = "https://menunest.azurewebsites.net/mcp";

    private static OAuthJwt Build() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
            ["MCP:ServerUrl"] = ServerUrl,
        }).Build());

    [Fact]
    public void Minted_token_validates_with_ValidationParameters_and_carries_claims()
    {
        var sut = Build();
        var token = sut.Mint(
            subject: "oid-123",
            clientId: "client-abc",
            scope: "api://x/access_as_user",
            extra: new[] { new Claim("name", "Pon"), new Claim("email", "pon@x.io") });

        // Clear the default inbound claim-type map so short claim names (oid, email, sub)
        // are not remapped to WS-Federation URIs by JwtSecurityTokenHandler 8.x.
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(token, sut.ValidationParameters(), out _);

        principal.FindFirst("oid")!.Value.Should().Be("oid-123");
        principal.FindFirst("name")!.Value.Should().Be("Pon");
        principal.FindFirst("email")!.Value.Should().Be("pon@x.io");
        principal.FindFirst("aud")!.Value.Should().Be(ServerUrl);
        principal.FindFirst("iss")!.Value.Should().Be(ServerUrl);
    }

    [Fact]
    public void Token_signed_with_different_key_fails_validation()
    {
        var token = Build().Mint("oid-1", "c", "s", Array.Empty<Claim>());
        var other = new OAuthJwt(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:SigningKey"] = "different", ["MCP:ServerUrl"] = ServerUrl }).Build());

        var handler2 = new JwtSecurityTokenHandler();
        handler2.InboundClaimTypeMap.Clear();
        var act = () => handler2.ValidateToken(token, other.ValidationParameters(), out _);
        act.Should().Throw<SecurityTokenInvalidSignatureException>();
    }
}
