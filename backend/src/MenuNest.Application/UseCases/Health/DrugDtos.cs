using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health;

/// <summary>
/// List/summary projection of a drug — used in the Drug Master list and
/// the picker on the Take Medication screen.
/// </summary>
public sealed record DrugDto(
    Guid Id,
    string Name,
    string? ActiveIngredient,
    DrugType DrugType,
    string DoseStrength,
    int EffectDurationMinHours,
    int EffectDurationMaxHours,
    int MaxDailyDose,
    int StockCount,
    DateOnly? ExpirationDate,
    IReadOnlyList<Guid> TreatsSymptomIds,
    bool HasPhoto,
    string? FirstPhotoUrl);

/// <summary>
/// Full drug detail — includes all photos and the timestamps. Returned by
/// <c>GET /api/drugs/{id}</c>.
/// </summary>
public sealed record DrugDetailDto(
    Guid Id,
    string Name,
    string? ActiveIngredient,
    DrugType DrugType,
    string DoseStrength,
    int EffectDurationMinHours,
    int EffectDurationMaxHours,
    int MaxDailyDose,
    int StockCount,
    DateOnly? ExpirationDate,
    string? UsageNote,
    IReadOnlyList<Guid> TreatsSymptomIds,
    IReadOnlyList<PhotoRefDto> Photos,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// Lightweight photo reference embedded in detail/list DTOs. The
/// <c>Url</c> is plain (no SAS); the API attaches a read-SAS at
/// response time if needed.
/// </summary>
public sealed record PhotoRefDto(Guid Id, string Url, long FileSize, string ContentType);
