namespace MenuNest.Domain.Entities;

/// <summary>
/// A DCR-registered MCP client (RFC 7591). Keyed by the opaque client_id we
/// hand back to claude.ai. Durable so a client survives an App Service restart
/// (see ADR-037). Not a domain <c>Entity</c> — its identity is the opaque id.
/// </summary>
public sealed class OAuthClient
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    /// <summary>JSON-serialised string[] of allowed redirect URIs.</summary>
    public string RedirectUrisJson { get; set; } = null!;
    public string? Scope { get; set; }
    public DateTime ExpiresAt { get; set; }
}
