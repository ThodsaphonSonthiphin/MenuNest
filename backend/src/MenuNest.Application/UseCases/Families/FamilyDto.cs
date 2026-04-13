namespace MenuNest.Application.UseCases.Families;

/// <summary>
/// Response payload for family create/read endpoints.
/// </summary>
public sealed record FamilyDto(
    Guid Id,
    string Name,
    string InviteCode,
    int MemberCount);
