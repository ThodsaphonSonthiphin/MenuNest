namespace MenuNest.Domain.Entities;

/// <summary>
/// A durable, MenuNest-minted sign-in for the web SPA (ADR-161). Deliberately
/// separate from <see cref="OAuthRefreshToken"/>: this session holds no upstream
/// identity-provider token, because refreshing it never calls one (ADR-162).
/// </summary>
public sealed class AppSession
{
    public string RefreshCode { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
