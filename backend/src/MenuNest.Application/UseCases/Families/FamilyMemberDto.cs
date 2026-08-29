namespace MenuNest.Application.UseCases.Families;

public sealed record FamilyMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTime JoinedAt,
    bool IsCreator,
    // menunest-201: separate from IsCreator on purpose. The creator flag is
    // history and never moves; the head flag is a role that does.
    bool IsHead,
    RelationshipLabelDto[] Relationships);

public sealed record RelationshipLabelDto(
    Guid RelationshipId,
    string RelationType,
    string Label);
