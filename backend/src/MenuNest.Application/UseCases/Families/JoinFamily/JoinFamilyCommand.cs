using Mediator;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed record JoinFamilyCommand(string InviteCode) : ICommand<FamilyDto>;
