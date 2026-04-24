using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;

public sealed record DeleteAccountCommand(Guid Id) : ICommand<Unit>;
