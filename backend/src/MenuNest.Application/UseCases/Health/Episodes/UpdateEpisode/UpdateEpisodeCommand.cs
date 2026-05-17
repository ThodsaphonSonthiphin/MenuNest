using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.Episodes.UpdateEpisode;

/// <summary>
/// Partial update of an episode — every field is nullable; only the
/// fields actually supplied are written. Migraine attributes are
/// grouped: if any of them is supplied, all eight are written (the
/// domain method replaces the whole bag).
/// </summary>
public sealed record UpdateEpisodeCommand(
    Guid Id,
    int? Severity = null,
    string? Notes = null,
    bool? IsOnPeriod = null,
    IReadOnlyList<Guid>? TriggerIds = null,
    // Migraine attributes — supplying ANY rewrites ALL eight
    bool? HasAura = null,
    IReadOnlyList<AuraType>? AuraTypes = null,
    int? AuraDurationMin = null,
    SymptomLocation? Location = null,
    SymptomQuality? Quality = null,
    IReadOnlyList<AssociatedSymptom>? AssociatedSymptoms = null,
    bool? WorsenedByActivity = null,
    FunctionalImpact? FunctionalImpact = null,
    bool MigraineAttributesProvided = false) : ICommand<EpisodeDetailDto>;
