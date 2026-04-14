using Mediator;

namespace MenuNest.Application.UseCases.Families.RotateInviteCode;

public sealed record RotateInviteCodeCommand : ICommand<RotateInviteCodeResult>;

public sealed record RotateInviteCodeResult(string InviteCode);
