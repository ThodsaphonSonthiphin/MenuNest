using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Budget.History;

/// <summary>
/// The single place a <see cref="BudgetChange"/> is written. It deliberately
/// does NOT save: the calling handler's own SaveChangesAsync commits the act
/// and its history row together, so a recorded change can never outlive a
/// failed write.
/// </summary>
public sealed class BudgetChangeRecorder
{
    private readonly IApplicationDbContext _db;
    public BudgetChangeRecorder(IApplicationDbContext db) => _db = db;

    public void Record(BudgetChange change) => _db.BudgetChanges.Add(change);
}
