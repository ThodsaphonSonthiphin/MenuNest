using Mediator;

namespace MenuNest.Application.UseCases.Families.TransferHead;

public sealed record TransferHeadCommand(Guid NewHeadUserId) : ICommand<Unit>;
