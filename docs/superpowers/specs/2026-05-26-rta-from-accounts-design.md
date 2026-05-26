# RTA Derived from Accounts — Design

**Status:** Draft
**Date:** 2026-05-26
**Author:** thodsaphon.sonthipin

## Background

The current budget model has three layers that don't connect to each other:

1. **`BudgetAccount.Balance`** — set by `openingBalance` at create time, updated by `BudgetTransaction.AdjustBalance` on each transaction
2. **`MonthlyIncome`** table — a per-(family, year, month) row populated only by the `PUT /api/budget/monthly/income` endpoint (the "Set Income" dialog)
3. **`MonthlyAssignment`** + envelope `available` — money pushed from RTA into categories

The "Ready to Assign" (RTA) value is computed as:

```
RTA = income_thisMonth + leftOverFromLastMonth − totalAssignedThisMonth
```

This formula ignores account balances entirely. A user with ฿52,480.61 sitting in a "Make" account but no `MonthlyIncome` row for the current month sees `RTA = 0 − 5,000 = −฿5,000` the moment they assign anything, even though they have plenty of money. They are told the budget is over-assigned, the UI surfaces "Pull money back to rebalance", and the mental model breaks.

This bug was reproduced in production on 2026-05-26 (see `git log` around commit 528d99e for related fixes).

## Goals

- Account balances flow into RTA automatically. Money in an account = money you can budget.
- Drop the manual "Set Income" UX entirely. Recording income is just "+ Transaction" with no category (an inflow).
- Match the YNAB accounting identity: `RTA = sum(accounts) − sum(envelopes)`. This is mathematically equivalent to YNAB's `total_inflows_alltime − total_assigned_alltime`.
- Close a related loophole: hidden categories are currently excluded from the `totalAvailable` sum, which would inflate the new RTA. Fix by summing across **all** categories regardless of `IsHidden`.

## Non-goals

- Reworking transfers between accounts. Current model already handles these as paired transactions; the new formula is self-balancing for them.
- Special-casing credit / loan accounts. Their `Balance` continues to flow into the sum verbatim.
- Future-dated transactions. `Account.Balance` updates immediately on insert while `envelope.available` walks by date — a pre-existing inconsistency that the new formula does not introduce but also does not fix. Out of scope.
- Multi-currency. Single currency (THB) only.

## Data model change

```
Before:                              After:

Accounts.Balance ─┐                  Accounts.Balance ──┐
                  │                                     │
MonthlyIncome ────┼─ none connect    Envelopes ─────────┤
                  │                                     │
Envelopes ────────┘                                     ▼
                                            RTA = sum(accounts)
                                                − sum(envelope.available, ALL cats)
```

`MonthlyIncome` is removed entirely. Its only consumer (`GetMonthlySummaryHandler`) no longer needs it. Its only writer (`SetMonthlyIncome` use case) no longer exists.

### Why this works (YNAB identity)

```
sum(account.balance) = total_inflows_alltime − total_outflows_alltime
sum(envelope.available) = total_assigned_alltime + total_categorized_activity_alltime
                       (categorized activity is the negative outflow portion that hit envelopes)

Subtracting:
sum(accounts) − sum(envelopes)
  = total_inflows − total_outflows − total_assigned − total_categorized_activity
  = total_inflows − total_assigned + (total_categorized_activity − total_outflows)
  = total_inflows − total_assigned + total_uncategorized_outflows

If all outflows are categorized (well-kept books), uncategorized_outflows = 0, and:
  RTA_new = total_inflows − total_assigned     ← YNAB's RTA
```

The formula self-balances under every operation:

| Action                                  | accounts | envelopes | RTA   |
|-----------------------------------------|----------|-----------|-------|
| Receive paycheck (inflow, CategoryId=null) | +1000  | 0         | +1000 |
| Assign 500 to "Bills"                   | 0        | +500      | −500  |
| Spend 200 from "Bills" (categorized)    | −200     | −200      | 0     |
| Transfer between accounts (paired tx)   | 0        | 0         | 0     |

## Backend changes

### Files deleted

- `MenuNest.Domain/Entities/MonthlyIncome.cs`
- `MenuNest.Infrastructure/Persistence/Configurations/MonthlyIncomeConfiguration.cs`
- `MenuNest.Application/UseCases/Budget/Monthly/SetMonthlyIncome/` (folder)
- `MenuNest.Application.UnitTests/Budget/Monthly/SetMonthlyIncome*.cs` (any tests under it)

### Files modified

**`IApplicationDbContext` / `AppDbContext`** — remove `DbSet<MonthlyIncome> MonthlyIncomes`.

**`MenuNest.WebApi/Controllers/BudgetController.cs`** — remove the `SetMonthlyIncomeAsync` action and its route binding `PUT /api/budget/monthly/income`.

**`GetMonthlySummaryHandler.cs`** — rewrite the post-loop math:

```csharp
// Sum of account balances (all accounts, including closed)
var totalAccountBalance = await _db.BudgetAccounts
    .Where(a => a.FamilyId == familyId)
    .SumAsync(a => a.Balance, ct);

// Sum across ALL categories (hidden included) so hiding doesn't inflate RTA
decimal totalEnvelopeAvailableAllCats = 0;
foreach (var cat in categories) {
    var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id);
    var catTx          = allTx        .Where(t => t.CategoryId == cat.Id);
    decimal available = 0;
    // (same per-month walk as today, but for every cat — not just visible ones)
    ...
    totalEnvelopeAvailableAllCats += available;
}

// Income, as a per-month informational stat (kept for the "X of Y placed" UI)
var inflowsThisMonth = await _db.BudgetTransactions
    .Where(t => t.FamilyId == familyId
             && t.CategoryId == null
             && t.Amount > 0
             && t.Date >= selected && t.Date < nextMonth)
    .SumAsync(t => t.Amount, ct);

var readyToAssign = totalAccountBalance - totalEnvelopeAvailableAllCats;
```

Note: the visible-only sums (`totalAssignedThisMonth`, `totalActivityThisMonth`, the per-group totals, the per-envelope DTOs) remain unchanged — they drive the envelope list and are correctly scoped to visible items.

### DTO field changes

**`MonthlySummaryDto`** field disposition:

| Field                   | Before                                | After                                           |
|-------------------------|---------------------------------------|-------------------------------------------------|
| `income`                | `MonthlyIncomes` row for the month    | Sum of positive uncategorized transactions this month |
| `leftOverFromLastMonth` | derived from envelope sums            | **Removed** — no consumer                       |
| `readyToAssign`         | `income + leftOver − assignedThis`    | `sum(accounts) − sum(envelopes_all_cats)`       |
| `totalAvailable`        | visible cats only                     | unchanged — visible cats only                   |

### EF Core migration

`AddMigration DropMonthlyIncome` → `DROP TABLE MonthlyIncomes`. The personal dev DB has zero rows, so no data loss. Migration runs at startup on next deploy.

## Frontend changes

### Files deleted

- `frontend/src/pages/budget/components/SetIncomeDialog.tsx`

### Files modified

**`frontend/src/shared/api/api.ts`**
- Remove `setMonthlyIncome` mutation builder and `useSetMonthlyIncomeMutation` export.
- Remove `leftOverFromLastMonth` from the `MonthlySummaryDto` TS interface.

**`frontend/src/pages/budget/BudgetPage.tsx`**
- Remove the `SetIncomeDialog` import, its open-state, and its render.
- Remove the `currentAmount={summary.income}` prop that fed the dialog.

**`frontend/src/pages/budget/components/RtaHero.tsx`**
- The hero element changes from `<button>` to a passive `<div>` / `<section>` — RTA is now a read-only status panel.
- Text bindings (`summary.income`, `summary.totalAssigned`) still work; they now show derived inflow stats rather than user-typed income.

**MSW handlers** (if applicable in `frontend/src/test/`) — remove the `PUT /api/budget/monthly/income` stub.

## Testing strategy

### Backend

- Delete the `SetMonthlyIncome*Tests` files.
- Rewrite `GetMonthlySummaryHandlerTests`:
  - **New formula correctness**: seed 1 account with balance 1000, 1 category with assignment 300. Assert `readyToAssign == 700`.
  - **Hidden category regression**: seed account 1000, two cats with assignment 300 each, one hidden. Assert `readyToAssign == 400` (hidden cat IS counted).
  - **Inflow-via-transaction**: seed account opening 0, then insert a transaction (CategoryId=null, Amount=+500). Assert account.Balance == 500 and readyToAssign == 500.
  - **`income` DTO field**: seed 2 positive uncategorized tx (+200, +300) this month plus 1 negative one (−50). Assert `summary.income == 500` (only positive inflows summed).
- Keep `CreateAccount` / `CreateTransaction` tests unchanged.

### Frontend

- Remove any `SetIncomeDialog` test files.
- Update `RtaHero` tests: drop assertions that tapping opens a dialog.
- MSW handler test for income endpoint: remove.

### Manual smoke test

1. Open `/budget` on the May 2026 view of the existing personal account.
2. Confirm "Make" account balance ฿52,480.61 is reflected in the new RTA (≈฿47,480 after the existing ฿5,000 AIS assignment).
3. Add a `+Transaction` to Make with amount +5,000 and no category. RTA should jump by +5,000.
4. Add an envelope assignment of 1,000 to AIS. RTA should drop by −1,000.

## Risks / out of scope

- **Data loss on migration**: `MonthlyIncomes` rows are dropped. Acceptable for this app (single-user dev family with no rows).
- **API consumers**: anything calling `PUT /api/budget/monthly/income` will receive 404 after deploy. No known client outside this repo's SPA.
- **Future-dated transactions** (pre-existing): out of scope.
- **Closed accounts**: their `Balance` continues to count toward RTA. If users want to "park" old accounts without affecting RTA, they should zero out the balance first. Acceptable for now.
- **Spec is a breaking change**, but for a single-user app under active development. No deprecation period needed.

## Implementation note (deferred to plan)

The detailed step ordering, commit boundaries, and rollback strategy belong in the implementation plan (next step via `writing-plans`). Sequence likely looks like:

1. Rewrite handler + DTO + tests (server still has table, but no longer reads it)
2. Frontend cleanup (dialog, mutation, hero behavior, tests)
3. Drop endpoint + use case + entity + migration
4. Deploy and smoke-test on production data
