# Budget `mvp` Milestone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the phone, set what each **Account** holds and see "today you can spend X" — with every movement of money written as a **Budget transaction**, and every past month's **Ready to Assign** finally correct.

**Architecture:** `BudgetAccount.Balance` is demoted from source-of-truth to a cache; `GetMonthlySummary` derives each account's balance by summing its **Budget transactions** up to the end of the month being viewed, putting accounts and envelopes on one clock. A new `DailyAllowance` row per **Family** stores a frozen figure, the everyday pot it was computed from, and the date — re-frozen only by a **Budgeting event**, never by spending. The **Pace line** compares `figure × completed days` against `frozen pot − current pot`.

**Tech Stack:** .NET 9 / EF Core (SQL Server), Mediator, FluentValidation, xUnit + **Moq** + FluentAssertions; React + Redux Toolkit SPA; Playwright e2e; ModelContextProtocol.AspNetCore 1.0.0.

**Spec:** `docs/superpowers/specs/2026-08-22-budget-mvp-milestone-design.md`
**Decisions:** menunest-181, -182, -183, -184, -185, -186, -187, -188
**Approved screen:** Claude Design → `MenuNest design system` → `screens/budget-shell.html` (project `107862ef-c14b-42f4-a8f2-4bbe36951e25`)

## Global Constraints

- **Every commit must reference the ticket.** Use `(#99)` in the subject or `Refs #99` in the body. Not `closes` — the milestone is one of several on issue #99.
- **Stage explicit paths only.** Never `git add -A` or `git add .`. `daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) must never be swept into a commit.
- **Every commit must leave the ENTIRE suite green — and nothing enforces this for you.** CLAUDE.md describes `frontend/.husky/pre-commit` (`set -e`; backend `dotnet build` + `dotnet test` Release, frontend `tsc --noEmit` + `npm run build`) as running on every commit. **Verified 2026-08-22: it does not run in this working copy** — `core.hooksPath` is unset, `.git/hooks/pre-commit` does not exist, and the husky script is not executable. So the gate is *yours*: before every `git commit` in this plan, run

  ```bash
  cd backend && dotnet build && dotnet test
  cd ../frontend && npx tsc --noEmit && npm run build
  ```

  and do not commit if anything is red. A task's own filtered tests passing is **not** enough — the whole suite must be green, which is why an EF entity and its mapping must land together.
- **A new `DbSet<>` must be added to all three `IApplicationDbContext` implementers** — `AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext` — or the build fails `CS0535`. The entity, its EF configuration and all three DbSets must land in the **same commit**: an unmapped entity fails EF model validation for *every* test touching the context.
- **Mocking is Moq, not NSubstitute.** `var m = new Mock<IUserProvisioner>();` — `Substitute.For<>` will not compile.
- **Migrations are applied by hand.** Neither `Program.cs` nor `.github/workflows/main_menunest.yml` runs `dotnet ef database update`.
- **Prod deploys on push to `main`.** Any UI change must be verified interactively before push.
- **There is no component/visual test harness.** vitest runs in `environment: 'node'` with no jsdom. `tsc` + `build` + vitest **cannot** catch a rendering, layout or CSS bug — and **neither can SDD's per-task review**. Every UI task below therefore ends with an explicit mock-diff and interactive check.
- **Money columns are `decimal(18,4)`.**
- **Currency is THB**, formatted by the existing `formatTHB` helper in `frontend/src/pages/budget/BudgetPage.hooks.ts`.

## File Structure

**Domain** (`backend/src/MenuNest.Domain/Entities/`)
- `DailyAllowance.cs` — **new**. Owns the freeze arithmetic and the completed-day count. All allowance maths lives here so it is unit-testable without a database.
- `BudgetCategory.cs` — gains `IsEveryday` + `MarkEveryday`.
- `BudgetAccount.cs` — loses `SetBalance`.

**Application** (`backend/src/MenuNest.Application/UseCases/Budget/`)
- `Accounts/CorrectBalance/` — **new** use case (command, validator, handler).
- `Allowance/AllowanceFreezer.cs` — **new**. One shared service both the summary query and every **Budgeting event** call, so the freeze rule exists once.
- `Categories/SetEverydayMarks/` — **new** bulk-mark use case.
- `Monthly/GetMonthlySummary/` — derives account balances; emits `dailyAllowance`.

**Infrastructure** (`backend/src/MenuNest.Infrastructure/Persistence/`)
- `Configurations/DailyAllowanceConfiguration.cs` — **new**.
- `Migrations/` — one migration: destructive wipe + new column + new table.

**MCP** (`backend/src/MenuNest.McpServer/Tools/BudgetTools.cs`)

**Frontend** (`frontend/src/pages/budget/`)
- `components/DailyAllowanceCard.tsx`, `components/EverydayMarksSheet.tsx` — **new**.
- `components/EnvelopeCard.tsx`, `components/AccountsStrip.tsx`, `budgetSlice.ts`, `BudgetPage.tsx`, `BudgetPage.css` — modified.

Files that change together live together: the allowance card and its sheet sit beside the components they replace in the render order, not in a new folder.

---

### Task 1: Model — `DailyAllowance`, the everyday mark, and the migration

This task is deliberately large because CLAUDE.md forbids splitting it: an entity without its EF configuration and all three DbSets fails EF model validation for every test that touches the context, so a partial commit can never pass the pre-commit hook.

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/DailyAllowance.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/DailyAllowanceConfiguration.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/BudgetCategory.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetCategoryConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs:31`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs:38`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddDailyAllowanceAndEverydayMark.cs` (generated)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Allowance/DailyAllowanceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `DailyAllowance.Freeze(Guid familyId, decimal pot, DateOnly on) → DailyAllowance`
  - `DailyAllowance.Refreeze(decimal pot, DateOnly on) → void`
  - `DailyAllowance.CompletedDays(DateOnly today) → int`
  - `DailyAllowance.PaceDelta(decimal currentPot, DateOnly today) → decimal` — **the single home of the Pace line formula**; Task 4 calls it and must not restate it
  - `DailyAllowance.IsForMonth(int year, int month) → bool`
  - properties `FamilyId`, `Amount`, `FrozenPot`, `FrozenOn`, `ForYear`, `ForMonth`
  - `BudgetCategory.IsEveryday` (bool), `BudgetCategory.MarkEveryday(bool)`
  - `IApplicationDbContext.DailyAllowances`

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Allowance/DailyAllowanceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Allowance;

public class DailyAllowanceTests
{
    // menunest-181's own worked example: 6,000 over the 11 days remaining on 21 August.
    [Fact]
    public void Freeze_divides_pot_by_days_remaining_inclusive_of_today()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.Amount.Should().BeApproximately(545.4545m, 0.0001m);
        a.FrozenPot.Should().Be(6000m);
        a.ForYear.Should().Be(2026);
        a.ForMonth.Should().Be(8);
    }

    [Fact]
    public void Freeze_on_the_last_day_of_the_month_divides_by_one()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 900m, new DateOnly(2026, 8, 31));

        a.Amount.Should().Be(900m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2500)]
    public void Freeze_floors_at_zero_when_the_pot_is_empty_or_negative(decimal pot)
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), pot, new DateOnly(2026, 8, 21));

        a.Amount.Should().Be(0m);
        a.FrozenPot.Should().Be(pot); // the pot itself is recorded honestly
    }

    [Fact]
    public void CompletedDays_is_zero_on_the_freeze_day_itself()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 21)).Should().Be(0);
    }

    [Fact]
    public void CompletedDays_counts_whole_days_since_the_freeze()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 25)).Should().Be(4);
    }

    [Fact]
    public void CompletedDays_never_goes_negative_for_a_date_before_the_freeze()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 19)).Should().Be(0);
    }

    [Fact]
    public void Refreeze_replaces_the_figure_the_pot_and_the_month()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.Refreeze(3000m, new DateOnly(2026, 9, 1));

        a.Amount.Should().Be(100m);   // 3000 / 30 days in September
        a.FrozenPot.Should().Be(3000m);
        a.FrozenOn.Should().Be(new DateOnly(2026, 9, 1));
        a.ForMonth.Should().Be(9);
    }

    // ── PaceDelta — menunest-186 ────────────────────────────────────────────

    [Fact]
    public void PaceDelta_is_zero_on_the_freeze_day_even_after_spending()
    {
        // No day has been completed, so nothing can be behind.
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: 6000m, today: new DateOnly(2026, 8, 21)).Should().Be(0m);
    }

    [Fact]
    public void PaceDelta_does_not_double_count_a_same_day_spend_after_the_freeze()
    {
        // THE trap this design exists to avoid: summing transactions with
        // Date >= FrozenOn would count this spend AND see it already deducted
        // from the pot, because BudgetTransaction.Date is a DateOnly.
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: 5500m, today: new DateOnly(2026, 8, 21)).Should().Be(500m);
    }

    [Fact]
    public void PaceDelta_is_negative_when_less_was_spent_than_the_completed_days_allowed()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));
        // 4 completed days x 545.4545 = 2181.81 should-have; 1800 actually spent.
        a.PaceDelta(currentPot: 4200m, today: new DateOnly(2026, 8, 25))
            .Should().BeApproximately(-381.81m, 0.01m);
    }

    [Fact]
    public void PaceDelta_is_positive_when_more_was_spent_than_the_completed_days_allowed()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));
        // 4 completed days allow 2181.81; 4000 was spent.
        a.PaceDelta(currentPot: 2000m, today: new DateOnly(2026, 8, 25))
            .Should().BeApproximately(1818.18m, 0.01m);
    }

    [Fact]
    public void PaceDelta_survives_a_pot_driven_negative_by_overspending()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 1000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: -500m, today: new DateOnly(2026, 8, 22))
            .Should().BeApproximately(1500m - a.Amount, 0.01m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~DailyAllowanceTests"`
Expected: FAIL to **compile** — `DailyAllowance` does not exist.

- [ ] **Step 3: Create the `DailyAllowance` entity**

`backend/src/MenuNest.Domain/Entities/DailyAllowance.cs`:

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The frozen "you can spend this much today" figure for a family (menunest-181).
/// Exactly one row per family, overwritten at every Budgeting event (menunest-185).
/// <para>
/// <c>FrozenPot</c> is not redundant with <c>Amount</c>: the Pace line measures
/// actually-spent as <c>FrozenPot - currentPot</c>. It cannot sum transactions
/// dated on or after <c>FrozenOn</c>, because <see cref="BudgetTransaction.Date"/>
/// is a <see cref="DateOnly"/> — a spend made earlier on the freeze day carries the
/// same date and would be double-counted (menunest-186).
/// </para>
/// </summary>
public sealed class DailyAllowance : Entity
{
    public Guid FamilyId { get; private set; }

    /// <summary>The frozen figure. Floors at 0; never moved by spending.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The everyday pot as it stood at the freeze. May be negative.</summary>
    public decimal FrozenPot { get; private set; }

    public DateOnly FrozenOn { get; private set; }
    public int ForYear { get; private set; }
    public int ForMonth { get; private set; }

    private DailyAllowance() { }

    public static DailyAllowance Freeze(Guid familyId, decimal pot, DateOnly on)
    {
        var allowance = new DailyAllowance { FamilyId = familyId };
        allowance.Refreeze(pot, on);
        return allowance;
    }

    public void Refreeze(decimal pot, DateOnly on)
    {
        var daysRemaining = DateTime.DaysInMonth(on.Year, on.Month) - on.Day + 1;
        if (daysRemaining <= 0)
            throw new DomainException("Days remaining in the month must be positive.");

        Amount = Math.Max(0m, pot / daysRemaining);
        FrozenPot = pot;
        FrozenOn = on;
        ForYear = on.Year;
        ForMonth = on.Month;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Whole days finished since the freeze. Zero on the freeze day itself, so the
    /// Pace line stays silent that day (menunest-186).
    /// </summary>
    public int CompletedDays(DateOnly today) => Math.Max(0, today.DayNumber - FrozenOn.DayNumber);

    /// <summary>
    /// The Pace line figure: actually-spent minus should-have-spent. Positive is
    /// "over", negative is "under", zero renders nothing (menunest-186).
    /// <para>
    /// Actually-spent is measured pot-against-pot, never by summing transactions
    /// dated on or after <see cref="FrozenOn"/> — see the class remarks.
    /// </para>
    /// </summary>
    public decimal PaceDelta(decimal currentPot, DateOnly today)
        => (FrozenPot - currentPot) - (Amount * CompletedDays(today));

    public bool IsForMonth(int year, int month) => ForYear == year && ForMonth == month;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~DailyAllowanceTests"`
Expected: PASS, 13 tests (8 freeze/day-count + 5 PaceDelta).

- [ ] **Step 5: Add the everyday mark to `BudgetCategory`**

In `backend/src/MenuNest.Domain/Entities/BudgetCategory.cs`, add the property beside `IsHidden`:

```csharp
    public bool IsHidden { get; private set; }

    /// <summary>
    /// Marks this envelope as day-to-day spending — the only kind that feeds the
    /// Daily allowance (menunest-181). Lives on the envelope, never on its group,
    /// so it survives a move between groups.
    /// </summary>
    public bool IsEveryday { get; private set; }
```

In `Create(...)`, add `IsEveryday = false,` beside `IsHidden = false,`. Then add the mutator beside `Hide`/`Unhide`:

```csharp
    public void MarkEveryday(bool isEveryday)
    {
        IsEveryday = isEveryday;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 6: Add the EF configuration and all three DbSets**

Create `backend/src/MenuNest.Infrastructure/Persistence/Configurations/DailyAllowanceConfiguration.cs`, following `BudgetAccountConfiguration`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class DailyAllowanceConfiguration : IEntityTypeConfiguration<DailyAllowance>
{
    public void Configure(EntityTypeBuilder<DailyAllowance> b)
    {
        b.ToTable("DailyAllowances");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,4)");
        b.Property(x => x.FrozenPot).HasColumnType("decimal(18,4)");
        b.Property(x => x.FrozenOn).IsRequired();
        b.Property(x => x.ForYear).IsRequired();
        b.Property(x => x.ForMonth).IsRequired();

        // menunest-185: exactly one frozen figure per family, overwritten at each freeze.
        b.HasIndex(x => x.FamilyId).IsUnique();
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

In `BudgetCategoryConfiguration.cs`, add inside `Configure`:

```csharp
        b.Property(x => x.IsEveryday).IsRequired().HasDefaultValue(false);
```

Add the DbSet to **all three** implementers — the build fails `CS0535` if any is missed:

- `IApplicationDbContext.cs`, after line 31: `DbSet<DailyAllowance> DailyAllowances { get; }`
- `AppDbContext.cs`, beside the other budget sets: `public DbSet<DailyAllowance> DailyAllowances => Set<DailyAllowance>();`
- `SqliteAppDbContext.cs`, after line 38: `public DbSet<DailyAllowance> DailyAllowances => Set<DailyAllowance>();`
- `InMemoryAppDbContext.cs`, beside its budget sets: same line.

- [ ] **Step 7: Generate the migration**

Run:

```bash
cd backend
dotnet ef migrations add AddDailyAllowanceAndEverydayMark \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Then **edit the generated `Up(...)`** to prepend the destructive wipe (menunest-188). Order is FK-safe — children before parents:

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // menunest-188: the old budget data is no longer valid. Every Account's
        // opening money is a stored number that no BudgetTransaction explains, and
        // a surviving Envelope holding unexplained Available money would corrupt
        // Ready to Assign the same way. Wipe it; there is no back-fill.
        migrationBuilder.Sql("DELETE FROM BudgetTransactions;");
        migrationBuilder.Sql("DELETE FROM MonthlyAssignments;");
        migrationBuilder.Sql("DELETE FROM BudgetCategories;");
        migrationBuilder.Sql("DELETE FROM BudgetCategoryGroups;");
        migrationBuilder.Sql("DELETE FROM BudgetAccounts;");

        // ... then the generated AddColumn / CreateTable calls, unchanged ...
    }
```

- [ ] **Step 8: Run the full backend suite**

Run: `cd backend && dotnet build && dotnet test`
Expected: PASS. A `CS0535` here means a DbSet was missed in Step 6; an EF model-validation failure across many unrelated tests means the configuration was missed.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/DailyAllowance.cs \
        backend/src/MenuNest.Domain/Entities/BudgetCategory.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/DailyAllowanceConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetCategoryConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
        backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Allowance/DailyAllowanceTests.cs
git commit -m "feat(budget): DailyAllowance entity and the everyday envelope mark (#99)"
```

---

### Task 2: The opening balance becomes a transaction; `SetBalance` is deleted

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/BudgetAccount.cs:66-70` (delete `SetBalance`)
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/UpdateAccount/UpdateAccountCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/UpdateAccount/UpdateAccountHandler.cs:27`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs` (drop `SetBalance` from the request DTO)
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/UpdateAccountHandlerTests.cs` (drop the SetBalance cases)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `CreateAccountHandler` writes one uncategorised `BudgetTransaction` per non-zero opening balance. `UpdateAccountCommand` becomes `(Guid Id, string Name, int SortOrder, bool IsClosed)` — four members, no `SetBalance`.

- [ ] **Step 1: Write the failing tests**

Append to `CreateAccountHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Opening_balance_is_written_as_an_uncategorised_transaction()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m),
            CancellationToken.None);

        var tx = fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id);
        tx.Amount.Should().Be(40_000m);
        tx.CategoryId.Should().BeNull();          // lands in Ready to Assign
        tx.Notes.Should().Be("Opening balance");
        result.Balance.Should().Be(40_000m);      // the cache still agrees
    }

    [Fact]
    public async Task A_zero_opening_balance_writes_no_transaction()
    {
        // BudgetTransaction.Create throws on a zero amount (BudgetTransaction.cs:27).
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Empty", BudgetAccountType.Cash, 0m),
            CancellationToken.None);

        fx.Db.BudgetTransactions.Any(t => t.AccountId == result.Id).Should().BeFalse();
        result.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_negative_opening_balance_is_written_for_a_liability()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("KBank Credit", BudgetAccountType.Credit, -12_000m),
            CancellationToken.None);

        fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id).Amount.Should().Be(-12_000m);
        result.Balance.Should().Be(-12_000m);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateAccountHandlerTests"`
Expected: FAIL — `Sequence contains no elements` on the `Single(...)`, because no transaction is written today.

- [ ] **Step 3: Rewrite `CreateAccountHandler.Handle`**

Replace the body after the sort-order calculation:

```csharp
        // menunest-183: the opening balance is a BudgetTransaction, not a stored
        // number. A derived balance whose history begins with a non-transaction
        // begins from nowhere.
        var acc = BudgetAccount.Create(familyId, cmd.Name, cmd.Type, 0m, nextSortOrder);
        _db.BudgetAccounts.Add(acc);

        if (cmd.OpeningBalance != 0m)
        {
            var (userId, _) = await _users.RequireFamilyAsync(ct);
            _db.BudgetTransactions.Add(BudgetTransaction.Create(
                familyId, acc.Id, categoryId: null,
                amount: cmd.OpeningBalance,
                date: DateOnly.FromDateTime(DateTime.UtcNow),
                notes: "Opening balance",
                createdByUserId: userId));
            acc.AdjustBalance(cmd.OpeningBalance);   // keep the cache true
        }

        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
```

Hoist the `RequireFamilyAsync` call to the top of the method and destructure both values once — do not call it twice.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateAccountHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Delete `SetBalance` everywhere**

1. `BudgetAccount.cs` — delete the `SetBalance` method and its XML doc comment (lines 61–70). Keep `AdjustBalance`.
2. `UpdateAccountCommand.cs` — drop the `decimal? SetBalance` member.
3. `UpdateAccountHandler.cs` — delete line 27 (`if (c.SetBalance.HasValue) acc.SetBalance(c.SetBalance.Value);`).
4. `BudgetDtos.cs` — drop `SetBalance` from the update-account request DTO.
5. `BudgetController.cs` — remove the argument where it constructs `UpdateAccountCommand`.
6. `UpdateAccountHandlerTests.cs` — delete any test asserting the overwrite, and fix the remaining `new UpdateAccountCommand(...)` calls to four arguments.

- [ ] **Step 6: Run the full backend suite**

Run: `cd backend && dotnet build && dotnet test`
Expected: PASS. The compiler is the checklist here — every remaining `SetBalance` reference is a build error, and there should be none left.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/BudgetAccount.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Accounts/UpdateAccount/ \
        backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/
git commit -m "feat(budget): opening balance writes a transaction, SetBalance is deleted (#99)"
```

---

### Task 3: Derive the account balance as of the month being viewed

This is the two-clock fix. It carries the most weight in the milestone, and menunest-188 destroyed the free correctness check a back-fill would have given — so the seeded tests below are the *only* evidence the derivation is right.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs` (steps 5 and 7)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/GetMonthlySummaryDerivedBalanceTests.cs`

**Interfaces:**
- Consumes: `BudgetTransaction` rows written by Task 2.
- Produces: `MonthlySummaryDto.Accounts[].Balance` and the `ReadyToAssign` figure are both as-of-month.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/GetMonthlySummaryDerivedBalanceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class GetMonthlySummaryDerivedBalanceTests
{
    private static GetMonthlySummaryHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    /// <summary>
    /// July holds 30,000; August adds 22,480. Viewing July must show 30,000 —
    /// what the account held THEN, not the 52,480 it holds today (menunest-182).
    /// </summary>
    private static Guid SeedAccountWithTwoMonths(HandlerTestFixture fx)
    {
        var acc = BudgetAccount.Create(fx.Family.Id, "SCB", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 30_000m, new DateOnly(2026, 7, 15), "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 22_480m, new DateOnly(2026, 8, 3), "Salary", fx.User.Id));
        return acc.Id;
    }

    [Fact]
    public async Task A_past_month_shows_the_balance_held_at_the_end_of_that_month()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(30_000m);
    }

    [Fact]
    public async Task The_current_month_shows_every_transaction_to_date()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(52_480m);
    }

    [Fact]
    public async Task A_month_before_the_first_transaction_shows_zero()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var june = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 6), CancellationToken.None);

        june.Accounts.Single().Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_transaction_on_the_last_day_of_the_month_is_included()
    {
        // Guards the boundary: the filter is Date < firstOfNextMonth, not Date < lastDay.
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 500m, new DateOnly(2026, 7, 31), "Late", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(500m);
    }

    [Fact]
    public async Task Ready_to_assign_uses_the_derived_balance_not_the_stored_one()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        // No envelopes, so Ready to Assign is the whole derived account total.
        july.ReadyToAssign.Should().Be(30_000m);
    }

    [Fact]
    public async Task Another_familys_transactions_never_leak_in()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Mine", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 100m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, 9_999m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(100m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GetMonthlySummaryDerivedBalanceTests"`
Expected: FAIL — July returns `0m` (the stored `Balance` of accounts created at 0), not `30_000m`.

- [ ] **Step 3: Derive the balances in one grouped query**

In `GetMonthlySummaryHandler.Handle`, insert **before** the existing step 5, and note this deliberately loads *all* transactions including uncategorised ones — unlike `allTx`, which filters `CategoryId != null`:

```csharp
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
```

Replace step 5's `totalAccountBalance`:

```csharp
        var accountIds = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId).Select(a => a.Id).ToListAsync(ct);
        var totalAccountBalance = accountIds.Sum(DerivedBalance);
```

Replace step 7's projection — it must materialise the entities first, because `DerivedBalance` is a local function EF cannot translate:

```csharp
        var accountRows = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);
        var accounts = accountRows
            .Select(a => new BudgetAccountDto(
                a.Id, a.Name, a.Type, DerivedBalance(a.Id), a.SortOrder, a.IsClosed))
            .ToList();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GetMonthlySummaryDerivedBalanceTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Run the full backend suite**

Run: `cd backend && dotnet test`
Expected: PASS. Existing `GetMonthlySummary` tests that seeded a stored `Balance` without transactions will now fail — that is correct, and they must be rewritten to seed transactions instead.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/
git commit -m "feat(budget): derive account balances as of the month being viewed (#99)"
```

---

### Task 4: The freeze and the Pace line

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Allowance/AllowanceFreezer.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Categories/SetEverydayMarks/SetEverydayMarksCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Categories/SetEverydayMarks/SetEverydayMarksHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs` (add `DailyAllowanceDto`, add it to `MonthlySummaryDto`)
- Modify: `GetMonthlySummaryHandler.cs`
- Modify: `Monthly/SetAssignedAmount/`, `Monthly/MoveMoney/`, `Monthly/CoverOverspending/` handlers — call the freezer
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Allowance/AllowanceFreezerTests.cs`

**Interfaces:**
- Consumes: `DailyAllowance.Freeze/Refreeze/CompletedDays/PaceDelta/IsForMonth` (Task 1); `IsEveryday` (Task 1).
- **The Pace line formula is NOT written here.** It lives once, on `DailyAllowance.PaceDelta`, and is already tested in Task 1. Call it; never restate it.
- Produces:
  - `AllowanceFreezer.RefreezeAsync(Guid familyId, DateOnly today, CancellationToken) → Task<DailyAllowance?>` — returns `null` when nothing is marked.
  - `AllowanceFreezer.CurrentPotAsync(Guid familyId, DateOnly today, CancellationToken) → Task<decimal>`
  - `DailyAllowanceDto(decimal Amount, DateOnly FrozenOn, decimal PaceDelta, bool HasMarks)` — `PaceDelta` is `actual − should`; positive is over, negative is under, zero renders nothing.

- [ ] **Step 1: Write `AllowanceFreezer`**

Create `backend/src/MenuNest.Application/UseCases/Budget/Allowance/AllowanceFreezer.cs`:

```csharp
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
```

Register it in DI beside the other Application services.

- [ ] **Step 2: Write the freezer's integration tests**

Create `AllowanceFreezerTests.cs` covering: (a) `RefreezeAsync` returns `null` and writes nothing when no envelope is marked; (b) marking one envelope holding 6,000 on a seeded date produces the expected figure; (c) a second `RefreezeAsync` **overwrites** rather than inserting a second row (assert `db.DailyAllowances.Count() == 1`); (d) an unmarked envelope's money is excluded from the pot. Use `HandlerTestFixture` and seed via `BudgetCategory.Create(...)` + `MarkEveryday(true)` + `MonthlyAssignment.Create(...)`.

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~AllowanceFreezerTests"`
Expected: PASS.

- [ ] **Step 3: Wire the three Budgeting events and the lazy rollover**

In `SetAssignedAmountHandler`, `MoveMoneyHandler` and `CoverOverspendingHandler`: after `SaveChangesAsync`, if any touched category has `IsEveryday`, call `RefreezeAsync(familyId, DateOnly.FromDateTime(DateTime.UtcNow), ct)` then save again.

In `SetEverydayMarksHandler` (new): apply every mark in the request via `MarkEveryday(...)`, save once, then `RefreezeAsync` once — one **Budgeting event** for the whole sheet (menunest-184).

In `GetMonthlySummaryHandler`, after the derived balances:

```csharp
        // menunest-185: the card is current-month only, checked against today's
        // real date, not the requested month.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
```

Add `DailyAllowance` to `MonthlySummaryDto` as a nullable trailing member, and inject `AllowanceFreezer` into the handler.

- [ ] **Step 4: Run the full backend suite**

Run: `cd backend && dotnet build && dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Allowance/ \
        backend/src/MenuNest.Application/UseCases/Budget/Categories/SetEverydayMarks/ \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/ \
        backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.Infrastructure/DependencyInjection.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Allowance/
git commit -m "feat(budget): freeze the daily allowance and compute the pace line (#99)"
```

---

### Task 5: The MCP surface and its refuse-then-confirm gate

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CorrectBalance/CorrectBalanceCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CorrectBalance/CorrectBalanceHandler.cs`
- Modify: `backend/src/MenuNest.McpServer/Tools/BudgetTools.cs:53-61`
- Modify: `frontend/src/pages/budget/components/ReconcileBalanceDialog.tsx` (repoint at the new endpoint)
- Test: `backend/tests/MenuNest.McpServer.UnitTests/BudgetToolsCorrectBalanceTests.cs`

**Interfaces:**
- Consumes: `DerivedBalance` logic from Task 3 (reuse the same `Date < nextMonth` sum, scoped to one account and today).
- Produces: `correct_account_balance(Guid accountId, decimal actualBalance, bool confirmed, DateOnly? date, string? notes)`. Returns `BalanceCorrectionResultDto(bool Written, decimal DerivedBalance, decimal Difference, string Message)`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.McpServer.UnitTests/BudgetToolsCorrectBalanceTests.cs`:

```csharp
[Fact]
public async Task An_unconfirmed_call_writes_nothing_and_names_the_numbers()
{
    // Seeded: derived balance 2,400. Stated: 3,000.
    var result = await sut.correct_account_balance(
        accountId, actualBalance: 3000m, confirmed: false, date: null, notes: null, ct);

    result.Written.Should().BeFalse();
    result.DerivedBalance.Should().Be(2400m);
    result.Difference.Should().Be(600m);
    result.Message.Should().Contain("2,400").And.Contain("600");
    db.BudgetTransactions.Should().BeEmpty();
}

[Fact]
public async Task A_confirmed_call_writes_one_uncategorised_correction()
{
    var result = await sut.correct_account_balance(
        accountId, actualBalance: 3000m, confirmed: true, date: null, notes: null, ct);

    result.Written.Should().BeTrue();
    var tx = db.BudgetTransactions.Single();
    tx.Amount.Should().Be(600m);
    tx.CategoryId.Should().BeNull();     // lands in Ready to Assign, no quarantine
    tx.Notes.Should().Be("Balance correction");
}

[Fact]
public async Task A_zero_difference_writes_nothing_and_is_not_an_error()
{
    var result = await sut.correct_account_balance(
        accountId, actualBalance: 2400m, confirmed: true, date: null, notes: null, ct);

    result.Written.Should().BeFalse();
    db.BudgetTransactions.Should().BeEmpty();
}

[Fact]
public async Task The_supplied_date_lands_the_correction_in_that_month()
{
    var result = await sut.correct_account_balance(
        accountId, 3000m, confirmed: true, date: new DateOnly(2026, 7, 31), notes: null, ct);

    db.BudgetTransactions.Single().Date.Should().Be(new DateOnly(2026, 7, 31));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests`
Expected: FAIL to compile — `correct_account_balance` does not exist.

- [ ] **Step 3: Implement the handler and the tool**

`CorrectBalanceHandler` derives today's balance for the account (`SUM(Amount)` over its transactions), computes `difference = actualBalance − derived`, and:

- `difference == 0` → return `Written: false`, no write, message "already correct".
- `!confirmed` → return `Written: false`, no write, and a message naming all three numbers.
- otherwise → `BudgetTransaction.Create(familyId, accountId, categoryId: null, amount: difference, date: date ?? today, notes: notes ?? "Balance correction", userId)`, `AdjustBalance(difference)`, save.

In `BudgetTools.cs`, delete the `setBalance` parameter from `update_budget_account` and correct its description to `"Update a budget account's name, sort order, or closed status"`. Add:

```csharp
    [McpServerTool, Description(
        "State an account's true balance. Writes a Balance correction transaction for the "
        + "difference, which lands in Ready to Assign. SAFETY: the first call MUST pass "
        + "confirmed=false — the server refuses it and returns the derived balance, the "
        + "difference and the Ready-to-Assign movement. Show those numbers to the user, ask "
        + "them, and only re-send with confirmed=true if they agree. Never pass confirmed=true "
        + "on a first attempt.")]
    public async Task<BalanceCorrectionResultDto> correct_account_balance(
        [Description("Account ID")] Guid accountId,
        [Description("The true balance the account actually holds right now")] decimal actualBalance,
        [Description("false on the first call. true only after the user has seen the numbers and agreed.")] bool confirmed,
        [Description("Optional: the date the correction belongs to (defaults to today)")] DateOnly? date,
        [Description("Optional: a note (defaults to 'Balance correction')")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new CorrectBalanceCommand(accountId, actualBalance, confirmed, date, notes), ct);
```

The refusal message is user-facing — format it with thousands separators and name **Ready to Assign** explicitly.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.McpServer.UnitTests`
Expected: PASS.

- [ ] **Step 5: Repoint `ReconcileBalanceDialog`**

It posts a transaction directly today (`ReconcileBalanceDialog.tsx:48-51`). Point it at the new endpoint with `confirmed: true` — the dialog *is* the confirmation, and it already shows the numbers and requires a press. Its visible behaviour must not change.

- [ ] **Step 6: Run the full suite**

Run: `cd backend && dotnet test` and `cd frontend && npx tsc --noEmit`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/CorrectBalance/ \
        backend/src/MenuNest.McpServer/Tools/BudgetTools.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/tests/MenuNest.McpServer.UnitTests/ \
        frontend/src/pages/budget/components/ReconcileBalanceDialog.tsx
git commit -m "feat(budget): gated correct_account_balance replaces setBalance (#99)"
```

---

### Task 6: `DailyAllowanceCard` and its empty state

> **UI task.** vitest cannot render this and SDD's per-task review will not look at it. Steps 4 and 5 are not optional — CLAUDE.md records two features (#46, #97) that shipped visibly broken straight through every gate.

**Files:**
- Create: `frontend/src/pages/budget/components/DailyAllowanceCard.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx` (render above `RtaHero`)
- Modify: `frontend/src/pages/budget/budgetSlice.ts` (carry `dailyAllowance`)
- Modify: `frontend/src/pages/budget/BudgetPage.css`
- Test: `frontend/src/pages/budget/lib/paceLine.ts` + `paceLine.test.ts`

**Interfaces:**
- Consumes: `MonthlySummaryDto.dailyAllowance` → `{ amount, frozenOn, paceDelta, hasMarks } | null` (Task 4).
- Produces: `<DailyAllowanceCard onOpenMarks={() => void} />`; `formatPaceLine(paceDelta: number): string | null`.

- [ ] **Step 1: Extract the pure logic and test it**

The one part of this card that *can* be unit-tested is the wording. Put it in `lib/` so vitest can reach it — CLAUDE.md asks for exactly this.

`frontend/src/pages/budget/lib/paceLine.ts`:

```ts
import {formatTHB} from '../BudgetPage.hooks'

/** menunest-186: null renders nothing — on the freeze day, or when exactly on pace. */
export function formatPaceLine(paceDelta: number): string | null {
  if (Math.abs(paceDelta) < 0.005) return null
  return paceDelta > 0
    ? `you are ${formatTHB(paceDelta)} over`
    : `you are ${formatTHB(-paceDelta)} under`
}
```

`paceLine.test.ts`: asserts `null` at 0 and at 0.004; "over" for positive; "under" for negative (with the sign stripped).

Run: `cd frontend && npx vitest run src/pages/budget/lib/paceLine.test.ts`
Expected: FAIL, then PASS after the module exists.

- [ ] **Step 2: Build the card**

Three states, all in one component:

1. `dailyAllowance === null` → render nothing (not the current month — menunest-185).
2. `hasMarks === false` → the empty state: an invitation, tappable, calling `onOpenMarks`. **Never a number** (menunest-181).
3. otherwise → the headline `formatTHB(amount)`, the sub-line "won't change if you spend more today", and `formatPaceLine(paceDelta)` when it returns a string. The whole card is tappable → `onOpenMarks`.

Give it `data-testid="bdg-daily-allowance"` and the empty state `data-testid="bdg-daily-allowance-empty"` for Task 9's e2e spec.

- [ ] **Step 3: Render it above `RtaHero`**

In `BudgetPage.tsx`, place `<DailyAllowanceCard />` immediately above `<RtaHero />` — the order the mock fixes: MonthStrip → **Daily allowance** → RtaHero → QuickAssignChips → AccountsStrip → filters → EnvelopeList.

- [ ] **Step 4: Diff against the approved mock**

Fetch it and compare — tokens, colours, spacing, structural treatment:

```
DesignSync get_file → project 107862ef-c14b-42f4-a8f2-4bbe36951e25, path screens/budget-shell.html
```

The card uses the existing `bdg-` CSS variable scope (`--accent: #4f46e5`, `--green: #15803d`, `--red: #b91c1c`, `--orange: #b45309`). Do not invent new colours. Passing `tsc` and `build` is **not** evidence the card matches the mock.

- [ ] **Step 5: Verify interactively**

Run the app, open `/budget` at phone width. Confirm: the empty state appears with nothing marked; the card is absent after pressing `‹`; nothing below it is pushed off-screen.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/budget/components/DailyAllowanceCard.tsx \
        frontend/src/pages/budget/lib/paceLine.ts \
        frontend/src/pages/budget/lib/paceLine.test.ts \
        frontend/src/pages/budget/BudgetPage.tsx \
        frontend/src/pages/budget/budgetSlice.ts \
        frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): daily allowance card with pace line and empty state (#99)"
```

---

### Task 7: `EverydayMarksSheet` and the envelope dot

> **UI task.** Steps 4–5 of Task 6 apply here too.

**Files:**
- Create: `frontend/src/pages/budget/components/EverydayMarksSheet.tsx`
- Modify: `frontend/src/pages/budget/components/EnvelopeCard.tsx:32-58`
- Modify: `frontend/src/pages/budget/budgetSlice.ts`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

**Interfaces:**
- Consumes: `onOpenMarks` from Task 6; `SetEverydayMarks` endpoint from Task 4.
- Produces: `<EverydayMarksSheet open onClose />`, dispatching one save on close.

- [ ] **Step 1: Build the sheet**

Lists **every** envelope across **every** group with a tick box — **no group filter**, because the mark is group-independent (menunest-181). Ticks are held in local state; **closing the sheet is the commit point**, dispatching one bulk save → one **Budgeting event** → one re-freeze (menunest-184). Closing with no change dispatches nothing.

`data-testid="bdg-everyday-sheet"`, and each row `data-testid="bdg-everyday-row"`.

- [ ] **Step 2: Add the dot to the collapsed envelope row**

In `EnvelopeCard.tsx`, inside `bdg-env-name` after the emoji, render a small dot when `cat.isEveryday`. The mark lives on the **Envelope** even though it is set elsewhere, so it must be visible there (menunest-184).

- [ ] **Step 3: Check the crowded case**

The collapsed row holds emoji, name, one icon (`⚠` when overspent, else `⇄`) and the money pill. It now gains a dot. Verify at phone width with a **long envelope name** *and* an overspent state — that is where the glyphs compete hardest.

- [ ] **Step 4: Diff against the mock, then verify interactively**

Same as Task 6, Steps 4–5. Additionally confirm: ticking six envelopes and closing once makes the headline change **once**, not six times — that is the whole point of menunest-184.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/budget/components/EverydayMarksSheet.tsx \
        frontend/src/pages/budget/components/EnvelopeCard.tsx \
        frontend/src/pages/budget/budgetSlice.ts \
        frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): everyday marks sheet and the envelope dot (#99)"
```

---

### Task 8: The two one-tap affordances

> **UI task.** Steps 4–5 of Task 6 apply here too.

**Files:**
- Modify: `frontend/src/pages/budget/components/AccountsStrip.tsx`
- Modify: `frontend/src/pages/budget/components/EnvelopeCard.tsx:37-57`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

**Interfaces:**
- Consumes: `ReconcileBalanceDialog` (repointed in Task 5); the existing `TransactionDialog`.
- Produces: no new exports.

- [ ] **Step 1: The `✎` on each account card**

Add a `✎` icon button to each account card in `AccountsStrip`, opening `ReconcileBalanceDialog` for that account directly — no detour through account-detail. Match `EnvelopeCard`'s existing icon-button pattern exactly: `className="bdg-env-icon-btn"`, `onClick={(e) => { e.stopPropagation(); ... }}`, an `aria-label`, and a `data-testid`.

`stopPropagation` is essential — the account card is itself a navigation target.

- [ ] **Step 2: The `＋` on the collapsed envelope row**

Today the row renders `⚠` when overspent and `⇄` otherwise — mutually exclusive. Add `＋` **beside** whichever shows, opening `TransactionDialog` pre-filled with that envelope. `data-testid="bdg-env-add-icon"`.

- [ ] **Step 3: Diff against the mock, then verify interactively**

Same as Task 6, Steps 4–5. Both new icons must be comfortably thumb-reachable at phone width, and the overspent row must still fit.

- [ ] **Step 4: Run the existing e2e specs**

Run: `cd frontend && npx playwright test budget.`
Expected: all four existing specs PASS. `budget.add-entry-points.spec.ts` is the one most likely to break — it asserts on exactly these affordances.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/budget/components/AccountsStrip.tsx \
        frontend/src/pages/budget/components/EnvelopeCard.tsx \
        frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): one-tap balance correction and spend logging (#99)" 
```

---

### Task 9: e2e coverage, then apply the migration to prod

**Files:**
- Create: `frontend/e2e/budget.daily-allowance.spec.ts`

- [ ] **Step 1: Write the e2e spec**

This is the only mechanism in the repo that catches a rendering bug automatically, and it only covers what it exercises (#97 shipped an unstyled page because no spec touched it). Cover:

1. With nothing marked → `bdg-daily-allowance-empty` is visible and no figure renders.
2. Tap the card → `bdg-everyday-sheet` opens and lists envelopes from more than one group.
3. Tick two rows, close → a figure renders in `bdg-daily-allowance`.
4. Press `‹` → `bdg-daily-allowance` is **absent** (menunest-185).
5. The `✎` on an account card opens the reconcile dialog.

Follow the existing fixtures in `frontend/e2e/fixtures/` and `helpers/`.

- [ ] **Step 2: Run the whole budget e2e suite**

Run: `cd frontend && npx playwright test budget.`
Expected: five specs PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/budget.daily-allowance.spec.ts
git commit -m "test(budget): e2e coverage for the daily allowance card (#99)"
```

- [ ] **Step 4: Preview the migration SQL**

**Do not skip this.** The migration deletes every budget row.

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Read the `DELETE FROM` statements and confirm they are the five intended tables.

- [ ] **Step 5: Apply it by hand**

Confirm the terminal session first — `az account show` must report `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th`.

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

If it fails with `Client with IP address '...' is not allowed`, add a **temporary** firewall rule for that IP, apply, then remove it:

```bash
IP=<the address named in the error>
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP
# ... apply ...
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

- [ ] **Step 6: Recreate the budget and verify the milestone**

The budget is now empty. Create the **Accounts**, then the envelope groups and **Envelopes**, then mark the everyday ones. Then confirm the milestone's own sentence on a real phone:

- each **Account** shows what it holds, and `✎` corrects it;
- the card reads "today you can spend X";
- press `‹` — the past month's **Ready to Assign** is now correct, and the card is gone.

---

## Self-Review

**Spec coverage.** §2 model → Task 1. §3.1 opening balance → Task 2. §3.2 correction → Tasks 2, 5. §3.3 derived balance → Task 3. §4.1 freeze + lazy rollover → Task 4. §4.2 Pace line → Task 1 (`DailyAllowance.PaceDelta`, its single home, tested there); Task 4 only calls it. §4.3 empty state → Tasks 4, 6. §4.4 response shape → Task 4. §5 MCP → Task 5. §6 frontend → Tasks 6, 7, 8. §7 migration → Tasks 1, 9. §8 testing → every task, plus Task 9. §9 order → Tasks 1–9. No gaps.

**Type consistency.** `DailyAllowance.Freeze/Refreeze/CompletedDays/PaceDelta/IsForMonth` are defined in Task 1 and used with those exact names in Task 4. `DailyAllowanceDto(Amount, FrozenOn, PaceDelta, HasMarks)` is defined in Task 4 and consumed with those field names in Task 6. `AllowanceFreezer.RefreezeAsync/CurrentPotAsync/HasMarksAsync` are defined and used consistently. `formatPaceLine` is defined in Task 6 Step 1 and used in Step 2. `data-testid` values introduced in Tasks 6–8 are the ones Task 9 asserts on.

**Known ordering hazard.** Task 3 will break existing `GetMonthlySummary` tests that seed a stored `Balance` with no transactions. That is expected and correct — Task 3 Step 5 calls it out rather than leaving the implementer to guess.
