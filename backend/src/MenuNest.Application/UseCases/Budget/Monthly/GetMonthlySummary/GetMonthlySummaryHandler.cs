using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;

public sealed class GetMonthlySummaryHandler : IQueryHandler<GetMonthlySummaryQuery, MonthlySummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly AllowanceFreezer _freezer;
    private readonly PaymentEnvelopeProvisioner _envelopes;
    private readonly IClock _clock;

    // Named projection for in-memory transaction rows (allows passing to static helpers).
    private readonly record struct TxRow(Guid? CategoryId, decimal Amount, DateOnly Date);

    public GetMonthlySummaryHandler(
        IApplicationDbContext db, IUserProvisioner users, AllowanceFreezer freezer,
        PaymentEnvelopeProvisioner envelopes, IClock clock)
    { _db = db; _users = users; _freezer = freezer; _envelopes = envelopes; _clock = clock; }

    public async ValueTask<MonthlySummaryDto> Handle(GetMonthlySummaryQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var selected = new DateOnly(q.Year, q.Month, 1);
        var nextMonth = selected.AddMonths(1);

        // menunest-202 / menunest-181's precedent: provision lazily on read, so
        // Credit accounts that predate this feature gain their envelope on the
        // first page load rather than needing a data backfill. The SAVING
        // variant, deliberately: this is the hottest read in the app (every
        // /budget load and every RTK Query refetch, from two devices at once),
        // so the duplicate-key race is real here and must degrade to "someone
        // else already created it" rather than to an HTTP 500.
        await _envelopes.EnsureForFamilyAndSaveAsync(familyId, ct);

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

        // 2a. Accounts, their derived balances, and the Credit rows the Payment
        //     envelopes are derived from. All three are needed by the envelope
        //     loops below, so they are loaded before them.
        var accountRows = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);

        // Derived account balances as of the END of the selected month
        // (menunest-182). One grouped query, not one per account.
        var balancesByAccount = (await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId && t.Date < nextMonth)
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct))
            .ToDictionary(x => x.AccountId, x => x.Total);

        decimal DerivedBalance(Guid accountId) =>
            balancesByAccount.TryGetValue(accountId, out var total) ? total : 0m;

        // Every row on a Credit account, for the payment-envelope derivation (§4.2).
        // Unlike allTx this keeps the UNcategorised rows — a payment and a cash
        // advance are both uncategorised and both matter to the derivation.
        var creditIds = accountRows
            .Where(a => a.Type == BudgetAccountType.Credit).Select(a => a.Id).ToHashSet();
        // Skipped entirely for a family with no Credit account — otherwise this
        // is a guaranteed-empty round trip on the hottest read in the app.
        var creditRowsByAccount = creditIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<TxRow>>()
            : (await _db.BudgetTransactions
                    .Where(t => t.FamilyId == familyId && t.Date < nextMonth
                             && creditIds.Contains(t.AccountId))
                    .Select(t => new { t.AccountId, t.CategoryId, t.Amount, t.Date })
                    .ToListAsync(ct))
                .GroupBy(t => t.AccountId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<TxRow>)g
                        .Select(t => new TxRow(t.CategoryId, t.Amount, t.Date))
                        .ToList());

        // A Payment envelope is derived from its card's rows (menunest-208), not
        // from the assignment-plus-activity walk every other Envelope uses.
        // CardSpending is null for an ordinary envelope; R-1 for a Payment one.
        (decimal Available, decimal Assigned, decimal Activity, decimal? CardSpending) EnvelopeNumbers(
            Domain.Entities.BudgetCategory cat)
        {
            var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id).ToList();
            if (cat.PaymentForAccountId is not { } accId)
            {
                var catTx = allTx.Where(t => t.CategoryId == cat.Id).ToList();
                var (available0, assignedThis0, activityThis0) =
                    ComputeEnvelopeAvailable(catAssignments, catTx, q.Year, q.Month);
                return (available0, assignedThis0, activityThis0, null);
            }

            var assignedToDate = catAssignments.Sum(a => a.AssignedAmount);
            var rows = creditRowsByAccount.TryGetValue(accId, out var r)
                ? r : Array.Empty<TxRow>();
            var available = PaymentEnvelopeMath.Available(
                assignedToDate,
                rows.Select(t => new PaymentEnvelopeMath.AccountTxRow(t.CategoryId, t.Amount)));
            var assignedThis = catAssignments
                .FirstOrDefault(a => a.Year == q.Year && a.Month == q.Month)?.AssignedAmount ?? 0m;
            // Activity on a Payment envelope is money that left it: every
            // uncategorised inflow to the card this month — payments, and
            // anything else that reduces the envelope (an uncategorised merchant
            // refund, a cashback credit). That is exactly the in-month part of
            // the uncategorisedInflow term Available subtracts, so Activity and
            // Available never disagree. Signed negative, like every other
            // envelope's spending.
            var activityThis = -rows
                .Where(t => t.CategoryId == null && t.Amount > 0m
                         && t.Date.Year == q.Year && t.Date.Month == q.Month)
                .Sum(t => t.Amount);
            // R-1: CardSpending is the term Available loses vs. Assigned+Activity
            // for a Payment envelope — every categorised row on the card this
            // month, negated so a purchase (a negative tx) reads as positive
            // spending. Together with assignedThis and activityThis this is
            // exactly the month-over-month change decomposition of
            // PaymentEnvelopeMath.Available (assigned − categorised −
            // uncategorisedInflow), so Available == assignedThis + cardSpendingThis
            // + activityThis whenever there is no carried-in Available from a
            // prior month (§4.3/R-1).
            var cardSpendingThis = -rows
                .Where(t => t.CategoryId != null
                         && t.Date.Year == q.Year && t.Date.Month == q.Month)
                .Sum(t => t.Amount);
            return (available, assignedThis, activityThis, cardSpendingThis);
        }

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
                var (available, assignedThis, activityThis, cardSpendingThis) = EnvelopeNumbers(cat);

                var progress = ComputeProgress(cat, assignedThis, available, selected);
                envelopes.Add(new EnvelopeDto(
                    cat.Id, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
                    assignedThis, activityThis, available,
                    cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth,
                    progress.Fraction, progress.Hint, cat.IsEveryday,
                    cat.PaymentForAccountId,
                    cat.PaymentForAccountId is { } payAcc
                        ? PaymentEnvelopeMath.Shortfall(DerivedBalance(payAcc), available)
                        : null,
                    cardSpendingThis));

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
        // For ordinary envelopes allTx already excludes uncategorized rows
        // (CategoryId != null filter above), so this sums only money that landed
        // in envelopes; uncategorized inflows are reflected in totalAccountBalance
        // instead. Payment envelopes MUST be in this sum: they are what holds a
        // card's funded debt back, now that the card itself has left the balance.
        decimal totalEnvelopeAvailableAllCats = 0;
        // §4.3: Available per Payment envelope's account, for the account-level
        // Shortfall computed below — keyed by the CARD's account id, not the
        // envelope's category id.
        var availableByPaymentEnvelope = new Dictionary<Guid, decimal>();
        foreach (var cat in categories)
        {
            var (available, _, _, _) = EnvelopeNumbers(cat);
            totalEnvelopeAvailableAllCats += available;
            if (cat.PaymentForAccountId is { } accId) availableByPaymentEnvelope[accId] = available;
        }

        // 5. Total account balance.
        // menunest-203 / menunest-206: Credit and Loan leave Ready to Assign.
        // Their debt is held by a Payment envelope (cards) or by an ordinary
        // Envelope the User made (loans) — counting the negative balance as well
        // would hold the same money back twice.
        var totalAccountBalance = accountRows
            .Where(a => !PaymentEnvelopeMath.IsDebtType(a.Type))
            .Sum(a => DerivedBalance(a.Id));

        // 6. Income = positive uncategorized inflows for the selected month.
        var income = await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId
                     && t.CategoryId == null
                     && t.PaymentId == null      // menunest-204: paying your own card is not income
                     && t.Amount > 0m
                     && t.Date >= selected && t.Date < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        // RTA = sum(accounts) − sum(envelope.available across ALL categories)
        decimal readyToAssign = totalAccountBalance - totalEnvelopeAvailableAllCats;

        // 7. Accounts list for the UI. DerivedBalance is a local function EF cannot
        //    translate, so the entities were materialised at step 2a and are
        //    projected in memory here. Every account is listed, including the
        //    debt ones dropped from totalAccountBalance — they leave Ready to
        //    Assign, not the UI.
        var shortfallByAccount = accountRows
            .Where(a => a.Type == BudgetAccountType.Credit)
            .ToDictionary(a => a.Id, a => PaymentEnvelopeMath.Shortfall(
                DerivedBalance(a.Id), availableByPaymentEnvelope.GetValueOrDefault(a.Id)));

        var accounts = accountRows
            .Select(a => new BudgetAccountDto(
                a.Id, a.Name, a.Type, DerivedBalance(a.Id), a.SortOrder, a.IsClosed,
                shortfallByAccount.TryGetValue(a.Id, out var sf) ? sf : null))
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
