using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Allowance;

/// <summary>
/// The single place the Daily allowance freeze rule lives (menunest-181). Every
/// Budgeting event calls <see cref="RefreezeAsync"/>: marking or unmarking an
/// everyday envelope, assigning into one, and month rollover. Recording a spend
/// must NOT call it.
/// </summary>
public sealed class AllowanceFreezer(IApplicationDbContext db)
{
    /// <summary>
    /// Sum of Available over every everyday-marked envelope, as of <paramref name="asOf"/>'s
    /// month. Returns 0 when nothing is marked — the caller distinguishes "no marks"
    /// from "marks worth nothing" via <see cref="HasMarksAsync"/>.
    /// </summary>
    public async Task<decimal> CurrentPotAsync(Guid familyId, DateOnly asOf, CancellationToken ct)
    {
        var everydayIds = await db.BudgetCategories
            .Where(c => c.FamilyId == familyId && c.IsEveryday)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (everydayIds.Count == 0) return 0m;

        var nextMonth = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(1);

        var assigned = await db.MonthlyAssignments
            .Where(a => a.FamilyId == familyId && everydayIds.Contains(a.CategoryId)
                     && (a.Year < asOf.Year || (a.Year == asOf.Year && a.Month <= asOf.Month)))
            .SumAsync(a => (decimal?)a.AssignedAmount, ct) ?? 0m;

        var activity = await db.BudgetTransactions
            .Where(t => t.FamilyId == familyId && t.CategoryId != null
                     && everydayIds.Contains(t.CategoryId!.Value) && t.Date < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        // Available accumulates from the beginning of time; activity is signed.
        return assigned + activity;
    }

    public Task<bool> HasMarksAsync(Guid familyId, CancellationToken ct) =>
        db.BudgetCategories.AnyAsync(c => c.FamilyId == familyId && c.IsEveryday, ct);

    /// <summary>
    /// Re-freezes the family's figure. Returns null (and stores nothing) when no
    /// envelope is marked — menunest-181's empty state.
    /// </summary>
    public async Task<DailyAllowance?> RefreezeAsync(Guid familyId, DateOnly today, CancellationToken ct)
    {
        if (!await HasMarksAsync(familyId, ct)) return null;

        var pot = await CurrentPotAsync(familyId, today, ct);
        var row = await db.DailyAllowances.FirstOrDefaultAsync(x => x.FamilyId == familyId, ct);

        if (row is null)
        {
            row = DailyAllowance.Freeze(familyId, pot, today);
            db.DailyAllowances.Add(row);
        }
        else
        {
            row.Refreeze(pot, today);
        }
        return row;
    }
}
