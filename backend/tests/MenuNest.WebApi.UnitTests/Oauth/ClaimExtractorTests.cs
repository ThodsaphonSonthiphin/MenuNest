using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class ClaimExtractorTests
{
    private static string MakeIdToken(params Claim[] claims)
        => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims));

    [Fact]
    public void FromIdToken_reads_oid_name_and_email()
    {
        var idToken = MakeIdToken(
            new Claim("oid", "obj-1"),
            new Claim("name", "Pon"),
            new Claim("preferred_username", "pon@x.io"));

        var id = ClaimExtractor.FromIdToken(idToken);

        id.Oid.Should().Be("obj-1");
        id.Name.Should().Be("Pon");
        id.Email.Should().Be("pon@x.io");
    }

    [Fact]
    public void FromIdToken_prefers_email_claim_then_falls_back_to_preferred_username()
    {
        var idToken = MakeIdToken(new Claim("oid", "o"), new Claim("email", "real@x.io"),
            new Claim("preferred_username", "upn@x.io"));
        ClaimExtractor.FromIdToken(idToken).Email.Should().Be("real@x.io");
    }
}
