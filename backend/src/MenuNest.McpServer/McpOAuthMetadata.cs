namespace MenuNest.McpServer;

/// <summary>
/// RFC 9728 OAuth 2.0 Protected Resource Metadata for the MCP endpoint.
/// Property names are snake_case to match the wire format exactly (System.Text.Json
/// serializes them verbatim). Served anonymously at /.well-known/oauth-protected-resource
/// so MCP clients (claude.ai) can discover the authorization server + required scope.
/// </summary>
public sealed record ProtectedResourceMetadata(
    string resource,
    string[] authorization_servers,
    string[] scopes_supported,
    string[] bearer_methods_supported);

public static class McpOAuthMetadata
{
    /// <summary>
    /// Builds the protected-resource document.
    /// <paramref name="instance"/> is the Entra instance (e.g. "https://login.microsoftonline.com/").
    /// <paramref name="tenantId"/> must be the concrete tenant GUID in prod — pointing the
    /// authorization server at the tenant-specific issuer is the only fully issuer-consistent
    /// discovery path for claude.ai (see ADR-002). <paramref name="resourceUrl"/> is the MCP
    /// endpoint URL, derived from the incoming request so it is environment-agnostic.
    /// </summary>
    public static ProtectedResourceMetadata Build(
        string instance, string tenantId, string clientId, string resourceUrl)
        => new(
            resource: resourceUrl,
            authorization_servers: [$"{instance.TrimEnd('/')}/{tenantId}/v2.0"],
            scopes_supported: [$"api://{clientId}/access_as_user"],
            bearer_methods_supported: ["header"]);
}
