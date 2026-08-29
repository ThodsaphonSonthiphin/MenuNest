using Mediator;

namespace MenuNest.Application.UseCases.Budget.History.RedoChange;

public sealed record RedoChangeCommand(Guid ChangeId) : ICommand<Unit>;
