namespace MenuNest.Application.UseCases.Me;

/// <summary>
/// Response payload for <c>GET /api/me</c>. Describes the signed-in
/// user and, if they belong to one, their family, plus the user's
/// <c>HomePath</c> (Home-page preference).
/// </summary>
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider,
    string? HomePath);
