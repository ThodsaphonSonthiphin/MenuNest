using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts;

/// <summary>
/// Makes sure every Credit account in a family has its Payment envelope
/// (menunest-202). Called on account creation and, for accounts that predate
/// this feature, lazily on the first summary read (menunest-181's precedent).
/// A Loan account never gets one (menunest-206).
///
/// Idempotent against the one failure mode that would otherwise surface as a
/// user-visible error: two concurrent callers racing to create the envelope
/// for the SAME account. The filtered unique index on
/// <see cref="BudgetCategory.PaymentForAccountId"/> rejects the loser's
/// insert; <see cref="EnsureForFamilyAndSaveAsync"/> catches exactly that
/// failure and reports 0 created instead of letting the DbUpdateException
/// surface as an HTTP 500.
///
/// NOT protected: two concurrent callers provisioning for two DIFFERENT
/// Credit accounts in the same family can each find no "บัตรเครดิต" group —
/// the lookup-then-insert below has no DB-level uniqueness behind it
/// (BudgetCategoryGroupConfiguration's only index is (FamilyId, SortOrder),
/// not (FamilyId, Name)) — and each create one, leaving two identical group
/// headers. This is accepted, not fixed: every money figure is derived from
/// PaymentForAccountId, never from group membership (menunest-208), so a
/// duplicate group cannot miscount anything — the worst case is a cosmetic
/// duplicate header. A DB-level fix (a unique index on (FamilyId, Name)) was
/// rejected: it would constrain every group in the app, not just this
/// feature's, and needs a migration that cannot be verified safe against
/// whatever group names already exist in production. The deterministic group
/// lookup below (ordered by SortOrder, then Id) at least makes every later
/// call converge on the SAME one of the two groups if that race is ever hit,
/// instead of splitting envelopes across both indefinitely.
/// </summary>
public sealed class PaymentEnvelopeProvisioner(IApplicationDbContext db)
{
    public const string CreditGroupName = "บัตรเครดิต";

    /// <summary>
    /// Stages missing envelopes on the change tracker WITHOUT saving — the
    /// caller owns the unit of work, so this is safe to fold into a larger
    /// SaveChangesAsync elsewhere. On its own it offers no protection against
    /// a concurrent duplicate insert; use
    /// <see cref="EnsureForFamilyAndSaveAsync"/> when this is the only thing
    /// being saved and a graceful loss of the race matters.
    /// </summary>
    /// <returns>How many envelopes were added to the change tracker.</returns>
    public async Task<int> EnsureForFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var staged = await StageAsync(familyId, ct);
        return staged.Count;
    }

    /// <summary>
    /// Stages missing envelopes (as <see cref="EnsureForFamilyAsync"/>) and
    /// immediately saves them in their own unit of work. If a concurrent
    /// caller already committed the envelope for the SAME account first, the
    /// filtered unique index on PaymentForAccountId rejects ours — that
    /// specific DbUpdateException is caught, our pending additions are
    /// discarded, and this returns 0 rather than throwing. Any other
    /// DbUpdateException — a real failure unrelated to this race — is NOT
    /// swallowed; it propagates.
    /// </summary>
    public async Task<int> EnsureForFamilyAndSaveAsync(Guid familyId, CancellationToken ct)
    {
        var staged = await StageAsync(familyId, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return staged.Count;
        }
        catch (DbUpdateException ex) when (LooksLikeDuplicatePaymentEnvelope(ex))
        {
            // Another caller won the race and already committed. Discard what
            // WE staged (Remove on a never-saved Added entity detaches it —
            // no DELETE is issued) rather than leaving it stuck in the
            // tracker, and report 0: the account already has its envelope,
            // just not one we created.
            foreach (var category in staged.CreatedCategories)
                db.BudgetCategories.Remove(category);
            if (staged.CreatedGroup is not null)
                db.BudgetCategoryGroups.Remove(staged.CreatedGroup);
            return 0;
        }
    }

    private readonly record struct StagedResult(
        int Count, BudgetCategoryGroup? CreatedGroup, List<BudgetCategory> CreatedCategories);

    private static readonly StagedResult NoneStaged = new(0, null, new List<BudgetCategory>());

    private async Task<StagedResult> StageAsync(Guid familyId, CancellationToken ct)
    {
        var creditIds = await db.BudgetAccounts
            .Where(a => a.FamilyId == familyId && a.Type == BudgetAccountType.Credit)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        if (creditIds.Count == 0) return NoneStaged;

        var covered = await db.BudgetCategories
            .Where(c => c.FamilyId == familyId && c.PaymentForAccountId != null)
            .Select(c => c.PaymentForAccountId!.Value)
            .ToListAsync(ct);

        var missing = creditIds.Where(a => !covered.Contains(a.Id)).ToList();
        if (missing.Count == 0) return NoneStaged;

        // Ordered so that if the accepted concurrent-group race has already
        // left two "บัตรเครดิต" groups for this family, every caller
        // converges on the same one (lowest SortOrder, ties broken by Id)
        // instead of splitting new envelopes across both.
        var group = await db.BudgetCategoryGroups
            .Where(g => g.FamilyId == familyId && g.Name == CreditGroupName)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .FirstOrDefaultAsync(ct);

        BudgetCategoryGroup? createdGroup = null;
        if (group is null)
        {
            var nextGroupSort = (await db.BudgetCategoryGroups
                .Where(g => g.FamilyId == familyId)
                .MaxAsync(g => (int?)g.SortOrder, ct) ?? -1) + 1;
            group = BudgetCategoryGroup.Create(familyId, CreditGroupName, nextGroupSort);
            db.BudgetCategoryGroups.Add(group);
            createdGroup = group;
        }

        var nextSort = (await db.BudgetCategories
            .Where(c => c.GroupId == group.Id)
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;

        var createdCategories = new List<BudgetCategory>();
        foreach (var acc in missing)
        {
            var envelope = BudgetCategory.CreatePaymentEnvelope(
                familyId, group.Id, acc.Id, acc.Name, nextSort++);
            db.BudgetCategories.Add(envelope);
            createdCategories.Add(envelope);
        }
        return new StagedResult(missing.Count, createdGroup, createdCategories);
    }

    /// <summary>
    /// Narrows the catch in <see cref="EnsureForFamilyAndSaveAsync"/> to the
    /// one failure it exists to swallow — a second insert rejected by the
    /// filtered unique index on PaymentForAccountId. Deliberately NOT
    /// narrowed by exception TYPE (SqlException / SqliteException): the
    /// Application layer takes no package dependency on either ADO provider,
    /// so this method only ever sees the provider-agnostic DbUpdateException.
    /// This matches on message text instead — the best narrowing available at
    /// this layer without adding a provider-specific reference just to catch
    /// one exception. Verified against both providers this codebase actually
    /// runs on: SQL Server's "Violation of UNIQUE KEY constraint
    /// 'IX_BudgetCategories_PaymentForAccountId' ... duplicate key ..." and
    /// SQLite's "UNIQUE constraint failed: BudgetCategories.PaymentForAccountId"
    /// (see the fix report for the exact captured SQLite text). If neither
    /// substring matches, this is some other failure and is left to
    /// propagate rather than being silently absorbed.
    /// </summary>
    private static bool LooksLikeDuplicatePaymentEnvelope(DbUpdateException ex)
    {
        var text = $"{ex.Message} {ex.InnerException?.Message}";
        return text.Contains("PaymentForAccountId", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase)
                || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
    }
}
