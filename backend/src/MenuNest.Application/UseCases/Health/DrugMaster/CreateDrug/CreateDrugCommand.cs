using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.DrugMaster.CreateDrug;

/// <summary>
/// Creates a drug. Photos are attached separately via
/// <c>POST /api/drugs/{id}/photos</c> after the client has finished
/// direct-to-blob upload via a SAS URL — only at that point are
/// <c>fileSize</c> and <c>contentType</c> known.
/// </summary>
public sealed record CreateDrugCommand(
    string Name,
    DrugType DrugType,
    string DoseStrength,
    int EffectDurationMinHours,
    int EffectDurationMaxHours,
    int MaxDailyDose,
    int StockCount = 0,
    string? ActiveIngredient = null,
    DateOnly? ExpirationDate = null,
    string? UsageNote = null,
    IReadOnlyList<Guid>? TreatsSymptomIds = null) : ICommand<DrugDetailDto>;
