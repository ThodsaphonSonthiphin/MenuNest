using Mediator;

namespace MenuNest.Application.UseCases.Families.CreateFamily;

public sealed record CreateFamilyCommand(string Name) : ICommand<FamilyDto>;
