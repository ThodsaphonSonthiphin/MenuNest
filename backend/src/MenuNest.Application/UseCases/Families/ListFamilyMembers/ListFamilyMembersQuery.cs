using Mediator;

namespace MenuNest.Application.UseCases.Families.ListFamilyMembers;

public sealed record ListFamilyMembersQuery : IQuery<IReadOnlyList<FamilyMemberDto>>;
