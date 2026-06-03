using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class TokenUtilTests
{
    [Fact]
    public void Opaque_is_urlsafe_and_unique()
    {
        var a = TokenUtil.Opaque();
        var b = TokenUtil.Opaque();
        a.Should().NotBe(b);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }
}
