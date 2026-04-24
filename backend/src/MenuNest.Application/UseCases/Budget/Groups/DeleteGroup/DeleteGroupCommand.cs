using Mediator;

namespace MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;

public sealed record DeleteGroupCommand(Guid Id) : ICommand<Unit>;
