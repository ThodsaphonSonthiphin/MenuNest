using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.Episodes.StartEpisode;

/// <summary>
/// Starts a new symptom episode. The "Quick Log Attack" command — most
/// migraine-specific attributes are optional so the same handler can
/// log non-migraine episodes too (e.g., stomach ache, fever).
/// </summary>
public sealed record StartEpisodeCommand(
    Guid SymptomId,
    int Severity,
    bool IsOnPeriod = false,
    DateTime? StartedAt = null,
    IReadOnlyList<Guid>? TriggerIds = null,
    string? Notes = null,
    // Migraine-specific attributes — all optional
    bool? HasAura = null,
    IReadOnlyList<AuraType>? AuraTypes = null,
    int? AuraDurationMin = null,
    SymptomLocation? Location = null,
    SymptomQuality? Quality = null,
    IReadOnlyList<AssociatedSymptom>? AssociatedSymptoms = null,
    bool? WorsenedByActivity = null,
    FunctionalImpact? FunctionalImpact = null) : ICommand<EpisodeDto>;
