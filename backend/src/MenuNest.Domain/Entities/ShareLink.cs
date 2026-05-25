using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A doctor-share link. The actual share URL contains an HMAC-signed JWT;
/// we persist only the SHA-256 hash of the token (<see cref="TokenHash"/>)
/// so a DB leak cannot reveal valid share URLs. Revocation is enforced by
/// checking <see cref="RevokedAt"/> on every public report request.
/// </summary>
public sealed class ShareLink : Entity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateOnly DateFrom { get; private set; }
    public DateOnly DateTo { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastAccessedAt { get; private set; }
    public int AccessCount { get; private set; }

    // EF Core
    private ShareLink() { }

    public static ShareLink Create(
        Guid userId,
        string tokenHash,
        DateOnly dateFrom,
        DateOnly dateTo,
        DateTime expiresAt,
        DateTime nowUtc)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("Token hash is required.");
        if (dateTo < dateFrom)
            throw new DomainException("DateTo must be on or after DateFrom.");
        if (expiresAt <= nowUtc)
            throw new DomainException("ExpiresAt must be in the future.");

        return new ShareLink
        {
            UserId = userId,
            TokenHash = tokenHash,
            DateFrom = dateFrom,
            DateTo = dateTo,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke()
    {
        if (RevokedAt is not null) return;
        RevokedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
        AccessCount++;
        // Intentionally NOT updating UpdatedAt — read-side bookkeeping only.
    }

    public bool IsValidAt(DateTime nowUtc) =>
        RevokedAt is null && nowUtc < ExpiresAt;
}
