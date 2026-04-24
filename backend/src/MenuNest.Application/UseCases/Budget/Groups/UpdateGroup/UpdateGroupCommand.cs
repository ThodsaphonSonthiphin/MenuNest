using Mediator;

namespace MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;

public sealed record UpdateGroupCommand(Guid Id, string Name, int SortOrder) : ICommand<CategoryGroupDto>;
