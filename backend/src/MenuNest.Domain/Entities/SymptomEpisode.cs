using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One occurrence of a symptom (one migraine attack, one fever episode, etc.).
/// Migraine-specific attributes (<see cref="HasAura"/>, <see cref="Location"/>,
/// <see cref="Quality"/>, <see cref="AssociatedSymptoms"/>, etc.) are all
/// nullable so the same entity serves non-migraine episodes too.
///
/// State transitions:
///   Start → (maybe MarkNoDrug) → UpdateSeverity/SetMigraineAttributes/SetTriggers ...
///        → Resolve (sets EndedAt) OR RetroClose (sets EndedAt + retro flag)
/// </summary>
public sealed class SymptomEpisode : Entity
{
    public Guid UserId { get; private set; }
    public Guid SymptomId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public int Severity { get; private set; }
    public int? SeverityAfter { get; private set; }
    public bool IsOnPeriod { get; private set; }
    public bool NoDrugTaken { get; private set; }
    public NoDrugReason? NoDrugReasonCode { get; private set; }
    public string? Notes { get; private set; }
    public bool RetroClosed { get; private set; }
    public string? RetroEstimatedDuration { get; private set; }

    // Migraine-specific attributes (nullable — only set for migraine-type episodes)
    public bool? HasAura { get; private set; }
    public int? AuraDurationMin { get; private set; }
    public SymptomLocation? Location { get; private set; }
    public SymptomQuality? Quality { get; private set; }
    public bool? WorsenedByActivity { get; private set; }
    public FunctionalImpact? FunctionalImpact { get; private set; }

    // JSON-backed collections
    private List<Guid> _triggerIds = new();
    public IReadOnlyList<Guid> TriggerIds => _triggerIds.AsReadOnly();

    private List<AuraType> _auraTypes = new();
    public IReadOnlyList<AuraType> AuraTypes => _auraTypes.AsReadOnly();

    private List<AssociatedSymptom> _associatedSymptoms = new();
    public IReadOnlyList<AssociatedSymptom> AssociatedSymptoms => _associatedSymptoms.AsReadOnly();

    // EF Core
    private SymptomEpisode() { }

    public static SymptomEpisode Start(
        Guid userId,
        Guid symptomId,
        int severity,
        bool isOnPeriod = false,
        DateTime? startedAt = null,
        IEnumerable<Guid>? triggerIds = null,
        string? notes = null)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (symptomId == Guid.Empty)
            throw new DomainException("SymptomId is required.");
        if (severity < 1 || severity > 10)
            throw new DomainException("Severity must be between 1 and 10.");

        return new SymptomEpisode
        {
            UserId = userId,
            SymptomId = symptomId,
            StartedAt = startedAt ?? DateTime.UtcNow,
            Severity = severity,
            IsOnPeriod = isOnPeriod,
            Notes = notes?.Trim(),
            _triggerIds = triggerIds?.Distinct().ToList() ?? new List<Guid>()
        };
    }

    public void SetMigraineAttributes(
        bool? hasAura,
        IEnumerable<AuraType>? auraTypes,
        int? auraDurationMin,
        SymptomLocation? location,
        SymptomQuality? quality,
        IEnumerable<AssociatedSymptom>? associatedSymptoms,
        bool? worsenedByActivity,
        FunctionalImpact? functionalImpact)
    {
        if (auraDurationMin.HasValue && auraDurationMin.Value < 0)
            throw new DomainException("Aura duration cannot be negative.");

        HasAura = hasAura;
        _auraTypes = auraTypes?.Distinct().ToList() ?? new List<AuraType>();
        AuraDurationMin = auraDurationMin;
        Location = location;
        Quality = quality;
        _associatedSymptoms = associatedSymptoms?.Distinct().ToList() ?? new List<AssociatedSymptom>();
        WorsenedByActivity = worsenedByActivity;
        FunctionalImpact = functionalImpact;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTriggers(IEnumerable<Guid> triggerIds)
    {
        _triggerIds = triggerIds?.Distinct().ToList() ?? new List<Guid>();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSeverity(int newSeverity)
    {
        if (newSeverity < 1 || newSeverity > 10)
            throw new DomainException("Severity must be between 1 and 10.");

        Severity = newSeverity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetIsOnPeriod(bool isOnPeriod)
    {
        IsOnPeriod = isOnPeriod;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Resolve(DateTime? endedAt = null, int severityAfter = 0)
    {
        if (EndedAt is not null)
            throw new DomainException("Episode is already resolved.");
        if (severityAfter < 0 || severityAfter > 10)
            throw new DomainException("Severity-after must be between 0 and 10.");

        EndedAt = endedAt ?? DateTime.UtcNow;
        SeverityAfter = severityAfter;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RetroClose(string? estimatedDuration, DateTime? endedAt = null)
    {
        if (EndedAt is not null)
            throw new DomainException("Episode is already resolved.");

        RetroClosed = true;
        RetroEstimatedDuration = estimatedDuration?.Trim();
        EndedAt = endedAt ?? DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkNoDrug(NoDrugReason reason)
    {
        NoDrugTaken = true;
        NoDrugReasonCode = reason;
        UpdatedAt = DateTime.UtcNow;
    }
}
