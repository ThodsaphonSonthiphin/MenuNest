using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.DrugMaster.UpdateDrug;

public sealed record UpdateDrugCommand(
    Guid Id,
    string Name,
    DrugType DrugType,
    string DoseStrength,
    int EffectDurationMinHours,
    int EffectDurationMaxHours,
    int MaxDailyDose,
    int StockCount,
    string? ActiveIngredient,
    DateOnly? ExpirationDate,
    string? UsageNote,
    IReadOnlyList<Guid>? TreatsSymptomIds) : ICommand<DrugDetailDto>;
