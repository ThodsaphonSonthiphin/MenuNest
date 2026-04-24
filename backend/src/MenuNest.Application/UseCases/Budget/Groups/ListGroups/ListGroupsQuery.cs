using Mediator;

namespace MenuNest.Application.UseCases.Budget.Groups.ListGroups;

public sealed record ListGroupsQuery : IQuery<IReadOnlyList<CategoryGroupDto>>;
