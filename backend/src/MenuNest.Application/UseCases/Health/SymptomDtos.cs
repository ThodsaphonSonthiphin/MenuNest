namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// Symptom for the AutoComplete picker. <c>IsSeed = true</c> rows are
/// global; otherwise they are user-custom.
/// </summary>
public sealed record SymptomDto(Guid Id, string Name, bool IsSeed);

/// <summary>
/// Trigger for the AutoComplete picker — same hybrid pattern as
/// <see cref="SymptomDto"/>.
/// </summary>
public sealed record TriggerDto(Guid Id, string Name, bool IsSeed);
