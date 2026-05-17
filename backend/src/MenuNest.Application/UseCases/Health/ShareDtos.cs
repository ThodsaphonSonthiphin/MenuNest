namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// The result of <see cref="Share.CreateShareLink.CreateShareLinkCommand"/>.
/// The <see cref="Token"/> is returned only once — store only its hash on
/// the server. Frontend renders <see cref="ShareUrl"/> as a QR code and
/// copyable link.
/// </summary>
public sealed record CreateShareLinkResultDto(
    string Token,
    string ShareUrl,
    Guid ShareId,
    DateTime ExpiresAt,
    DateOnly DateFrom,
    DateOnly DateTo);

/// <summary>
/// Row on the "My share links" page. Raw token is NEVER returned —
/// only metadata so the user can revoke if they suspect a leak.
/// </summary>
public sealed record ShareLinkSummaryDto(
    Guid Id,
    DateOnly DateFrom,
    DateOnly DateTo,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    int AccessCount,
    DateTime? LastAccessedAt);
