using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class RedirectAllowlistTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://claude.com/api/mcp/auth_callback", true)]
    [InlineData("https://evil.example.com/cb", false)]
    [InlineData("http://claude.ai/api/mcp/auth_callback", false)]
    [InlineData(null, false)]
    public void IsAllowed_matches_only_known_claude_callbacks(string? uri, bool expected)
        => RedirectAllowlist.IsAllowed(uri).Should().Be(expected);
}
