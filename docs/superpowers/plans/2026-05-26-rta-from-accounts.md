# RTA from Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broken `MonthlyIncome`-based RTA computation with `RTA = sum(accounts) − sum(envelope.available across ALL categories)`, removing all related UI, API, entities, and tests.

**Architecture:** Single-pass refactor in four phases — (1) backend handler/DTO/tests rewrite while keeping the table; (2) frontend cleanup so nothing calls the dying endpoint; (3) backend deletion of entity + use case + endpoint; (4) EF migration to drop the table. Each phase commits independently and leaves a green build.

**Tech Stack:** .NET 10 + EF Core 10 + Mediator + xUnit/FluentAssertions on the backend; React 19 + RTK Query on the frontend.

**Spec:** [docs/superpowers/specs/2026-05-26-rta-from-accounts-design.md](../specs/2026-05-26-rta-from-accounts-design.md)

---

## File Map

**Backend — modify:**
- `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs` — drop `LeftOverFromLastMonth` from `MonthlySummaryDto`
- `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs` — new RTA formula, sum all cats, derive Income from inflows
- `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs` — remove `SetMonthlyIncomeAsync` action
- `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` — remove `DbSet<MonthlyIncome> MonthlyIncomes`
- `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` — same
- `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/GetMonthlySummaryHandlerTests.cs` — rewrite assertions
- `frontend/src/shared/api/api.ts` — drop `setMonthlyIncome` mutation, `leftOverFromLastMonth` field
- `frontend/src/pages/budget/BudgetPage.tsx` — remove dialog state and render
- `frontend/src/pages/budget/components/RtaHero.tsx` — `<button>` → `<div>`, drop `onClick` prop

**Backend — delete:**
- `backend/src/MenuNest.Domain/Entities/MonthlyIncome.cs`
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/MonthlyIncomeConfiguration.cs`
- `backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetMonthlyIncome/` (folder)
- `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetMonthlyIncomeHandlerTests.cs`

**Frontend — delete:**
- `frontend/src/pages/budget/components/SetIncomeDialog.tsx`

**Backend — create:**
- `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_DropMonthlyIncome.cs` (EF-generated)

---

## Phase 1 — Backend handler + DTO

### Task 1: Rewrite `GetMonthlySummaryHandlerTests` for the new model

**Files:**
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/GetMonthlySummaryHandlerTests.cs`

The existing file references `fx.Db.MonthlyIncomes` and asserts `LeftOverFromLastMonth`. Replace those — seed accounts instead and assert the new identity. Keep all tests that test envelope walking / target progress / cross-family isolation; only adjust the income-related ones.

- [ ] **Step 1.1: Replace the `Empty_family_returns_zero_summary` test**

Remove the `LeftOverFromLastMonth` assertion (the field is going away). Keep everything else.

```csharp
[Fact]
public async Task Empty_family_returns_zero_summary()
{
    using var fx = new HandlerTestFixture();

    var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

    var result = await sut.Handle(
        new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

    result.Year.Should().Be(2026);
    result.Month.Should().Be(4);
    result.Income.Should().Be(0m);
    result.TotalAssigned.Should().Be(0m);
    result.TotalActivity.Should().Be(0m);
    result.Available.Should().Be(0m);
    result.ReadyToAssign.Should().Be(0m);
    result.Groups.Should().BeEmpty();
    result.Accounts.Should().BeEmpty();
}
```

- [ ] **Step 1.2: Replace the `Single_category_assigned_no_spending_fills_envelope` test**

Drop the `LeftOverFromLastMonth.Should().Be(0m)` line. Everything else stays.

```csharp
result.TotalAssigned.Should().Be(500m);
result.TotalActivity.Should().Be(0m);
result.Available.Should().Be(500m);
```

- [ ] **Step 1.3: Replace the `Rollover_from_prior_month_carries_available_forward` test**

The current test ends with:
```csharp
result.LeftOverFromLastMonth.Should().Be(500m);
```
Delete that line. The envelope assertions above it stay (they test the per-envelope walking, which doesn't change).

- [ ] **Step 1.4: Replace `Income_and_assignments_produce_ready_to_assign` with the new identity**

The test currently seeds `MonthlyIncomes`. Replace it with a test that seeds an *account balance* and an assignment, then asserts `ReadyToAssign = balance − assigned`.

```csharp
/// <summary>
/// RTA = sum(accounts) − sum(envelope.available across all cats).
/// 1000 in an account + 500 assigned to a single category produces
/// envelope.available 500 → ReadyToAssign 1000 − 500 = 500.
/// </summary>
[Fact]
public async Task Account_balance_minus_envelope_available_produces_ready_to_assign()
{
    using var fx = new HandlerTestFixture();

    var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
    fx.Db.BudgetCategoryGroups.Add(group);
    var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
    fx.Db.BudgetCategories.Add(cat);

    fx.Db.BudgetAccounts.Add(BudgetAccount.Create(
        fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0));
    fx.Db.MonthlyAssignments.Add(
        MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
    await fx.Db.SaveChangesAsync();

    var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

    var result = await sut.Handle(
        new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

    result.TotalAssigned.Should().Be(500m);
    result.ReadyToAssign.Should().Be(500m);
}
```

- [ ] **Step 1.5: Add `Hidden_category_still_counts_toward_RTA` regression test**

This is the new invariant that prevents the "hide cat → inflate RTA" loophole. Hidden categories must not appear in `groups[].categories` but their available **must** be subtracted from RTA.

```csharp
/// <summary>
/// Hidden categories are hidden from the response, but their
/// envelope.available is still subtracted from RTA. Without this,
/// hiding a funded category would silently inflate the RTA.
/// </summary>
[Fact]
public async Task Hidden_category_is_subtracted_from_rta_even_though_hidden_from_response()
{
    using var fx = new HandlerTestFixture();

    var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
    fx.Db.BudgetCategoryGroups.Add(group);
    var visible = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
    var hidden  = BudgetCategory.Create(fx.Family.Id, group.Id, "Old Gym", null, 1);
    hidden.Hide();
    fx.Db.BudgetCategories.AddRange(visible, hidden);

    fx.Db.BudgetAccounts.Add(BudgetAccount.Create(
        fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0));
    fx.Db.MonthlyAssignments.AddRange(
        MonthlyAssignment.Create(fx.Family.Id, visible.Id, 2026, 4, 300m),
        MonthlyAssignment.Create(fx.Family.Id, hidden.Id,  2026, 4, 300m));
    await fx.Db.SaveChangesAsync();

    var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

    var result = await sut.Handle(
        new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

    // Response excludes the hidden cat (existing behavior).
    result.Groups.Single().Categories.Should().HaveCount(1);
    result.TotalAssigned.Should().Be(300m, "visible-only sum drives the UI total");

    // But hidden cat IS subtracted from RTA.
    result.ReadyToAssign.Should().Be(400m, "1000 − (300 visible + 300 hidden) = 400");
}
```

- [ ] **Step 1.6: Add `Income_field_is_sum_of_positive_inflows_this_month` test**

`Income` now means "sum of positive inflow transactions this month". Seed two positive inflows and one negative uncategorized outflow; assert only the positive ones contribute.

```csharp
[Fact]
public async Task Income_field_is_sum_of_positive_inflows_this_month()
{
    using var fx = new HandlerTestFixture();

    var account = BudgetAccount.Create(
        fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
    fx.Db.BudgetAccounts.Add(account);

    fx.Db.BudgetTransactions.AddRange(
        BudgetTransaction.Create(fx.Family.Id, account.Id, null,  200m,
            new DateOnly(2026, 4, 1), null, fx.User.Id),
        BudgetTransaction.Create(fx.Family.Id, account.Id, null,  300m,
            new DateOnly(2026, 4, 15), null, fx.User.Id),
        BudgetTransaction.Create(fx.Family.Id, account.Id, null, -50m,
            new DateOnly(2026, 4, 20), null, fx.User.Id));
    await fx.Db.SaveChangesAsync();

    var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

    var result = await sut.Handle(
        new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

    result.Income.Should().Be(500m, "only positive uncategorized inflows count toward Income");
}
```

- [ ] **Step 1.7: Replace `Cross_family_data_is_isolated` seeds**

Strip the `fx.Db.MonthlyIncomes.Add(...)` lines. The test still proves isolation via accounts and assignments. Replace the income assertion to look at the new derived `Income`:

```csharp
// Old line — remove:
//   fx.Db.MonthlyIncomes.Add(MonthlyIncome.Create(fx.Family.Id, 2026, 4, 500m));
//   fx.Db.MonthlyIncomes.Add(MonthlyIncome.Create(other.Id,     2026, 4, 9999m));
// New: seed one positive inflow per family
fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
    fx.Family.Id, /* account id from My Checking */ ..., null, 500m,
    new DateOnly(2026, 4, 1), null, fx.User.Id));
fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
    other.Id, /* account id from Foreign Checking */ ..., null, 9999m,
    new DateOnly(2026, 4, 1), null, fx.User.Id));
```

You'll need to capture the My Checking / Foreign Checking accounts in variables to get their IDs. The final assertions stay the same: `result.Income.Should().Be(500m)`.

- [ ] **Step 1.8: Run tests, verify they fail**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetMonthlySummary"
```
Expected: most tests now fail because the handler still uses the old formula. Specifically expect failures on `Account_balance_minus_envelope_available_produces_ready_to_assign`, `Hidden_category_is_subtracted_from_rta_even_though_hidden_from_response`, `Income_field_is_sum_of_positive_inflows_this_month`. Existing rollover/target/group tests should still pass (they don't depend on income or the new formula).

This failure confirms the tests are exercising the right behavior — proceed to the implementation step.

- [ ] **Step 1.9: Commit (red commit — TDD)**

```bash
git add backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/GetMonthlySummaryHandlerTests.cs
git commit -m "test(budget): assert new RTA = sum(accounts) − sum(envelopes) identity"
```

### Task 2: Update `MonthlySummaryDto`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs`

- [ ] **Step 2.1: Remove `LeftOverFromLastMonth` from `MonthlySummaryDto`**

Find the record definition and drop the `LeftOverFromLastMonth` property. Update the positional parameter list accordingly. After the change, the handler's `return new MonthlySummaryDto(...)` call must be updated too — that happens in Task 3.

- [ ] **Step 2.2: Run a build to confirm the only compile error is in the handler**

```bash
cd backend
dotnet build src/MenuNest.Application/MenuNest.Application.csproj
```
Expected: build fails with one error in `GetMonthlySummaryHandler.cs` because the constructor argument list no longer matches. This is the proper TDD red.

### Task 3: Rewrite `GetMonthlySummaryHandler`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs`

- [ ] **Step 3.1: Extract `totalEnvelopeAvailableAllCats`**

Currently the handler only sums visible cats into `totalAvailable`. Add a second pass (or extend the existing loop) over ALL categories:

```csharp
// AFTER the existing visible-only loop completes, walk every category
// (regardless of hidden) just to accumulate the all-cat available.
decimal totalEnvelopeAvailableAllCats = 0;
foreach (var cat in categories)
{
    var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id).ToList();
    var catTx          = allTx.Where(t => t.CategoryId == cat.Id).ToList();
    decimal available = 0;
    for (int y = 2000; y <= q.Year; y++)
    {
        int mStart = 1, mEnd = 12;
        if (y == q.Year) mEnd = q.Month;
        for (int m = mStart; m <= mEnd; m++)
        {
            var a   = catAssignments.FirstOrDefault(r => r.Year == y && r.Month == m)?.AssignedAmount ?? 0m;
            var act = catTx.Where(t => t.Date.Year == y && t.Date.Month == m).Sum(t => t.Amount);
            available += a + act;
        }
    }
    totalEnvelopeAvailableAllCats += available;
}
```

- [ ] **Step 3.2: Compute `totalAccountBalance`**

```csharp
var totalAccountBalance = await _db.BudgetAccounts
    .Where(a => a.FamilyId == familyId)
    .SumAsync(a => (decimal?)a.Balance, ct) ?? 0m;
```

(Defensive null coalesce keeps the in-memory provider happy when there are zero accounts.)

- [ ] **Step 3.3: Derive `income` from inflows**

Replace the existing `MonthlyIncomes` query with a positive-inflow sum:

```csharp
var income = await _db.BudgetTransactions
    .Where(t => t.FamilyId == familyId
             && t.CategoryId == null
             && t.Amount > 0m
             && t.Date >= selected && t.Date < nextMonth)
    .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
```

- [ ] **Step 3.4: Compute new RTA, drop `leftOverFromLastMonth`**

```csharp
decimal readyToAssign = totalAccountBalance - totalEnvelopeAvailableAllCats;
```

Remove the `leftOverFromLastMonth` local entirely.

- [ ] **Step 3.5: Update the `return new MonthlySummaryDto(...)` call**

Remove the `leftOverFromLastMonth` positional argument. The full return shape becomes:

```csharp
return new MonthlySummaryDto(
    q.Year, q.Month,
    income, totalAssignedThisMonth, totalActivityThisMonth,
    readyToAssign, totalAvailable,
    groupsDto, accounts);
```

(`totalAvailable` from the visible-only loop is unchanged — it still drives the UI.)

- [ ] **Step 3.6: Run tests, verify all pass**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj --filter "FullyQualifiedName~GetMonthlySummary"
```
Expected: all `GetMonthlySummary*` tests PASS.

- [ ] **Step 3.7: Run the full backend test suite**

```bash
cd backend
dotnet test
```
Expected: PASS. The only remaining-but-now-stale test file is `SetMonthlyIncomeHandlerTests.cs` — it still tests the (still-present-but-unused) `SetMonthlyIncomeHandler`. Phase 3 deletes both together.

- [ ] **Step 3.8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs
git commit -m "feat(budget): derive RTA from sum(accounts) − sum(envelope.available)"
```

---

## Phase 2 — Frontend cleanup

### Task 4: Update the TS interface

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 4.1: Remove `leftOverFromLastMonth` from `MonthlySummaryDto`**

Find the interface definition (around line 368) and drop the line:
```ts
leftOverFromLastMonth: number
```

- [ ] **Step 4.2: Run typecheck**

```bash
cd frontend
npx tsc -b --noEmit
```
Expected: passes. The field had no FE consumer.

### Task 5: Convert `RtaHero` to a passive panel

**Files:**
- Modify: `frontend/src/pages/budget/components/RtaHero.tsx`

- [ ] **Step 5.1: Remove the `onClick` prop and switch from `<button>` to a `<section>`**

Replace the entire component body:

```tsx
export function RtaHero({summary}: {summary: MonthlySummaryDto}) {
  const rta = summary.readyToAssign
  const state: 'has-money' | 'zero' | 'over' =
    rta > 0 ? 'has-money' : rta === 0 ? 'zero' : 'over'

  const stateLabel =
    state === 'zero' ? 'Every baht has a job' :
    state === 'over' ? 'Too much assigned' :
    'Still to place'

  const contextLine =
    state === 'zero'
      ? `${formatTHB(summary.income)} fully placed.`
      : state === 'over'
      ? `Pull ${formatTHB(Math.abs(rta))} back to rebalance.`
      : `${formatTHB(summary.totalAssigned)} of ${formatTHB(summary.income)} placed.`

  const pctRaw = summary.income <= 0 ? 0 : (summary.totalAssigned / summary.income) * 100
  const pctClamped = state === 'over' ? 100 : Math.min(100, Math.max(0, pctRaw))
  const pctLabel = Math.round(pctClamped)

  return (
    <section className={`bdg-rta-hero is-${state}`} data-testid="bdg-rta-hero">
      <div className="bdg-rta-topline">
        <span className="bdg-rta-month">{MONTHS[summary.month - 1]} {summary.year}</span>
        <span className="bdg-rta-state-pill">{stateLabel}</span>
      </div>

      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(rta)}
      </div>

      <div className="bdg-rta-context">{contextLine}</div>

      <div
        className="bdg-rta-progress"
        data-testid="bdg-rta-progress"
        aria-label={`${pctLabel} percent of income placed`}
      >
        <div className="bdg-rta-progress-track">
          <div className="bdg-rta-progress-fill" style={{width: `${pctClamped}%`}} />
        </div>
        <span className="bdg-rta-progress-pct">{pctLabel}%</span>
      </div>
    </section>
  )
}
```

Also update the JSDoc block above the function — remove the "Tapping opens SetIncomeDialog via onClick" sentence and replace with "Display-only; the assign / spend flows live elsewhere."

- [ ] **Step 5.2: Update CSS if `<button>`-specific selectors exist**

Scan `frontend/src/pages/budget/BudgetPage.css` for `button.bdg-rta-hero` selectors. If any exist, change them to `.bdg-rta-hero`. (The current CSS uses `.bdg-rta-hero` directly, so this is likely a no-op — verify.)

- [ ] **Step 5.3: Run frontend build**

```bash
cd frontend
npm run build
```
Expected: PASS.

### Task 6: Delete `SetIncomeDialog` and clean up `BudgetPage`

**Files:**
- Delete: `frontend/src/pages/budget/components/SetIncomeDialog.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`

- [ ] **Step 6.1: Delete the dialog file**

```bash
rm frontend/src/pages/budget/components/SetIncomeDialog.tsx
```

- [ ] **Step 6.2: Remove `SetIncomeDialog` references from `BudgetPage.tsx`**

Drop these lines:
```tsx
import {SetIncomeDialog} from './components/SetIncomeDialog'
const [incomeOpen, setIncomeOpen] = useState(false)
```

Change the `<RtaHero ...>` line:
```tsx
// before:
<RtaHero summary={summary} onClick={() => setIncomeOpen(true)} />
// after:
<RtaHero summary={summary} />
```

Delete the conditional dialog render:
```tsx
// remove entirely:
{incomeOpen && (
  <SetIncomeDialog
    currentAmount={summary.income}
    onClose={() => setIncomeOpen(false)}
  />
)}
```

If the `useState` import is no longer used after these removals, drop it too.

- [ ] **Step 6.3: Run typecheck + build**

```bash
cd frontend
npx tsc -b --noEmit
npm run build
```
Expected: PASS.

### Task 7: Remove `setMonthlyIncome` mutation from `api.ts`

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 7.1: Remove the mutation builder**

Find this block (around the budget endpoints, after `coverOverspending`):
```ts
setMonthlyIncome: build.mutation<void, {year: number; month: number; amount: number}>({
    query: (b) => ({url: '/api/budget/monthly/income', method: 'PUT', body: b}),
    invalidatesTags: (_r, _e, a) => [{type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
}),
```
Delete it.

- [ ] **Step 7.2: Remove the hook export**

In the exported `{ ... } = api` block near the bottom, remove:
```ts
useSetMonthlyIncomeMutation,
```

- [ ] **Step 7.3: Run typecheck**

```bash
cd frontend
npx tsc -b --noEmit
```
Expected: PASS. If any test or file other than `SetIncomeDialog` still imported the hook, the build catches it here.

- [ ] **Step 7.4: Commit Phase 2**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/budget/BudgetPage.tsx \
        frontend/src/pages/budget/components/RtaHero.tsx \
        frontend/src/pages/budget/components/SetIncomeDialog.tsx
git commit -m "feat(budget): drop SetIncomeDialog, RtaHero becomes a display-only panel"
```

---

## Phase 3 — Backend cleanup

### Task 8: Remove `SetMonthlyIncome` use case + tests

**Files:**
- Delete: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetMonthlyIncome/` (whole folder)
- Delete: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetMonthlyIncomeHandlerTests.cs`

- [ ] **Step 8.1: Delete the folders**

```bash
rm -rf backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetMonthlyIncome
rm backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetMonthlyIncomeHandlerTests.cs
```

- [ ] **Step 8.2: Run the backend test suite**

```bash
cd backend
dotnet test
```
Expected: compile errors will surface in `BudgetController` (it still references `SetMonthlyIncomeCommand`). The next task fixes that.

### Task 9: Remove the income endpoint from `BudgetController`

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`

- [ ] **Step 9.1: Open the controller and locate the action**

Find:
```csharp
[HttpPut("monthly/income")]
public async ValueTask<IActionResult> SetMonthlyIncomeAsync(...)
{
    ...
}
```

- [ ] **Step 9.2: Delete the action and remove its `using` if unused**

Remove the action body and the request DTO/record if one is defined inline. If you removed the last consumer of `MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome`, also drop that `using`.

- [ ] **Step 9.3: Run the backend test suite**

```bash
cd backend
dotnet test
```
Expected: PASS. The controller no longer references the deleted use case.

### Task 10: Remove the `MonthlyIncome` entity, config, and DbSet

**Files:**
- Delete: `backend/src/MenuNest.Domain/Entities/MonthlyIncome.cs`
- Delete: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/MonthlyIncomeConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` — drop `DbSet<MonthlyIncome> MonthlyIncomes { get; }`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` — drop the `DbSet` property + any `OnModelCreating` references / direct `modelBuilder.Entity<MonthlyIncome>()` calls

- [ ] **Step 10.1: Delete the entity file**

```bash
rm backend/src/MenuNest.Domain/Entities/MonthlyIncome.cs
```

- [ ] **Step 10.2: Delete the configuration file**

```bash
rm backend/src/MenuNest.Infrastructure/Persistence/Configurations/MonthlyIncomeConfiguration.cs
```

- [ ] **Step 10.3: Update `IApplicationDbContext`**

Remove this line:
```csharp
DbSet<MonthlyIncome> MonthlyIncomes { get; }
```

- [ ] **Step 10.4: Update `AppDbContext`**

Remove the `DbSet<MonthlyIncome>` property and any `using MenuNest.Domain.Entities` that becomes unused. If `OnModelCreating` registers the configuration explicitly (`new MonthlyIncomeConfiguration()`), remove that line.

- [ ] **Step 10.5: Build + test**

```bash
cd backend
dotnet build
dotnet test
```
Expected: PASS.

- [ ] **Step 10.6: Commit Phase 3**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetMonthlyIncome \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetMonthlyIncomeHandlerTests.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/src/MenuNest.Domain/Entities/MonthlyIncome.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/MonthlyIncomeConfiguration.cs \
        backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs \
        backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs
git commit -m "refactor(budget): drop SetMonthlyIncome use case, endpoint, and entity"
```

---

## Phase 4 — EF migration + smoke test

### Task 11: Generate the `DropMonthlyIncome` migration

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_DropMonthlyIncome.cs`

- [ ] **Step 11.1: Run the EF tool**

```bash
cd backend
dotnet ef migrations add DropMonthlyIncome \
  --project src/MenuNest.Infrastructure \
  --startup-project src/MenuNest.WebApi
```

Expected: EF generates a migration whose `Up` method calls `migrationBuilder.DropTable(name: "MonthlyIncomes")` and whose `Down` recreates the table schema.

- [ ] **Step 11.2: Open the generated file and sanity-check**

Verify:
- `Up` drops the table.
- `Down` recreates `MonthlyIncomes` with `FamilyId`, `Year`, `Month`, `Amount`, `Id`, audit columns (matching what was there).

- [ ] **Step 11.3: Apply the migration locally if a dev DB exists**

If you have a local SQL Server with the dev family in it:
```bash
dotnet ef database update --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Otherwise skip — production DB picks up the migration on next deploy via the existing migration runner at startup (search `Migrate` in `Program.cs` to confirm).

- [ ] **Step 11.4: Run the full test suite**

```bash
cd backend
dotnet test
```
Expected: PASS.

- [ ] **Step 11.5: Commit + push**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(budget): EF migration DropMonthlyIncome"
git push main main
```

### Task 12: Production smoke test

- [ ] **Step 12.1: Wait for CI to deploy**

The push triggers both the SPA and backend deploy workflows. Allow ~5 minutes.

- [ ] **Step 12.2: Open `/budget` in the deployed app on May 2026**

Confirm:
- RTA is positive (≈฿47,480 = ฿52,480.61 account balance − ฿5,000 AIS assignment).
- "Too much assigned" banner is gone.
- Tapping the hero does nothing (it's no longer interactive).

- [ ] **Step 12.3: Record an inflow transaction**

Tap "+ Transaction" on the Make account → amount +1000, no category → save. Confirm:
- Make balance becomes ฿53,480.61.
- RTA becomes ≈฿48,480.

- [ ] **Step 12.4: Assign 1000 to AIS**

Tap the AIS envelope → set Assigned to 6000 → save. Confirm:
- AIS Available shows ฿6,000.
- RTA drops by 1000 (≈฿47,480).

- [ ] **Step 12.5: Mark plan complete**

If all smoke checks pass, the feature is live.

---

## Self-review notes (resolved before this plan was published)

- **Spec coverage**: every section of the design spec has at least one task. The "Income field" derivation (spec §"DTO field changes") is covered in Step 3.3 + tested in Step 1.6.
- **Hidden-cat regression**: Task 1.5 captures the new invariant explicitly.
- **Migration**: Task 11 generates a single migration; `Down` is preserved for emergency rollback even though we don't expect to need it.
- **Type consistency**: `MonthlySummaryDto`'s positional argument order in Step 3.5 matches the field disposition table in the spec; `LeftOverFromLastMonth` removed everywhere; `Income` semantic restated in both spec and Task 1.6 / Step 3.3.
