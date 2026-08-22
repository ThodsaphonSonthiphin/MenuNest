using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;

public sealed class GetMonthlySummaryHandler : IQueryHandler<GetMonthlySummaryQuery, MonthlySummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly AllowanceFreezer _freezer;
    private readonly IClock _clock;

    // Named projection for in-memory transaction rows (allows passing to static helpers).
    private readonly record struct TxRow(Guid? CategoryId, decimal Amount, DateOnly Date);

    public GetMonthlySummaryHandler(
        IApplicationDbContext db, IUserProvisioner users, AllowanceFreezer freezer, IClock clock)
    { _db = db; _users = users; _freezer = freezer; _clock = clock; }

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
            .Select(t => new TxRow(t.CategoryId, t.Amount, t.Date))
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

                var (available, assignedThis, activityThis) =
                    ComputeEnvelopeAvailable(catAssignments, catTx, q.Year, q.Month);

                var progress = ComputeProgress(cat, assignedThis, available, selected);
                envelopes.Add(new EnvelopeDto(
                    cat.Id, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
                    assignedThis, activityThis, available,
                    cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth,
                    progress.Fraction, progress.Hint, cat.IsEveryday));

                gAssigned += assignedThis; gActivity += activityThis; gAvailable += available;
            }

            groupsDto.Add(new EnvelopeGroupDto(
                group.Id, group.Name, group.SortOrder, group.IsHidden,
                gAssigned, gActivity, gAvailable, envelopes));
            totalAssignedThisMonth += gAssigned;
            totalActivityThisMonth += gActivity;
            totalAvailable += gAvailable;
        }

        // 4. All-cats envelope available (including hidden) for RTA calculation.
        // allTx already excludes uncategorized rows (CategoryId != null filter above),
        // so this sums only money that landed in envelopes. Uncategorized inflows are
        // reflected in totalAccountBalance instead.
        decimal totalEnvelopeAvailableAllCats = 0;
        foreach (var cat in categories)
        {
            var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id).ToList();
            var catTx          = allTx.Where(t => t.CategoryId == cat.Id).ToList();
            var (available, _, _) = ComputeEnvelopeAvailable(catAssignments, catTx, q.Year, q.Month);
            totalEnvelopeAvailableAllCats += available;
        }

        // 5a. Derived account balances as of the END of the selected month
        //     (menunest-182). One grouped query, not one per account.
        var balancesByAccount = (await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId && t.Date < nextMonth)
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct))
            .ToDictionary(x => x.AccountId, x => x.Total);

        decimal DerivedBalance(Guid accountId) =>
            balancesByAccount.TryGetValue(accountId, out var total) ? total : 0m;

        // 5. Total account balance.
        var accountIds = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId).Select(a => a.Id).ToListAsync(ct);
        var totalAccountBalance = accountIds.Sum(DerivedBalance);

        // 6. Income = positive uncategorized inflows for the selected month.
        var income = await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId
                     && t.CategoryId == null
                     && t.Amount > 0m
                     && t.Date >= selected && t.Date < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        // RTA = sum(accounts) − sum(envelope.available across ALL categories)
        decimal readyToAssign = totalAccountBalance - totalEnvelopeAvailableAllCats;

        // 7. Accounts list for the UI. DerivedBalance is a local function EF cannot
        //    translate, so materialise the entities first and project in memory.
        var accountRows = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);
        var accounts = accountRows
            .Select(a => new BudgetAccountDto(
                a.Id, a.Name, a.Type, DerivedBalance(a.Id), a.SortOrder, a.IsClosed))
            .ToList();

        // menunest-185/189: the card is current-month only, checked against
        // today's real date — the VIEWER's local day, resolved from the caller's
        // IANA time zone and the injected clock, never the server's UTC day
        // (ADR-038's Trips pattern applied to Budget). Every read that could show
        // the card needs the zone, so it is resolved unconditionally here, before
        // we even know whether the requested month is "current" — a missing or
        // unknown id is rejected, never silently read as UTC.
        var tz = BudgetTimeZone.Resolve(q.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));
        DailyAllowanceDto? allowance = null;
        if (q.Year == today.Year && q.Month == today.Month)
        {
            var row = await _db.DailyAllowances.FirstOrDefaultAsync(x => x.FamilyId == familyId, ct);

            // Month rollover is a Budgeting event, applied lazily on first read of
            // a new month (menunest-181). Idempotent; happens once per family.
            if (row is null || !row.IsForMonth(today.Year, today.Month))
            {
                row = await _freezer.RefreezeAsync(familyId, today, ct);
                if (row is not null) await _db.SaveChangesAsync(ct);
            }

            var hasMarks = await _freezer.HasMarksAsync(familyId, ct);
            if (row is not null && hasMarks)
            {
                var currentPot = await _freezer.CurrentPotAsync(familyId, today, ct);
                allowance = new DailyAllowanceDto(
                    row.Amount, row.FrozenOn, row.PaceDelta(currentPot, today), HasMarks: true);
            }
            else
            {
                allowance = new DailyAllowanceDto(0m, today, 0m, HasMarks: false);
            }
        }

        return new MonthlySummaryDto(
            q.Year, q.Month,
            income, totalAssignedThisMonth, totalActivityThisMonth,
            readyToAssign, totalAvailable,
            groupsDto, accounts, allowance);
    }

    /// <summary>
    /// Walks every month from Jan 2000 through (<paramref name="year"/>, <paramref name="month"/>) and
    /// accumulates the running envelope balance (assignments + activity).  Returns the cumulative
    /// <c>Available</c> together with the <c>AssignedThisMonth</c> and <c>ActivityThisMonth</c> values
    /// for the selected month, so callers that only need a subset can discard the rest.
    /// </summary>
    private static (decimal Available, decimal AssignedThisMonth, decimal ActivityThisMonth)
        ComputeEnvelopeAvailable(
            IReadOnlyList<Domain.Entities.MonthlyAssignment> catAssignments,
            IReadOnlyList<TxRow> catTx,
            int year, int month)
    {
        decimal available = 0, assignedThis = 0, activityThis = 0;
        for (int y = 2000; y <= year; y++)
        {
            int mEnd = (y == year) ? month : 12;
            for (int m = 1; m <= mEnd; m++)
            {
                var a   = catAssignments.FirstOrDefault(r => r.Year == y && r.Month == m)?.AssignedAmount ?? 0m;
                var act = catTx.Where(t => t.Date.Year == y && t.Date.Month == m).Sum(t => t.Amount);
                available += a + act; // act is negative for spending
                if (y == year && m == month) { assignedThis = a; activityThis = act; }
            }
        }
        return (available, assignedThis, activityThis);
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
