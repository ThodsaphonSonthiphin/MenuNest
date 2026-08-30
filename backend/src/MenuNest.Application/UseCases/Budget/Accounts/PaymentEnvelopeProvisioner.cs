using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts;

/// <summary>
/// Makes sure every Credit account in a family has its Payment envelope
/// (menunest-202). Idempotent, and it does NOT save — the caller owns the unit
/// of work. Called on account creation and, for accounts that predate this
/// feature, lazily on the first summary read (menunest-181's precedent).
/// A Loan account never gets one (menunest-206).
/// </summary>
public sealed class PaymentEnvelopeProvisioner(IApplicationDbContext db)
{
    public const string CreditGroupName = "บัตรเครดิต";

    /// <returns>How many envelopes were added to the change tracker.</returns>
    public async Task<int> EnsureForFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var creditIds = await db.BudgetAccounts
            .Where(a => a.FamilyId == familyId && a.Type == BudgetAccountType.Credit)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        if (creditIds.Count == 0) return 0;

        var covered = await db.BudgetCategories
            .Where(c => c.FamilyId == familyId && c.PaymentForAccountId != null)
            .Select(c => c.PaymentForAccountId!.Value)
            .ToListAsync(ct);

        var missing = creditIds.Where(a => !covered.Contains(a.Id)).ToList();
        if (missing.Count == 0) return 0;

        var group = await db.BudgetCategoryGroups
            .FirstOrDefaultAsync(g => g.FamilyId == familyId && g.Name == CreditGroupName, ct);
        if (group is null)
        {
            var nextGroupSort = (await db.BudgetCategoryGroups
                .Where(g => g.FamilyId == familyId)
                .MaxAsync(g => (int?)g.SortOrder, ct) ?? -1) + 1;
            group = BudgetCategoryGroup.Create(familyId, CreditGroupName, nextGroupSort);
            db.BudgetCategoryGroups.Add(group);
        }

        var nextSort = (await db.BudgetCategories
            .Where(c => c.GroupId == group.Id)
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;

        foreach (var acc in missing)
        {
            db.BudgetCategories.Add(BudgetCategory.CreatePaymentEnvelope(
                familyId, group.Id, acc.Id, acc.Name, nextSort++));
        }
        return missing.Count;
    }
}
