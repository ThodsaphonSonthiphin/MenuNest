# Budget Undo Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record every undoable budget act on the server and expose endpoints to list, undo and redo them — with no user-visible change whatsoever.

**Architecture:** A new `BudgetChange` row is written alongside each of the five undoable acts, storing the **delta** the act applied (never the before/after values). Undo issues a *compensating* write — the opposite delta — rather than restoring an old value, so a concurrent change by another Family member survives it (menunest-193). Redo re-applies the same delta forward. The rows are also the data the Change history screen will later read.

**Tech Stack:** .NET 10, EF Core, Mediator (`ICommandHandler`), FluentValidation, xUnit + Moq + FluentAssertions, SQLite for relational handler tests.

**Spec:** `docs/adr/menunest-193-*.md` (compensating writes), `menunest-194-*.md` (server store, 7-day window, month cut), `menunest-196-*.md` (which acts), `menunest-197-*.md` (staleness), `menunest-198-*.md` (whose acts). Read all five before starting; this plan argues from them.

## Global Constraints

- **This plan ships NOTHING visible.** No frontend file is touched. Every commit must be safe to deploy to prod on push, because `.github/workflows/main_menunest.yml` deploys on every push to `main`.
- **A new `DbSet<>` must be added to all THREE `IApplicationDbContext` implementers** — `AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext` — or the build fails `CS0535` (CLAUDE.md).
- **An entity and its EF configuration must land in the SAME commit.** An unmapped entity fails EF model validation for every test touching the DbContext, so pre-commit can never pass on a split (CLAUDE.md, learned on #33).
- **The migration is applied to prod BY HAND.** Neither the app nor CD runs `db.Database.Migrate()`. Do not apply it as part of this plan — Task 8 hands the user the runbook.
- **Tests use Moq, not NSubstitute.** `var m = new Mock<IUserProvisioner>(); m.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(user);`
- **Every commit references the issue.** Subject ends `(#106)` or the body carries `Refs #106`.
- **`git add <explicit paths>` only.** Never `git add -A` / `.` — `daily-state.md` and `AGENTS.md` must never be swept in.
- **The pre-commit hook runs the FULL suite** (backend build + test Release, frontend tsc + build, ~40s). Expect the wait; never `--no-verify`.
- Decimal money columns use `HasColumnType("decimal(18,4)")`, matching `MonthlyAssignmentConfiguration`.

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/MenuNest.Domain/Enums/BudgetChangeKind.cs` | the four undoable act kinds |
| `backend/src/MenuNest.Domain/Entities/BudgetChange.cs` | one recorded act + its inverse, and its undone state |
| `backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetChangeConfiguration.cs` | table, keys, indexes, FKs |
| `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` | `DbSet<BudgetChange> BudgetChanges` |
| `backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeRecorder.cs` | the single place a change is written; every handler calls it |
| `backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeApplier.cs` | applies a change forward or backward; the only place the inverse is computed |
| `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/*` | command, validator, handler |
| `backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/*` | command, handler |
| `backend/src/MenuNest.Application/UseCases/Budget/History/ListChanges/*` | query, handler, DTO |

`Recorder` and `Applier` are separate on purpose: recording happens inside five existing handlers, applying happens inside two new ones, and keeping the inverse arithmetic in exactly one file is what stops undo and redo drifting apart.

---

### Task 1: The `BudgetChange` entity, its mapping, and the migration

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/BudgetChangeKind.cs`
- Create: `backend/src/MenuNest.Domain/Entities/BudgetChange.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetChangeConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` (add one `DbSet` beside `DailyAllowances`, line ~32)
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/BudgetChangeTests.cs`

**Interfaces:**
- Produces: `BudgetChange.RecordAssign/RecordMove/RecordEverydayMark` factories, `MarkUndone(Guid byUserId, DateTime at)`, `MarkRedone()`, and the properties `FamilyId, UserId, Year, Month, Kind, BatchId, CategoryId, SecondCategoryId, Delta, FlagValue, IsUndone, UndoneByUserId, UndoneAt`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.History;

public class BudgetChangeTests
{
    private static readonly Guid Fam = Guid.NewGuid();
    private static readonly Guid Usr = Guid.NewGuid();
    private static readonly Guid Cat = Guid.NewGuid();

    [Fact]
    public void RecordAssign_stores_the_delta_not_the_absolute_amount()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, delta: 300m, batchId: null);

        c.Kind.Should().Be(BudgetChangeKind.Assign);
        c.Delta.Should().Be(300m);
        c.CategoryId.Should().Be(Cat);
        c.SecondCategoryId.Should().BeNull();
        c.IsUndone.Should().BeFalse();
    }

    [Fact]
    public void RecordAssign_rejects_a_zero_delta()
    {
        var act = () => BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 0m, null);
        act.Should().Throw<DomainException>().WithMessage("*no effect*");
    }

    [Fact]
    public void MarkUndone_then_MarkRedone_returns_the_row_to_active()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 300m, null);
        var at = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        c.MarkUndone(Usr, at);
        c.IsUndone.Should().BeTrue();
        c.UndoneByUserId.Should().Be(Usr);
        c.UndoneAt.Should().Be(at);

        c.MarkRedone();
        c.IsUndone.Should().BeFalse();
        c.UndoneByUserId.Should().BeNull();
        c.UndoneAt.Should().BeNull();
    }

    [Fact]
    public void MarkUndone_twice_is_rejected()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 300m, null);
        c.MarkUndone(Usr, DateTime.UtcNow);

        var act = () => c.MarkUndone(Usr, DateTime.UtcNow);
        act.Should().Throw<DomainException>().WithMessage("*already undone*");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BudgetChangeTests`
Expected: FAIL to **compile** — `BudgetChange` and `BudgetChangeKind` do not exist.

- [ ] **Step 3: Write the enum**

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>
/// The four budget acts Undo covers (menunest-196). Everything else -
/// transactions, balance corrections, account / Envelope / group CRUD - is
/// deliberately out of scope and is never recorded.
/// </summary>
public enum BudgetChangeKind
{
    Assign = 0,
    Move = 1,
    Cover = 2,
    EverydayMark = 3,
}
```

- [ ] **Step 4: Write the entity**

```csharp
using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One recorded budget act, holding the DELTA it applied rather than the
/// values before and after (menunest-193). Undo issues the opposite delta as
/// a new write, so a concurrent change by another Family member survives it;
/// restoring a stored old value would silently destroy that member's work.
///
/// <para><c>BatchId</c> groups the N writes one press of quick-assign makes
/// into a single history row (menunest-196).</para>
///
/// <para><c>Year</c>/<c>Month</c> is the BUDGET month the act belongs to, not
/// the wall clock - menunest-194 cuts the visible window at the start of the
/// current budget month, so this is what that filter reads.</para>
/// </summary>
public sealed class BudgetChange : Entity
{
    public Guid FamilyId { get; private set; }
    /// <summary>Who performed the act. menunest-198: a member may undo their own, the family head may undo anyone's.</summary>
    public Guid UserId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public BudgetChangeKind Kind { get; private set; }
    /// <summary>Non-null when this row is one of N writes from a single quick-assign press.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>The envelope the delta was applied to. For Move/Cover this is the SOURCE.</summary>
    public Guid CategoryId { get; private set; }
    /// <summary>Move/Cover only: the destination envelope, which received the opposite delta.</summary>
    public Guid? SecondCategoryId { get; private set; }
    /// <summary>Signed amount added to <see cref="CategoryId"/>. Zero for EverydayMark.</summary>
    public decimal Delta { get; private set; }
    /// <summary>EverydayMark only: the value the mark was set TO.</summary>
    public bool? FlagValue { get; private set; }

    public bool IsUndone { get; private set; }
    public Guid? UndoneByUserId { get; private set; }
    public DateTime? UndoneAt { get; private set; }

    private BudgetChange() { }

    public static BudgetChange RecordAssign(
        Guid familyId, Guid userId, int year, int month,
        Guid categoryId, decimal delta, Guid? batchId)
    {
        if (delta == 0m) throw new DomainException("An assign with no effect is not recorded.");
        return New(familyId, userId, year, month, BudgetChangeKind.Assign, batchId,
                   categoryId, null, delta, null);
    }

    public static BudgetChange RecordMove(
        Guid familyId, Guid userId, int year, int month,
        Guid fromCategoryId, Guid toCategoryId, decimal amount, bool isCover)
    {
        if (amount <= 0m) throw new DomainException("A move must carry a positive amount.");
        if (fromCategoryId == toCategoryId) throw new DomainException("A move needs two different envelopes.");
        return New(familyId, userId, year, month,
                   isCover ? BudgetChangeKind.Cover : BudgetChangeKind.Move, null,
                   fromCategoryId, toCategoryId, -amount, null);
    }

    public static BudgetChange RecordEverydayMark(
        Guid familyId, Guid userId, int year, int month, Guid categoryId, bool newValue)
        => New(familyId, userId, year, month, BudgetChangeKind.EverydayMark, null,
               categoryId, null, 0m, newValue);

    private static BudgetChange New(
        Guid familyId, Guid userId, int year, int month, BudgetChangeKind kind,
        Guid? batchId, Guid categoryId, Guid? secondCategoryId, decimal delta, bool? flagValue)
    {
        if (familyId == Guid.Empty) throw new DomainException("FamilyId is required.");
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (year < 2000 || year > 2100) throw new DomainException("Invalid year.");
        if (month < 1 || month > 12) throw new DomainException("Invalid month.");
        return new BudgetChange
        {
            FamilyId = familyId,
            UserId = userId,
            Year = year,
            Month = month,
            Kind = kind,
            BatchId = batchId,
            CategoryId = categoryId,
            SecondCategoryId = secondCategoryId,
            Delta = delta,
            FlagValue = flagValue,
            IsUndone = false,
        };
    }

    public void MarkUndone(Guid byUserId, DateTime atUtc)
    {
        if (IsUndone) throw new DomainException("This change is already undone.");
        IsUndone = true;
        UndoneByUserId = byUserId;
        UndoneAt = atUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkRedone()
    {
        if (!IsUndone) throw new DomainException("This change is not undone.");
        IsUndone = false;
        UndoneByUserId = null;
        UndoneAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 5: Write the EF configuration**

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetChangeConfiguration : IEntityTypeConfiguration<BudgetChange>
{
    public void Configure(EntityTypeBuilder<BudgetChange> b)
    {
        b.ToTable("BudgetChanges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Kind).HasConversion<int>().IsRequired();
        b.Property(x => x.Delta).HasColumnType("decimal(18,4)");

        // The list query filters by family + month and orders newest first
        // (menunest-194's window is min(7 days, since the 1st)).
        b.HasIndex(x => new { x.FamilyId, x.Year, x.Month, x.CreatedAt });

        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);

        // Restrict, NOT Cascade: menunest-197 requires a row whose Envelope was
        // deleted to STAY on the list, greyed and unpressable with its reason.
        // Cascade would delete the history row and the reason with it.
        b.HasOne<BudgetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

> **Note on the FK:** `DeleteCategoryHandler` currently hard-deletes an Envelope and its `MonthlyAssignments`. With `Restrict` that delete will now throw once a `BudgetChange` references the Envelope. Task 2's Step 6 handles this; do not "fix" it by switching to `Cascade`, which would defeat menunest-197.

- [ ] **Step 6: Add the DbSet to all three contexts**

In `IApplicationDbContext.cs`, immediately after `DbSet<DailyAllowance> DailyAllowances { get; }`:

```csharp
    DbSet<BudgetChange> BudgetChanges { get; }
```

In `AppDbContext.cs`, `SqliteAppDbContext.cs` and `InMemoryAppDbContext.cs`, add the matching property beside the existing `DailyAllowances` one, copying whatever form that file already uses (e.g. `public DbSet<BudgetChange> BudgetChanges => Set<BudgetChange>();`).

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BudgetChangeTests`
Expected: PASS, 4 tests.

Then run the whole suite, because a new `DbSet` breaks every DbContext test if the mapping is wrong:
Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 8: Create the migration (do NOT apply it)**

```bash
cd backend
dotnet ef migrations add AddBudgetChanges \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Then read the generated `Up()` and confirm it creates only `BudgetChanges` and its index. If it contains anything else, the model snapshot was stale — stop and investigate rather than editing the migration by hand.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/BudgetChangeKind.cs \
        backend/src/MenuNest.Domain/Entities/BudgetChange.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetChangeConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
        backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs \
        backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/BudgetChangeTests.cs
git commit -m "feat(budget): record budget changes as deltas for undo (#106)"
```

---

### Task 2: Record the change when money is assigned

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeRecorder.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetAssignedAmount/SetAssignedAmountCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetAssignedAmount/SetAssignedAmountHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Categories/DeleteCategory/DeleteCategoryHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetAssignedAmountRecordsChangeTests.cs`

**Interfaces:**
- Consumes: `BudgetChange.RecordAssign` from Task 1.
- Produces: `BudgetChangeRecorder.Record(BudgetChange change)` — adds the row to the context but does **not** save; the calling handler's existing `SaveChangesAsync` commits it in the same transaction as the act itself.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetAssignedAmountRecordsChangeTests
{
    private const string Bkk = "Asia/Bangkok";

    private static SetAssignedAmountHandler Sut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

    [Fact]
    public async Task Records_the_delta_from_zero_when_the_assignment_is_new()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 300m, Bkk, null),
            CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.Assign);
        change.CategoryId.Should().Be(cat.Id);
        change.Delta.Should().Be(300m);
        change.Year.Should().Be(2026);
        change.Month.Should().Be(8);
        change.UserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Records_the_difference_when_an_assignment_already_exists()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 200m));
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 500m, Bkk, null),
            CancellationToken.None);

        fx.Db.BudgetChanges.Single().Delta.Should().Be(300m);
    }

    [Fact]
    public async Task Records_nothing_when_the_amount_does_not_change()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 200m));
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 200m, Bkk, null),
            CancellationToken.None);

        fx.Db.BudgetChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Carries_the_batch_id_so_one_quick_assign_press_is_one_row()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var a = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var b = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(a, b);
        await fx.Db.SaveChangesAsync();
        var batch = Guid.NewGuid();

        await Sut(fx).Handle(new SetAssignedAmountCommand(a.Id, 2026, 8, 100m, Bkk, batch), CancellationToken.None);
        await Sut(fx).Handle(new SetAssignedAmountCommand(b.Id, 2026, 8, 200m, Bkk, batch), CancellationToken.None);

        fx.Db.BudgetChanges.Should().HaveCount(2);
        fx.Db.BudgetChanges.Select(c => c.BatchId).Should().AllBeEquivalentTo(batch);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~SetAssignedAmountRecordsChangeTests`
Expected: FAIL to compile — `BudgetChangeRecorder` does not exist and `SetAssignedAmountCommand` has five parameters, not six.

- [ ] **Step 3: Write the recorder**

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Budget.History;

/// <summary>
/// The single place a <see cref="BudgetChange"/> is written. It deliberately
/// does NOT save: the calling handler's own SaveChangesAsync commits the act
/// and its history row together, so a recorded change can never outlive a
/// failed write.
/// </summary>
public sealed class BudgetChangeRecorder
{
    private readonly IApplicationDbContext _db;
    public BudgetChangeRecorder(IApplicationDbContext db) => _db = db;

    public void Record(BudgetChange change) => _db.BudgetChanges.Add(change);
}
```

- [ ] **Step 4: Add `BatchId` to the command**

Replace the record declaration in `SetAssignedAmountCommand.cs`:

```csharp
public sealed record SetAssignedAmountCommand(
    Guid CategoryId, int Year, int Month, decimal Amount, string? TimeZoneId,
    Guid? BatchId)
    : ICommand<Unit>;
```

- [ ] **Step 5: Record the delta in the handler**

In `SetAssignedAmountHandler`, add `BudgetChangeRecorder _recorder` as a constructor parameter and field alongside the existing five. Then replace the assignment block:

```csharp
        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == cmd.CategoryId
              && x.Year == cmd.Year && x.Month == cmd.Month, ct);

        var previous = row?.AssignedAmount ?? 0m;
        var delta = cmd.Amount - previous;

        if (row is null)
            _db.MonthlyAssignments.Add(
                MonthlyAssignment.Create(familyId, cmd.CategoryId, cmd.Year, cmd.Month, cmd.Amount));
        else
            row.SetAmount(cmd.Amount);

        // menunest-193: record the DELTA, never the before/after values. Undo
        // applies the opposite delta as a new write, so a concurrent change by
        // another Family member survives it. A no-op assign records nothing.
        if (delta != 0m)
        {
            var (user, _) = await _users.RequireFamilyAsync(ct);
            _recorder.Record(BudgetChange.RecordAssign(
                familyId, user.Id, cmd.Year, cmd.Month, cmd.CategoryId, delta, cmd.BatchId));
        }

        await _db.SaveChangesAsync(ct);
```

Change the existing destructure at the top of `Handle` from `var (_, familyId)` to `var (user, familyId)` and use `user.Id` directly instead of calling `RequireFamilyAsync` twice.

- [ ] **Step 6: Stop `DeleteCategory` from breaking on the new FK**

Task 1 mapped `BudgetChange.CategoryId` with `DeleteBehavior.Restrict`, so deleting an Envelope that has history now throws a `DbUpdateException` instead of the domain error users expect. In `DeleteCategoryHandler.Handle`, immediately after the existing `hasTx` guard, add:

```csharp
        var hasHistory = await _db.BudgetChanges.AnyAsync(h => h.CategoryId == c.Id, ct);
        if (hasHistory)
            throw new DomainException("Cannot delete category with recent budget history — hide it instead.");
```

> This narrows menunest-197's "the Envelope was deleted" case rather than removing it: an Envelope with history can no longer be deleted at all while that history is inside the window, so a dead row is now only reachable for changes recorded before the Envelope existed in history. Leave menunest-197's disabled-row behaviour in the plan for Task 7 — it is still needed, and now it is also rare enough to be cheap.

- [ ] **Step 7: Fix the other call sites**

`SetAssignedAmountCommand` gained a parameter, so every construction of it fails to compile. Run the build and fix each one by passing `null` for `BatchId`:

Run: `cd backend && dotnet build`
Expected: errors listing each call site. Add `, null` to each, and register `BudgetChangeRecorder` in DI beside the other budget services in `backend/src/MenuNest.Application/DependencyInjection.cs` (`services.AddScoped<BudgetChangeRecorder>();`).

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS, including the four new tests.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeRecorder.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/SetAssignedAmount/ \
        backend/src/MenuNest.Application/UseCases/Budget/Categories/DeleteCategory/DeleteCategoryHandler.cs \
        backend/src/MenuNest.Application/DependencyInjection.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/SetAssignedAmountRecordsChangeTests.cs
git commit -m "feat(budget): record a change when money is assigned (#106)"
```

---

### Task 3: Record the change when money is moved or overspending is covered

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/MoveMoney/MoveMoneyHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/CoverOverspending/CoverOverspendingHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/MoveMoneyRecordsChangeTests.cs`

**Interfaces:**
- Consumes: `BudgetChange.RecordMove(familyId, userId, year, month, fromCategoryId, toCategoryId, amount, isCover)` and `BudgetChangeRecorder.Record`.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class MoveMoneyRecordsChangeTests
{
    private const string Bkk = "Asia/Bangkok";

    [Fact]
    public async Task Records_one_row_holding_the_source_as_a_negative_delta()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 8, 1000m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, to.Id, 2026, 8, 500m));
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(new MoveMoneyCommand(from.Id, to.Id, 2026, 8, 300m, Bkk), CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.Move);
        change.CategoryId.Should().Be(from.Id);
        change.SecondCategoryId.Should().Be(to.Id);
        change.Delta.Should().Be(-300m);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~MoveMoneyRecordsChangeTests`
Expected: FAIL to compile — `MoveMoneyHandler` takes five constructor arguments, not six.

- [ ] **Step 3: Record in `MoveMoneyHandler`**

Add `BudgetChangeRecorder _recorder` as a sixth constructor parameter and field. Change the destructure to `var (user, familyId)`. Then, immediately before the existing `await _db.SaveChangesAsync(ct);`:

```csharp
        _recorder.Record(BudgetChange.RecordMove(
            familyId, user.Id, cmd.Year, cmd.Month,
            cmd.FromCategoryId, cmd.ToCategoryId, cmd.Amount, isCover: false));
```

- [ ] **Step 4: Record in `CoverOverspendingHandler`**

Apply the identical change, with `isCover: true` and using that handler's own command property names for the source and destination categories. Read the file first — do not assume the property names match `MoveMoneyCommand`.

- [ ] **Step 5: Fix the call sites and run the tests**

Run: `cd backend && dotnet build`
Expected: errors at each `new MoveMoneyHandler(...)` / `new CoverOverspendingHandler(...)` in the existing tests. Add `, new BudgetChangeRecorder(fx.Db)` to each.

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Monthly/MoveMoney/MoveMoneyHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/CoverOverspending/CoverOverspendingHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/
git commit -m "feat(budget): record a change when money moves between envelopes (#106)"
```

---

### Task 4: Record the change when an everyday mark is toggled

**Files:**
- Modify: the handler behind `POST /api/budget/categories/everyday-marks` (find it with `grep -rl "everyday-marks" backend/src`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/EverydayMarksRecordChangeTests.cs`

**Interfaces:**
- Consumes: `BudgetChange.RecordEverydayMark(familyId, userId, year, month, categoryId, newValue)`.

- [ ] **Step 1: Read the handler first**

Run: `grep -rl "everyday-marks" backend/src` then read the handler it names, plus `frontend/src/pages/budget/lib/everydayMarksDiff.ts`, which computes what the SPA sends. The endpoint takes a **set** of marks, so one press can toggle several categories — record **one row per category whose value actually changed**, exactly as Task 2 records nothing for a no-op assign.

- [ ] **Step 2: Write the failing test**

Model it on `SetAssignedAmountRecordsChangeTests`: arrange two categories, one already `IsEveryday: true` and one `false`; send a request that flips only the second; assert `fx.Db.BudgetChanges` contains exactly one row, with `Kind == BudgetChangeKind.EverydayMark`, `CategoryId` the flipped one, and `FlagValue == true`.

The year and month to record are the ones the request carries; if the command has no year/month, use the current budget month from `fx.Clock` resolved through `BudgetTimeZone.Resolve` the same way the handler already does for the allowance freeze.

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~EverydayMarksRecordChangeTests`
Expected: FAIL — no rows are recorded.

- [ ] **Step 4: Record one row per actually-changed mark**

Add `BudgetChangeRecorder` to the handler's constructor, compute the set of categories whose `IsEveryday` differs from the requested value, and record one `BudgetChange.RecordEverydayMark` per difference before the existing `SaveChangesAsync`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Categories/ \
        backend/tests/MenuNest.Application.UnitTests/Budget/Categories/
git commit -m "feat(budget): record a change when an everyday mark is toggled (#106)"
```

---

### Task 5: The applier — the one place the inverse is computed

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeApplier.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/BudgetChangeApplierTests.cs`

**Interfaces:**
- Consumes: `BudgetChange` from Task 1.
- Produces: `Task ApplyAsync(BudgetChange change, int direction, CancellationToken ct)` where `direction` is `-1` to undo and `+1` to redo. It mutates `MonthlyAssignment` / `BudgetCategory` rows and does **not** save.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.History;

public class BudgetChangeApplierTests
{
    [Fact]
    public async Task Undoing_an_assign_subtracts_the_delta_and_leaves_a_concurrent_change_standing()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        // The user assigned 300; then somebody else added 100 on top.
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 400m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        // 400 - 300 = 100. A rollback to "0" would have destroyed the other 100.
        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Undoing_a_move_returns_the_money_to_the_source()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 8, 700m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, to.Id, 2026, 8, 800m));
        var change = BudgetChange.RecordMove(fx.Family.Id, fx.User.Id, 2026, 8, from.Id, to.Id, 300m, false);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == from.Id).AssignedAmount.Should().Be(1000m);
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == to.Id).AssignedAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Redoing_re_applies_the_same_delta_forward()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 100m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, +1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(400m);
    }

    [Fact]
    public async Task Undoing_an_everyday_mark_flips_it_back()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.SetEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        var change = BudgetChange.RecordEverydayMark(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, true);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.BudgetCategories.Single().IsEveryday.Should().BeFalse();
    }
}
```

> If `BudgetCategory` exposes the everyday mark under a different method name than `SetEveryday(bool)`, read the entity and use the real one in both the test and the applier.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BudgetChangeApplierTests`
Expected: FAIL to compile — `BudgetChangeApplier` does not exist.

- [ ] **Step 3: Write the applier**

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History;

/// <summary>
/// The ONLY place the inverse of a recorded change is computed, so undo and
/// redo can never drift apart: redo is the same arithmetic with the sign
/// flipped. Every path applies a COMPENSATING delta (menunest-193) - nothing
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
                var row = await RequireAssignmentAsync(change.FamilyId, change.CategoryId, change.Year, change.Month, ct);
                row.AdjustAmount(change.Delta * direction);
                break;

            case BudgetChangeKind.Move:
            case BudgetChangeKind.Cover:
                if (change.SecondCategoryId is null)
                    throw new DomainException("A move change is missing its destination.");
                var from = await RequireAssignmentAsync(change.FamilyId, change.CategoryId, change.Year, change.Month, ct);
                var to = await RequireAssignmentAsync(change.FamilyId, change.SecondCategoryId.Value, change.Year, change.Month, ct);
                from.AdjustAmount(change.Delta * direction);
                to.AdjustAmount(-change.Delta * direction);
                break;

            case BudgetChangeKind.EverydayMark:
                if (change.FlagValue is null)
                    throw new DomainException("An everyday-mark change is missing its value.");
                var cat = await _db.BudgetCategories.FirstOrDefaultAsync(
                    c => c.Id == change.CategoryId && c.FamilyId == change.FamilyId, ct)
                    ?? throw new DomainException("That envelope no longer exists.");
                cat.SetEveryday(direction == 1 ? change.FlagValue.Value : !change.FlagValue.Value);
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

        // The assignment row can legitimately be absent - a move whose
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BudgetChangeApplierTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/BudgetChangeApplier.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/BudgetChangeApplierTests.cs
git commit -m "feat(budget): compute the inverse of a recorded change in one place (#106)"
```

---

### Task 6: Undo and redo endpoints

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/RedoChangeCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/RedoChangeHandler.cs`
- Modify: the budget endpoint file in `backend/src/MenuNest.WebApi` (find with `grep -rl "budget/monthly/move" backend/src/MenuNest.WebApi`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/UndoChangeHandlerTests.cs`

**Interfaces:**
- Consumes: `BudgetChangeApplier.ApplyAsync`, `BudgetChange.MarkUndone/MarkRedone`.
- Produces: `POST /api/budget/history/{id}/undo` and `POST /api/budget/history/{id}/redo`, both returning 204.

**Permission for this plan:** a member may undo **only their own** changes. The family-head override from menunest-198 is **not** implemented here — that role does not exist yet and is Plan 2. Task 6 must leave a single, clearly-marked seam for it rather than a half-built check.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.History;

public class UndoChangeHandlerTests
{
    private static UndoChangeHandler Sut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock);

    private static (BudgetCategory cat, BudgetChange change) Seed(HandlerTestFixture fx, Guid actorId)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, actorId, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return (cat, change);
    }

    [Fact]
    public async Task Undoes_my_own_change_and_marks_the_row()
    {
        using var fx = new HandlerTestFixture();
        var (_, change) = Seed(fx, fx.User.Id);

        await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(0m);
        var reloaded = fx.Db.BudgetChanges.Single();
        reloaded.IsUndone.Should().BeTrue();
        reloaded.UndoneByUserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Refuses_to_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();
        var (_, change) = Seed(fx, Guid.NewGuid());

        var act = async () => await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own*");
    }

    [Fact]
    public async Task Refuses_to_undo_a_change_that_is_already_undone()
    {
        using var fx = new HandlerTestFixture();
        var (_, change) = Seed(fx, fx.User.Id);
        await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        var act = async () => await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already undone*");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UndoChangeHandlerTests`
Expected: FAIL to compile — the command and handler do not exist.

- [ ] **Step 3: Write the command and handler**

```csharp
using Mediator;
namespace MenuNest.Application.UseCases.Budget.History.UndoChange;
public sealed record UndoChangeCommand(Guid ChangeId) : ICommand<Unit>;
```

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History.UndoChange;

public sealed class UndoChangeHandler : ICommandHandler<UndoChangeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly BudgetChangeApplier _applier;
    private readonly IClock _clock;

    public UndoChangeHandler(
        IApplicationDbContext db, IUserProvisioner users,
        BudgetChangeApplier applier, IClock clock)
    { _db = db; _users = users; _applier = applier; _clock = clock; }

    public async ValueTask<Unit> Handle(UndoChangeCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var change = await _db.BudgetChanges.FirstOrDefaultAsync(
            c => c.Id == cmd.ChangeId && c.FamilyId == familyId, ct)
            ?? throw new DomainException("Change not found.");

        // menunest-198 also lets the FAMILY HEAD undo anyone's change. That role
        // does not exist yet - it is built in the family-head plan - so this is
        // the single seam where the check is widened. Do not scatter the rule.
        if (change.UserId != user.Id)
            throw new DomainException("You can only undo your own changes.");

        await _applier.ApplyAsync(change, -1, ct);
        change.MarkUndone(user.Id, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 4: Write redo as the mirror image**

`RedoChangeCommand(Guid ChangeId)` and `RedoChangeHandler` are identical to the above with three changes: `ApplyAsync(change, +1, ct)`, `change.MarkRedone()`, and the ownership message reading `"You can only redo your own changes."` Copy the whole handler rather than extracting a shared base — the two are short and the duplication is easier to read than the abstraction.

- [ ] **Step 5: Add the endpoints**

Find the file that already maps `budget/monthly/move` and add beside it, matching whatever mapping style that file uses:

```csharp
group.MapPost("/history/{id:guid}/undo", async (Guid id, ISender sender, CancellationToken ct) =>
{
    await sender.Send(new UndoChangeCommand(id), ct);
    return Results.NoContent();
});

group.MapPost("/history/{id:guid}/redo", async (Guid id, ISender sender, CancellationToken ct) =>
{
    await sender.Send(new RedoChangeCommand(id), ct);
    return Results.NoContent();
});
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/ \
        backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/ \
        backend/src/MenuNest.WebApi/ \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/UndoChangeHandlerTests.cs
git commit -m "feat(budget): undo and redo endpoints for a recorded change (#106)"
```

---

### Task 7: The history list endpoint, with the window and the undoable flag

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/ListChanges/BudgetChangeDto.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/ListChanges/ListChangesQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/History/ListChanges/ListChangesHandler.cs`
- Modify: the same WebApi endpoint file as Task 6
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/ListChangesHandlerTests.cs`

**Interfaces:**
- Produces: `GET /api/budget/history?year=&month=` returning `IReadOnlyList<BudgetChangeDto>`, newest first, where
  `BudgetChangeDto(Guid Id, Guid UserId, string UserDisplayName, BudgetChangeKind Kind, Guid? BatchId, string CategoryName, string? SecondCategoryName, decimal Delta, bool? FlagValue, bool IsUndone, string? UndoneByDisplayName, DateTime CreatedAt, bool CanUndo, string? BlockedReason)`.

**The window (menunest-194):** rows are visible when `CreatedAt >= max(now - 7 days, the first instant of the requested budget month)`. The month is a **hard cut** — a row from a previous month is never returned, even inside seven days.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History.ListChanges;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.History;

public class ListChangesHandlerTests
{
    [Fact]
    public async Task Returns_this_months_rows_newest_first_and_excludes_last_month()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var thisMonth = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        var lastMonth = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 7, cat.Id, 100m, null);
        fx.Db.BudgetChanges.AddRange(thisMonth, lastMonth);
        await fx.Db.SaveChangesAsync();

        var result = await new ListChangesHandler(fx.Db, fx.UserProvisioner.Object, fx.Clock)
            .Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(thisMonth.Id);
        result[0].CategoryName.Should().Be("Groceries");
        result[0].CanUndo.Should().BeTrue();
        result[0].UserDisplayName.Should().Be(fx.User.DisplayName);
    }
}
```

> `fx.Clock` is a fixed test clock. Read `HandlerTestFixture` and set its `UtcNow` to a date inside August 2026 so the seven-day half of the window does not exclude the seeded rows. If the fixture's clock is not settable, add a settable property to it in this task.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ListChangesHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the DTO and query**

```csharp
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

public sealed record BudgetChangeDto(
    Guid Id, Guid UserId, string UserDisplayName,
    BudgetChangeKind Kind, Guid? BatchId,
    string CategoryName, string? SecondCategoryName,
    decimal Delta, bool? FlagValue,
    bool IsUndone, string? UndoneByDisplayName,
    DateTime CreatedAt,
    bool CanUndo, string? BlockedReason);
```

```csharp
using Mediator;
namespace MenuNest.Application.UseCases.Budget.History.ListChanges;
public sealed record ListChangesQuery(int Year, int Month) : IQuery<IReadOnlyList<BudgetChangeDto>>;
```

- [ ] **Step 4: Write the handler**

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

public sealed class ListChangesHandler : IQueryHandler<ListChangesQuery, IReadOnlyList<BudgetChangeDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IClock _clock;

    public ListChangesHandler(IApplicationDbContext db, IUserProvisioner users, IClock clock)
    { _db = db; _users = users; _clock = clock; }

    public async ValueTask<IReadOnlyList<BudgetChangeDto>> Handle(
        ListChangesQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        // menunest-194: min(7 days, since the 1st of the requested month). The
        // month is a HARD cut, so a row from a previous month is never returned
        // even when it is inside seven days.
        var monthStart = new DateTime(q.Year, q.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sevenDaysAgo = _clock.UtcNow.AddDays(-7);
        var floor = monthStart > sevenDaysAgo ? monthStart : sevenDaysAgo;

        var rows = await (
            from h in _db.BudgetChanges
            join u in _db.Users on h.UserId equals u.Id
            join c in _db.BudgetCategories on h.CategoryId equals c.Id into cats
            from c in cats.DefaultIfEmpty()
            where h.FamilyId == familyId
               && h.Year == q.Year && h.Month == q.Month
               && h.CreatedAt >= floor
            orderby h.CreatedAt descending
            select new
            {
                h.Id, h.UserId, UserName = u.DisplayName, h.Kind, h.BatchId,
                CategoryName = c != null ? c.Name : null,
                h.SecondCategoryId, h.Delta, h.FlagValue,
                h.IsUndone, h.UndoneByUserId, h.CreatedAt
            }).ToListAsync(ct);

        var secondIds = rows.Where(r => r.SecondCategoryId != null)
                            .Select(r => r.SecondCategoryId!.Value).Distinct().ToList();
        var secondNames = await _db.BudgetCategories
            .Where(c => secondIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var undoerIds = rows.Where(r => r.UndoneByUserId != null)
                            .Select(r => r.UndoneByUserId!.Value).Distinct().ToList();
        var undoerNames = await _db.Users
            .Where(u => undoerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(r =>
        {
            // menunest-197: a row whose Envelope is gone STAYS on the list,
            // unpressable, saying why - it is never dropped.
            var gone = r.CategoryName is null;
            return new BudgetChangeDto(
                r.Id, r.UserId, r.UserName, r.Kind, r.BatchId,
                r.CategoryName ?? "(deleted envelope)",
                r.SecondCategoryId is null ? null
                    : secondNames.TryGetValue(r.SecondCategoryId.Value, out var n) ? n : "(deleted envelope)",
                r.Delta, r.FlagValue, r.IsUndone,
                r.UndoneByUserId is null ? null
                    : undoerNames.TryGetValue(r.UndoneByUserId.Value, out var un) ? un : null,
                r.CreatedAt,
                CanUndo: !gone,
                BlockedReason: gone ? "That envelope was deleted." : null);
        }).ToList();
    }
}
```

- [ ] **Step 5: Add the endpoint**

```csharp
group.MapGet("/history", async (int year, int month, ISender sender, CancellationToken ct) =>
    Results.Ok(await sender.Send(new ListChangesQuery(year, month), ct)));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/ListChanges/ \
        backend/src/MenuNest.WebApi/ \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/ListChangesHandlerTests.cs
git commit -m "feat(budget): list recorded budget changes for the current month (#106)"
```

---

### Task 8: Apply the migration to prod, by hand

**Files:** none — this task changes no code.

This is deliberately its own task because it is the only step that touches production data, and it must happen **after** the code is deployed, never before.

- [ ] **Step 1: Preview the SQL before touching prod**

```bash
cd backend
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --output /tmp/budgetchanges.sql
```

Read it. It must create only `BudgetChanges` and its index.

- [ ] **Step 2: Open the SQL firewall for your IP, temporarily**

```bash
IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP
```

- [ ] **Step 3: Apply it**

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` is required — without it SqlClient picks the Visual Studio **work** account and the login fails against the personal-tenant server. Confirm first that `az account show` reports `Pay-As-You-Go` / `personal@example.com`.

- [ ] **Step 4: Close the firewall again**

```bash
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

- [ ] **Step 5: Verify prod still works**

Open `/budget` in prod and assign money to an envelope. The page must behave exactly as before — this plan adds no visible change. If the API returns 500 with `Invalid object name 'BudgetChanges'`, the migration did not apply; re-run Step 3.

---

## Self-Review

**Spec coverage.** menunest-193 (deltas, compensating) — Tasks 1, 5. menunest-194 (server store, 7-day + month cut) — Tasks 1, 7. menunest-196 (five acts; quick-assign as one row) — Tasks 2, 3, 4 and the `BatchId` column. menunest-197 (dead row stays, disabled, with a reason) — Task 7's `CanUndo` / `BlockedReason` and Task 1's `Restrict` FK. menunest-198 (whose acts) — Task 6's ownership check, with the head override explicitly deferred to Plan 2 and marked at one seam.

**Not covered here, by design:** the family-head override, the `LeaveFamily` guard, `IWebPushSender`, and every frontend file. Those are Plans 2 and 3.

**Known gap this plan opens deliberately:** Task 2 Step 6 blocks deleting an Envelope that has history. That is a behaviour change to an existing feature, visible as a new error message. It is the cheapest way to honour menunest-197's FK without a soft delete, but it should be called out in the PR body so it is not discovered as a surprise.
