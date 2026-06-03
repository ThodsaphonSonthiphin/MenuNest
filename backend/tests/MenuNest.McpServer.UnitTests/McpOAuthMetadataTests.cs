using FluentAssertions;
using MenuNest.McpServer;

namespace MenuNest.McpServer.UnitTests;

public class McpOAuthMetadataTests
{
    private const string Instance = "https://login.microsoftonline.com/";
    private const string Tenant   = "d500e2f4-1325-41d2-9f92-2f2f39e8ea19";
    private const string ClientId = "e65fd81b-7a28-439b-a2ea-98734b5b5a36";
    private const string Resource = "https://menunest.azurewebsites.net/mcp";

    [Fact]
    public void Build_points_authorization_servers_at_tenant_specific_issuer()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.authorization_servers.Should().ContainSingle()
            .Which.Should().Be("https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0");
    }

    [Fact]
    public void Build_advertises_the_access_as_user_scope_for_the_api()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.scopes_supported.Should().ContainSingle()
            .Which.Should().Be("api://e65fd81b-7a28-439b-a2ea-98734b5b5a36/access_as_user");
    }

    [Fact]
    public void Build_passes_through_resource_and_sets_header_bearer_method()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.resource.Should().Be(Resource);
        meta.bearer_methods_supported.Should().Equal("header");
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/")]   // trailing slash
    [InlineData("https://login.microsoftonline.com")]    // no trailing slash
    public void Build_normalizes_instance_trailing_slash(string instance)
    {
        var meta = McpOAuthMetadata.Build(instance, Tenant, ClientId, Resource);

        meta.authorization_servers[0].Should().Be(
            "https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0");
    }
}
