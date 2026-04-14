using Mediator;

namespace MenuNest.Application.UseCases.Families.ListRelationships;

public sealed record ListRelationshipsQuery : IQuery<IReadOnlyList<RelationshipDto>>;
