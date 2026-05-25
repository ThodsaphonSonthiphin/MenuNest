using Mediator;

namespace MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

public sealed record CreateGroupCommand(string Name) : ICommand<CategoryGroupDto>;
