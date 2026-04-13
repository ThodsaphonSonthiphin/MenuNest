using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

/// <summary>
/// Cook every entry in <see cref="EntryIds"/> in a single transaction.
/// Aggregates ingredient deductions across all entries, clamps stock
/// at zero, and writes one StockTransaction per ingredient actually
/// deducted. Rejects the batch if any entry is missing, in another
/// family, or already cooked — the UI is expected to refresh and
/// retry in that case.
/// </summary>
public sealed record CookBatchCommand(IReadOnlyList<Guid> EntryIds) : ICommand<CookBatchResult>;

public sealed record CookBatchResult(
    IReadOnlyList<CookDeducted> Deducted,
    IReadOnlyList<CookShortfall> Partial,
    IReadOnlyList<Guid> CookedEntryIds);

public sealed record CookDeducted(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Amount);

public sealed record CookShortfall(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Required,
    decimal Deducted,
    decimal Missing);
