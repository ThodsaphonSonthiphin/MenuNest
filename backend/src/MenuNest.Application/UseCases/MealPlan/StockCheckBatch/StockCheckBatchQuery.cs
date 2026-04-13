using Mediator;
using MenuNest.Application.UseCases.MealPlan;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

/// <summary>
/// Aggregated stock check across an arbitrary list of meal plan entries
/// (typically every entry in a single slot, or the user's current
/// selection within the slot detail dialog).
/// </summary>
public sealed record StockCheckBatchQuery(IReadOnlyList<Guid> EntryIds) : IQuery<StockCheckBatchDto>;

public sealed record StockCheckBatchDto(
    IReadOnlyList<StockCheckLineDto> Lines,
    bool IsSufficient)
{
    public int MissingCount => Lines.Count(l => l.Missing > 0m);
}
