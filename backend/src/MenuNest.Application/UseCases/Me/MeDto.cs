namespace MenuNest.Application.UseCases.Me;

/// <summary>
/// Response payload for <c>GET /api/me</c>. Describes the signed-in
/// user and, if they belong to one, their family.
/// </summary>
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider);
