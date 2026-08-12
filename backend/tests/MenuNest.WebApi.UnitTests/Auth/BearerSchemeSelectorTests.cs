using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Auth;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Auth;

public sealed class BearerSchemeSelectorTests
{
    // The app JWT's issuer is MCP:ServerUrl VERBATIM, /mcp suffix included (OAuthJwt.cs:20).
    private const string AppIssuer = "https://menunest.azurewebsites.net/mcp";

    private static string AppToken()
    {
        var jwt = new OAuthJwt(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
                ["MCP:ServerUrl"] = AppIssuer,
            }).Build());
        return jwt.Mint("oid-1", AppSessionService.ClientId, string.Empty, Array.Empty<Claim>());
    }

    private static string TokenFrom(string issuer) =>
        new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer: issuer, audience: "aud", claims: new[] { new Claim("sub", "x") }));

    [Fact]
    public void An_app_minted_token_goes_to_the_app_scheme()
    {
        BearerSchemeSelector.Select($"Bearer {AppToken()}", AppIssuer)
            .Should().Be(BearerSchemeSelector.AppIssued);
    }

    [Fact]
    public void The_app_issuer_must_be_MCP_ServerUrl_verbatim_including_the_mcp_suffix()
    {
        // Global Constraint: OAuthJwt stamps `iss` = MCP:ServerUrl verbatim, /mcp
        // suffix and all, and this comparison is an exact string match. A "cleanup"
        // that passes a stripped base URL here would stop routing every app-minted
        // token to the app scheme — i.e. break all authentication — and no other gate
        // would notice.
        const string strippedBaseUrl = "https://menunest.azurewebsites.net";

        BearerSchemeSelector.Select($"Bearer {AppToken()}", strippedBaseUrl)
            .Should().NotBe(
                BearerSchemeSelector.AppIssued,
                "the issuer is MCP:ServerUrl verbatim; a base URL without /mcp is a different string")
            .And.Be(BearerSchemeSelector.Microsoft, "an unrecognised issuer falls back to Microsoft");
    }

    [Fact]
    public void A_google_token_still_goes_to_the_google_scheme()
    {
        BearerSchemeSelector.Select($"Bearer {TokenFrom("https://accounts.google.com")}", AppIssuer)
            .Should().Be(BearerSchemeSelector.Google);
    }

    [Fact]
    public void An_entra_token_still_goes_to_the_microsoft_scheme()
    {
        BearerSchemeSelector.Select(
                $"Bearer {TokenFrom("https://login.microsoftonline.com/common/v2.0")}", AppIssuer)
            .Should().Be(BearerSchemeSelector.Microsoft);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public void Anything_unreadable_falls_back_to_microsoft(string? header)
    {
        BearerSchemeSelector.Select(header, AppIssuer)
            .Should().Be(BearerSchemeSelector.Microsoft);
    }
}
