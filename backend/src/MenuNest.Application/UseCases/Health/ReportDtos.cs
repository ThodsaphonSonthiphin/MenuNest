using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// The full doctor-report payload returned from the public token-gated
/// endpoint. Mirrors the layout of <c>docs/mocks/doctor-report-mock.html</c>.
/// All time-of-day calculations are done in UTC; the frontend converts
/// to the viewer's local time zone for display.
/// </summary>
public sealed record DoctorReportDto(
    string PatientName,
    DateOnly DateFrom,
    DateOnly DateTo,
    int DurationDays,
    DateTime GeneratedAtUtc,
    DoctorReportSummary Summary,
    IReadOnlyList<DoctorReportFlag> ClinicalFlags,
    IReadOnlyList<TriggerCorrelationDto> TriggerCorrelations,
    IReadOnlyList<TreatmentEfficacyDto> TreatmentEfficacy,
    DoctorReportPatterns Patterns,
    IReadOnlyList<NoDrugEventDto> NoDrugEvents,
    IReadOnlyList<DoctorReportDay> Days);

public sealed record DoctorReportSummary(
    int TotalAttacks,
    int DaysAffected,
    int AcuteMedDays,
    double AverageDurationHours,
    double AveragePeakSeverity,
    int SevereAttacksCount,
    int DaysFullyDisabled,
    int AttacksWithAura,
    double AuraPercentage);

/// <param name="Code">Machine-readable identifier — one of <c>MOH_RISK</c>,
/// <c>FREQUENCY_NEAR_CHRONIC</c>, <c>FUNCTIONAL_DISABILITY</c>.</param>
/// <param name="Severity">Either <c>danger</c> or <c>warning</c>.</param>
public sealed record DoctorReportFlag(
    string Code,
    string Severity,
    string Title,
    string Detail);

public sealed record TriggerCorrelationDto(
    Guid TriggerId,
    string TriggerName,
    int AttackCount,
    double Percentage);

public sealed record TreatmentEfficacyDto(
    Guid DrugId,
    string DrugName,
    DrugType DrugType,
    int DoseCount,
    int ReliefCount,
    double ReliefPercentage,
    double AverageOnsetMinutes);

public sealed record DoctorReportPatterns(
    IReadOnlyDictionary<string, int> OnsetTimeBuckets,
    IReadOnlyDictionary<DayOfWeek, int> DayOfWeekCounts,
    int AttacksDuringPeriod,
    int AttacksOutsidePeriod,
    double AttackRateDuringPeriod,
    double AttackRateOutsidePeriod);

public sealed record NoDrugEventDto(
    Guid EpisodeId,
    DateTime StartedAt,
    string SymptomName,
    int Severity,
    NoDrugReason? Reason);

public sealed record DoctorReportDay(
    DateOnly Date,
    bool IsPeriodDay,
    int AttackCount,
    int PeakSeverity,
    int DoseCount,
    int NoDrugEvents,
    IReadOnlyList<DoctorReportEpisode> Episodes);

public sealed record DoctorReportEpisode(
    Guid Id,
    Guid SymptomId,
    string SymptomName,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Severity,
    int? SeverityAfter,
    bool? HasAura,
    SymptomLocation? Location,
    SymptomQuality? Quality,
    IReadOnlyList<AssociatedSymptom> AssociatedSymptoms,
    FunctionalImpact? FunctionalImpact,
    bool IsOnPeriod,
    bool NoDrugTaken,
    NoDrugReason? NoDrugReasonCode,
    IReadOnlyList<DoctorReportEpisodeIntake> Intakes,
    IReadOnlyList<DoctorReportEpisodeFollowUp> FollowUps,
    IReadOnlyList<Guid> TriggerIds);

public sealed record DoctorReportEpisodeIntake(
    DateTime TakenAt,
    string DrugName,
    int DoseAmount);

public sealed record DoctorReportEpisodeFollowUp(
    DateTime ScheduledAt,
    DateTime? RespondedAt,
    PingResponse? Response,
    int? SeverityAtCheck);
