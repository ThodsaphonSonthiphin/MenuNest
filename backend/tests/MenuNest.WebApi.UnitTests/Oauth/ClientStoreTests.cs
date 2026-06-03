using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Caching.Memory;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class ClientStoreTests
{
    private static ClientStore New() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Register_then_TryGet_returns_redirect_uris()
    {
        var store = New();
        var clientId = store.Register("claude", new[] { "https://claude.ai/api/mcp/auth_callback" }, null);

        store.TryGet(clientId, out var reg).Should().BeTrue();
        reg.RedirectUris.Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
    }

    [Fact]
    public void TryGet_unknown_client_returns_false()
        => New().TryGet("nope", out _).Should().BeFalse();
}
