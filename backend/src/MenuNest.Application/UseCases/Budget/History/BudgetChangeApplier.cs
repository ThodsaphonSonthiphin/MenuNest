using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History;

/// <summary>
/// The ONLY place the inverse of a recorded change is computed, so undo and
/// redo can never drift apart: redo is the same arithmetic with the sign
/// flipped. Every path applies a COMPENSATING delta (menunest-193) — nothing
/// here ever restores a stored old value.
/// </summary>
public sealed class BudgetChangeApplier
{
    private readonly IApplicationDbContext _db;
    public BudgetChangeApplier(IApplicationDbContext db) => _db = db;

    /// <param name="direction">-1 to undo, +1 to redo.</param>
    public async Task ApplyAsync(BudgetChange change, int direction, CancellationToken ct)
    {
        if (direction != -1 && direction != 1)
            throw new DomainException("Direction must be -1 or +1.");

        switch (change.Kind)
        {
            case BudgetChangeKind.Assign:
                var row = await RequireAssignmentAsync(
                    change.FamilyId, change.CategoryId, change.Year, change.Month, ct);
                row.AdjustAmount(change.Delta * direction);
                break;

            case BudgetChangeKind.Move:
            case BudgetChangeKind.Cover:
                if (change.SecondCategoryId is null)
                    throw new DomainException("A move change is missing its destination.");
                var from = await RequireAssignmentAsync(
                    change.FamilyId, change.CategoryId, change.Year, change.Month, ct);
                var to = await RequireAssignmentAsync(
                    change.FamilyId, change.SecondCategoryId.Value, change.Year, change.Month, ct);
                from.AdjustAmount(change.Delta * direction);
                to.AdjustAmount(-change.Delta * direction);
                break;

            case BudgetChangeKind.EverydayMark:
                if (change.FlagValue is null)
                    throw new DomainException("An everyday-mark change is missing its value.");
                var cat = await _db.BudgetCategories.FirstOrDefaultAsync(
                    c => c.Id == change.CategoryId && c.FamilyId == change.FamilyId, ct)
                    ?? throw new DomainException("That envelope no longer exists.");
                cat.MarkEveryday(direction == 1 ? change.FlagValue.Value : !change.FlagValue.Value);
                break;

            default:
                throw new DomainException("Unknown change kind.");
        }
    }

    private async Task<MonthlyAssignment> RequireAssignmentAsync(
        Guid familyId, Guid categoryId, int year, int month, CancellationToken ct)
    {
        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == categoryId
              && x.Year == year && x.Month == month, ct);
        if (row is not null) return row;

        // The assignment row can legitimately be absent — a move whose
        // destination was never assigned again. Create it at zero and let the
        // delta land on it, exactly as MoveMoneyHandler's GetOrCreateAsync does.
        var belongs = await _db.BudgetCategories.AnyAsync(
            c => c.Id == categoryId && c.FamilyId == familyId, ct);
        if (!belongs) throw new DomainException("That envelope no longer exists.");

        var created = MonthlyAssignment.Create(familyId, categoryId, year, month, 0m);
        _db.MonthlyAssignments.Add(created);
        return created;
    }
}
