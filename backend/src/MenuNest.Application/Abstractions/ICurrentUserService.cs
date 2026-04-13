namespace MenuNest.Application.Abstractions;

/// <summary>
/// Exposes the currently authenticated caller's identity to the
/// Application layer. Handlers never touch <c>HttpContext</c> or MSAL
/// types directly — they depend on this interface, which the
/// Infrastructure layer implements on top of the Entra ID JWT claims.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// True when the HTTP request carries a valid bearer token.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The Entra ID object identifier (<c>oid</c> claim). Stable
    /// across tenants and personal accounts. <c>null</c> when
    /// <see cref="IsAuthenticated"/> is false.
    /// </summary>
    string? ExternalId { get; }

    /// <summary>
    /// Preferred email address from the token, if present. Falls back
    /// to <c>preferred_username</c>.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Human-readable display name from the token (<c>name</c> claim).
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> if the caller
    /// is anonymous. Returns the non-null <see cref="ExternalId"/>.
    /// </summary>
    string RequireExternalId();
}
