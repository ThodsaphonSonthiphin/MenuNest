using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed record AddRelationshipCommand(
    Guid FromUserId,
    Guid ToUserId,
    RelationType RelationType) : ICommand<RelationshipDto>;
