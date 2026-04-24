using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;

public sealed class GetMonthlySummaryHandler : IQueryHandler<GetMonthlySummaryQuery, MonthlySummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public GetMonthlySummaryHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<MonthlySummaryDto> Handle(GetMonthlySummaryQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var selected = new DateOnly(q.Year, q.Month, 1);
        var nextMonth = selected.AddMonths(1);

        // 1. Load reference data
        var groups = await _db.BudgetCategoryGroups
            .Where(g => g.FamilyId == familyId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(ct);
        var categories = await _db.BudgetCategories
            .Where(c => c.FamilyId == familyId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        // 2. Load ALL assignments and transactions up to (inclusive) the selected month.
        var allAssignments = await _db.MonthlyAssignments
            .Where(a => a.FamilyId == familyId
                && (a.Year < q.Year || (a.Year == q.Year && a.Month <= q.Month)))
            .ToListAsync(ct);

        var allTx = await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId && t.CategoryId != null
                     && t.Date < nextMonth)
            .Select(t => new { t.CategoryId, t.Amount, t.Date })
            .ToListAsync(ct);

        // 3. Per-category: walk months and compute Available as of end of selected month,
        //    and Activity / Assigned *for* the selected month itself.
        var groupsDto = new List<EnvelopeGroupDto>();
        decimal totalAssignedThisMonth = 0;
        decimal totalActivityThisMonth = 0;
        decimal totalAvailable = 0;

        foreach (var group in groups.Where(g => !g.IsHidden))
        {
            var envelopes = new List<EnvelopeDto>();
            decimal gAssigned = 0, gActivity = 0, gAvailable = 0;

            foreach (var cat in categories.Where(c => c.GroupId == group.Id && !c.IsHidden))
            {
                var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id).ToList();
                var catTx = allTx.Where(t => t.CategoryId == cat.Id).ToList();

                // Activity per month
                decimal available = 0, assignedThis = 0, activityThis = 0;
                for (int y = 2000; y <= q.Year; y++)
                {
                    int mStart = 1, mEnd = 12;
                    if (y == q.Year) mEnd = q.Month;
                    for (int m = mStart; m <= mEnd; m++)
                    {
                        var a = catAssignments.FirstOrDefault(r => r.Year == y && r.Month == m)?.AssignedAmount ?? 0m;
                        var act = catTx.Where(t => t.Date.Year == y && t.Date.Month == m).Sum(t => t.Amount);
                        available += a + act; // act is negative for spending
                        if (y == q.Year && m == q.Month) { assignedThis = a; activityThis = act; }
                    }
                }

                var progress = ComputeProgress(cat, assignedThis, available, selected);
                envelopes.Add(new EnvelopeDto(
                    cat.Id, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
                    assignedThis, activityThis, available,
                    cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth,
                    progress.Fraction, progress.Hint));

                gAssigned += assignedThis; gActivity += activityThis; gAvailable += available;
            }

            groupsDto.Add(new EnvelopeGroupDto(
                group.Id, group.Name, group.SortOrder, group.IsHidden,
                gAssigned, gActivity, gAvailable, envelopes));
            totalAssignedThisMonth += gAssigned;
            totalActivityThisMonth += gActivity;
            totalAvailable += gAvailable;
        }

        // 4. Income & leftover
        var income = await _db.MonthlyIncomes
            .Where(i => i.FamilyId == familyId && i.Year == q.Year && i.Month == q.Month)
            .Select(i => i.Amount).FirstOrDefaultAsync(ct);

        // LeftOverFromLastMonth = sum of every category's Available *as of end of previous month*
        // = totalAvailable − (assignedThis + activityThis)
        decimal leftOverFromLastMonth = totalAvailable - totalAssignedThisMonth - totalActivityThisMonth;

        // Ready to Assign = income + leftover − totalAssigned
        decimal readyToAssign = income + leftOverFromLastMonth - totalAssignedThisMonth;

        // 5. Accounts
        var accounts = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new BudgetAccountDto(a.Id, a.Name, a.Type, a.Balance, a.SortOrder, a.IsClosed))
            .ToListAsync(ct);

        return new MonthlySummaryDto(
            q.Year, q.Month,
            income, totalAssignedThisMonth, totalActivityThisMonth,
            readyToAssign, leftOverFromLastMonth, totalAvailable,
            groupsDto, accounts);
    }

    private static (decimal? Fraction, string? Hint) ComputeProgress(
        Domain.Entities.BudgetCategory cat, decimal assignedThis, decimal available, DateOnly selectedMonth)
    {
        if (cat.TargetType == BudgetTargetType.None || cat.TargetAmount is null)
            return (null, null);

        var target = cat.TargetAmount.Value;
        if (target <= 0) return (null, null);

        if (cat.TargetType == BudgetTargetType.MonthlyAmount)
        {
            var frac = Math.Clamp(assignedThis / target, 0m, 1m);
            if (assignedThis >= target) return (1m, null);
            var remaining = target - assignedThis;
            var dayPart = cat.TargetDayOfMonth.HasValue ? $" by the {Ordinal(cat.TargetDayOfMonth.Value)}" : " this month";
            return (frac, $"฿{remaining:N2} more needed{dayPart}");
        }

        if (cat.TargetType == BudgetTargetType.ByDate && cat.TargetDueDate.HasValue)
        {
            var frac = Math.Clamp(available / target, 0m, 1m);
            if (available >= target) return (1m, null);
            var remaining = target - available;
            return (frac, $"฿{remaining:N2} more needed by {cat.TargetDueDate.Value:MMM d, yyyy}");
        }

        return (null, null);
    }

    private static string Ordinal(int n) => (n % 100 is >= 11 and <= 13) ? $"{n}th" :
        (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
