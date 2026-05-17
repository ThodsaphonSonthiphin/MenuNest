using Mediator;

namespace MenuNest.Application.UseCases.Health.Symptoms.ListSymptoms;

/// <summary>Returns all global seed symptoms PLUS the caller's custom symptoms.</summary>
public sealed record ListSymptomsQuery : IQuery<IReadOnlyList<SymptomDto>>;
