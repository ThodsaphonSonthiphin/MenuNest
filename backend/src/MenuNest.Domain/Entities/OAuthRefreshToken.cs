namespace MenuNest.Domain.Entities;

/// <summary>
/// Maps our opaque refresh code (held by claude.ai) to the stored Entra
/// refresh token. Single-use: rotated on every refresh (see ADR-037). The
/// Entra RT is protected at rest by Azure SQL TDE (no app-level encryption).
/// </summary>
public sealed class OAuthRefreshToken
{
    public string RefreshCode { get; set; } = null!;
    public string EntraRefreshToken { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
