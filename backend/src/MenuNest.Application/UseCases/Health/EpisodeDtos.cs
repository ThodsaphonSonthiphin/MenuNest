using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// Episode summary for the History list and the Home "active" banner.
/// </summary>
public sealed record EpisodeDto(
    Guid Id,
    Guid SymptomId,
    string SymptomName,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Severity,
    int? SeverityAfter,
    bool IsOnPeriod,
    bool NoDrugTaken,
    NoDrugReason? NoDrugReasonCode,
    bool RetroClosed,
    int IntakeCount,
    string? FirstDrugName);

/// <summary>
/// Full episode detail with all attributes, the intake timeline, and
/// the follow-up pings. Powers the Episode Detail screen.
/// </summary>
public sealed record EpisodeDetailDto(
    Guid Id,
    Guid SymptomId,
    string SymptomName,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Severity,
    int? SeverityAfter,
    bool IsOnPeriod,
    bool NoDrugTaken,
    NoDrugReason? NoDrugReasonCode,
    string? Notes,
    bool RetroClosed,
    string? RetroEstimatedDuration,
    // Migraine attributes
    bool? HasAura,
    int? AuraDurationMin,
    IReadOnlyList<AuraType> AuraTypes,
    SymptomLocation? Location,
    SymptomQuality? Quality,
    IReadOnlyList<AssociatedSymptom> AssociatedSymptoms,
    bool? WorsenedByActivity,
    FunctionalImpact? FunctionalImpact,
    IReadOnlyList<Guid> TriggerIds,
    IReadOnlyList<EpisodeIntakeDto> Intakes,
    IReadOnlyList<EpisodeFollowUpDto> FollowUps,
    IReadOnlyList<PhotoRefDto> Photos,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record EpisodeIntakeDto(
    Guid Id,
    Guid DrugId,
    string DrugName,
    string DoseStrength,
    DateTime TakenAt,
    int DoseAmount);

public sealed record EpisodeFollowUpDto(
    Guid Id,
    DateTime ScheduledAt,
    DateTime? AskedAt,
    DateTime? RespondedAt,
    PingResponse? Response,
    int? SeverityAtCheck,
    PingStatus Status);

/// <summary>
/// A due follow-up ping ready to be sent by the dispatcher. Joins the
/// ping with its episode (for user + symptom + severity), the symptom
/// name, and the user's most-recent intake on that episode (for the
/// push payload "~N min after taking X").
/// </summary>
public sealed record PendingPingDto(
    Guid PingId,
    Guid EpisodeId,
    Guid UserId,
    Guid SymptomId,
    string SymptomName,
    DateTime ScheduledAt,
    int Severity,
    DateTime? LastIntakeAt,
    string? LastDrugName,
    int MinutesSinceLastIntake);
