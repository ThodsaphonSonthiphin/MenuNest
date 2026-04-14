namespace MenuNest.Application.UseCases.Families;

public sealed record RelationshipDto(
    Guid Id,
    Guid FromUserId,
    string FromUserName,
    Guid ToUserId,
    string ToUserName,
    string RelationType);
