using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class PkceUtilTests
{
    [Fact]
    public void Challenge_then_Verify_roundtrips()
    {
        var verifier = PkceUtil.GenerateVerifier();
        var challenge = PkceUtil.Challenge(verifier);

        PkceUtil.Verify(verifier, challenge).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_verifier()
    {
        var challenge = PkceUtil.Challenge(PkceUtil.GenerateVerifier());

        PkceUtil.Verify(PkceUtil.GenerateVerifier(), challenge).Should().BeFalse();
    }

    [Fact]
    public void Challenge_is_base64url_without_padding_and_matches_known_vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        PkceUtil.Challenge(verifier).Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }
}
