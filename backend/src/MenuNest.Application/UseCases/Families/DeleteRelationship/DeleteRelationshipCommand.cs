using Mediator;

namespace MenuNest.Application.UseCases.Families.DeleteRelationship;

public sealed record DeleteRelationshipCommand(Guid Id) : ICommand;
