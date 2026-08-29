using Mediator;

namespace MenuNest.Application.UseCases.Budget.History.UndoChange;

public sealed record UndoChangeCommand(Guid ChangeId) : ICommand<Unit>;
