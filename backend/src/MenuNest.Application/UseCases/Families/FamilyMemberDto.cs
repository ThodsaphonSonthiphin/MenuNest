namespace MenuNest.Application.UseCases.Families;

public sealed record FamilyMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTime JoinedAt,
    bool IsCreator,
    RelationshipLabelDto[] Relationships);

public sealed record RelationshipLabelDto(
    Guid RelationshipId,
    string RelationType,
    string Label);
