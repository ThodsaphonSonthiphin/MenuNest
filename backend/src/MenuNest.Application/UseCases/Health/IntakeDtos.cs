using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// A logged intake — returned after <c>POST /api/intakes</c>.
/// </summary>
public sealed record IntakeDto(
    Guid Id,
    Guid DrugId,
    string DrugName,
    Guid? SymptomEpisodeId,
    DateTime TakenAt,
    int DoseAmount);

/// <summary>
/// Context for the Take Medication screen given a specific (optional)
/// active episode. The picker uses these three groups to decide what to
/// show as enabled / disabled / hidden.
/// </summary>
public sealed record TakeMedicationContextDto(
    Guid? SymptomEpisodeId,
    Guid? SymptomId,
    string? SymptomName,
    int? CurrentSeverity,
    IReadOnlyList<ActiveDrugDto> ActiveDrugs,
    IReadOnlyList<TakeableDrugDto> TakeableDrugs,
    IReadOnlyList<BlockedDrugDto> BlockedDrugs);

/// <summary>
/// A drug whose last intake is still within its effect window — user
/// should NOT take another dose.
/// </summary>
public sealed record ActiveDrugDto(
    Guid DrugId,
    string DrugName,
    string DoseStrength,
    DateTime LastTakenAt,
    DateTime EffectEndsAt,
    int RemainingMinutes,
    double ProgressPct);

/// <summary>
/// A drug that can be taken right now — within max-daily, not active,
/// and in stock.
/// </summary>
public sealed record TakeableDrugDto(
    Guid DrugId,
    string DrugName,
    string DoseStrength,
    DrugType DrugType,
    int StockCount,
    int EffectDurationMinHours,
    int EffectDurationMaxHours);

/// <summary>
/// A drug that treats the current symptom but is currently unavailable.
/// </summary>
public sealed record BlockedDrugDto(
    Guid DrugId,
    string DrugName,
    string DoseStrength,
    BlockedReason Reason,
    DateTime? AvailableAt);

public enum BlockedReason
{
    MaxDoseReached = 1,
    StillActive = 2,
    OutOfStock = 3
}
