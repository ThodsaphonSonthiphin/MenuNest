namespace MenuNest.Application.Abstractions;

/// <summary>
/// Signs and verifies HMAC-SHA256 share tokens for doctor-report links.
/// Tokens are short JWT-style strings; we persist only their SHA-256 hash
/// in the <c>ShareLinks</c> table so a DB leak cannot recover live tokens.
/// </summary>
public interface IShareTokenService
{
    /// <summary>
    /// Mints a new share token. The returned raw token is shown ONCE to the
    /// user (encoded in the QR URL); the <c>Hash</c> is what gets persisted.
    /// </summary>
    ShareTokenIssuance Issue(
        Guid userId,
        DateOnly dateFrom,
        DateOnly dateTo,
        DateTime expiresAtUtc);

    /// <summary>
    /// Verifies a token's signature, expiry, and structure. Returns the
    /// extracted claims, or throws if invalid. Does NOT check the database
    /// (revocation check happens at the data layer using the hash).
    /// </summary>
    ShareTokenClaims Verify(string rawToken);

    /// <summary>
    /// Returns the SHA-256 hash hex string of a raw token. Used to look up
    /// the matching <c>ShareLink</c> row.
    /// </summary>
    string Hash(string rawToken);
}

/// <param name="RawToken">The single-use token to include in the QR URL.</param>
/// <param name="Hash">SHA-256 hex hash to persist.</param>
public sealed record ShareTokenIssuance(string RawToken, string Hash);

public sealed record ShareTokenClaims(
    Guid UserId,
    DateOnly DateFrom,
    DateOnly DateTo,
    DateTime ExpiresAtUtc);
