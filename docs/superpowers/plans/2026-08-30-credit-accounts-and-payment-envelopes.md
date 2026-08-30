# Credit Accounts and Payment Envelopes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every **Credit** **Account** a **Payment envelope** that holds the money to pay its bill, so "can I pay this in full?" is one number on screen.

**Architecture:** Two nullable columns carry it — `BudgetCategories.PaymentForAccountId` binds an envelope to a card, `BudgetTransactions.PaymentId` pairs the two halves of a payment. The envelope's balance is **never stored**: it is derived at read time from the card's own transactions. **Ready to Assign** stops counting Credit and Loan accounts, which is what keeps the arithmetic honest and leaves pre-budget debt outside the budget.

**Tech Stack:** .NET 9 · EF Core (SQL Server) · Mediator · FluentValidation · xUnit + Moq + FluentAssertions · React 19 + RTK Query + Syncfusion · Playwright

**Spec:** `docs/superpowers/specs/2026-08-30-credit-accounts-and-payment-envelopes-design.md` (commit `812a082`)
**Decisions:** `docs/adr/menunest-202-*` … `menunest-213-*`
**Confirmed screen:** <https://claude.ai/code/artifact/ec003765-9fb8-420b-a253-80a76463913a>
**Branch:** `claude/grill-plan-skill-yj0fdb`

## Global Constraints

- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` (`set -e`) runs backend `dotnet build` + `dotnet test` (Release) **and** frontend `tsc --noEmit` + `npm run build` on every commit, ~40s. Never `--no-verify`.
- **An entity change and its EF configuration must land in the SAME commit.** An unmapped model fails EF model validation for every test that touches the context (learned on #33).
- **`git add <explicit paths>` only.** Never `git add -A` / `git add .`. `daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) must never enter a feature commit.
- **Every commit message references the issue:** `type(scope): summary (#112)`. The final task uses `(closes #112)`.
- **Four classes implement `IApplicationDbContext`** — `AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`, and `SaveChangesCountingDbContext` (a **decorator**, not a `DbContext`). This plan adds **no new `DbSet`**, so none of them change. Do not add one.
- **Backend tests use Moq**, never NSubstitute. `Substitute.For<>` will not compile.
- **Money columns are `decimal(18,4)`.** Every new decimal property needs `HasColumnType("decimal(18,4)")`.
- **The migration is applied to prod BY HAND** (`CLAUDE.md`). Nothing in `Program.cs` or `main_menunest.yml` runs it.
- **Do not push to `main`.** Pushing to `main` deploys to prod.
- Thai UI copy is authoritative as written here. Do not translate it.

---

### Task 1: Domain fields, EF configuration, migration

Adds both columns and their invariants. No behaviour changes yet — nothing reads the new fields, so the app is unaffected and the suite must stay green.

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/BudgetCategory.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/BudgetTransaction.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetCategoryConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetTransactionConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<generated>_AddPaymentEnvelopes.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/PaymentEnvelopeDomainTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `BudgetCategory.PaymentForAccountId` → `Guid?`
  - `BudgetCategory.IsPaymentEnvelope` → `bool`
  - `BudgetCategory.CreatePaymentEnvelope(Guid familyId, Guid groupId, Guid accountId, string accountName, int sortOrder)` → `BudgetCategory`
  - `BudgetCategory.RenameForAccount(string accountName)` → `void`
  - `BudgetTransaction.PaymentId` → `Guid?`
  - `BudgetTransaction.CreatePaymentLeg(Guid familyId, Guid accountId, decimal amount, DateOnly date, string? notes, Guid createdByUserId, Guid paymentId)` → `BudgetTransaction`

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/PaymentEnvelopeDomainTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class PaymentEnvelopeDomainTests
{
    private static BudgetCategory NewPaymentEnvelope() =>
        BudgetCategory.CreatePaymentEnvelope(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "KBank", 0);

    private static BudgetCategory NewOrdinary() =>
        BudgetCategory.Create(Guid.NewGuid(), Guid.NewGuid(), "อาหาร", "🍜", 0);

    [Fact]
    public void A_payment_envelope_is_named_for_its_account()
    {
        NewPaymentEnvelope().Name.Should().Be("จ่ายบัตร KBank");
    }

    [Fact]
    public void A_payment_envelope_knows_it_is_one()
    {
        NewPaymentEnvelope().IsPaymentEnvelope.Should().BeTrue();
        NewOrdinary().IsPaymentEnvelope.Should().BeFalse();
    }

    // menunest-205: the Daily allowance divides Everyday money by days left in
    // the month, so a payment envelope in that pot would RAISE "spend this much
    // today" every time the card is used.
    [Fact]
    public void A_payment_envelope_cannot_be_marked_everyday()
    {
        var env = NewPaymentEnvelope();
        var act = () => env.MarkEveryday(true);
        act.Should().Throw<DomainException>().WithMessage("*everyday*");
    }

    [Fact]
    public void Unmarking_everyday_on_a_payment_envelope_is_a_harmless_no_op()
    {
        var env = NewPaymentEnvelope();
        env.MarkEveryday(false);
        env.IsEveryday.Should().BeFalse();
    }

    [Fact]
    public void A_payment_envelope_cannot_be_renamed_or_regrouped_by_Update()
    {
        var env = NewPaymentEnvelope();
        var act = () => env.Update("บัตรแม่", null, Guid.NewGuid(), 3);
        act.Should().Throw<DomainException>().WithMessage("*payment envelope*");
    }

    [Fact]
    public void A_payment_envelope_cannot_be_hidden_by_hand()
    {
        var act = () => NewPaymentEnvelope().Hide();
        act.Should().Throw<DomainException>().WithMessage("*payment envelope*");
    }

    // The name follows the Account (menunest-212), so an account rename must be
    // able to push through — by its own method, not by Update.
    [Fact]
    public void RenameForAccount_retitles_the_envelope()
    {
        var env = NewPaymentEnvelope();
        env.RenameForAccount("KBank Platinum");
        env.Name.Should().Be("จ่ายบัตร KBank Platinum");
    }

    [Fact]
    public void An_ordinary_envelope_is_unaffected_by_any_of_these_guards()
    {
        var cat = NewOrdinary();
        cat.MarkEveryday(true);
        cat.Update("อาหาร2", "🍲", cat.GroupId, 2);
        cat.Hide();
        cat.IsEveryday.Should().BeTrue();
        cat.Name.Should().Be("อาหาร2");
        cat.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Both_legs_of_a_payment_carry_the_same_PaymentId()
    {
        var famId = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 30);

        var outLeg = BudgetTransaction.CreatePaymentLeg(
            famId, Guid.NewGuid(), -500m, date, null, userId, payId);
        var inLeg = BudgetTransaction.CreatePaymentLeg(
            famId, Guid.NewGuid(), 500m, date, null, userId, payId);

        outLeg.PaymentId.Should().Be(payId);
        inLeg.PaymentId.Should().Be(payId);
        outLeg.CategoryId.Should().BeNull("a payment is not spending");
        inLeg.CategoryId.Should().BeNull("a payment is not spending");
    }

    [Fact]
    public void An_ordinary_transaction_has_no_PaymentId()
    {
        BudgetTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, -100m,
            new DateOnly(2026, 8, 30), null, Guid.NewGuid())
            .PaymentId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeDomainTests`
Expected: **compile error** — `CreatePaymentEnvelope`, `IsPaymentEnvelope`, `RenameForAccount`, `CreatePaymentLeg` and `PaymentId` do not exist.

- [ ] **Step 3: Add the BudgetCategory members**

In `backend/src/MenuNest.Domain/Entities/BudgetCategory.cs`, add the property after `IsEveryday`:

```csharp
    /// <summary>
    /// Non-null exactly on a Payment envelope — the Credit account this envelope
    /// holds money to pay (menunest-202). One per account, enforced by a filtered
    /// unique index. A Loan account never has one (menunest-206).
    /// </summary>
    public Guid? PaymentForAccountId { get; private set; }

    public bool IsPaymentEnvelope => PaymentForAccountId.HasValue;
```

Add the factory after `Create`:

```csharp
    public static BudgetCategory CreatePaymentEnvelope(
        Guid familyId, Guid groupId, Guid accountId, string accountName, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("Account name is required.");
        return new BudgetCategory
        {
            FamilyId = familyId,
            GroupId = groupId,
            Name = $"จ่ายบัตร {accountName.Trim()}",
            Emoji = "💳",
            SortOrder = sortOrder,
            IsHidden = false,
            IsEveryday = false,
            TargetType = BudgetTargetType.None,
            PaymentForAccountId = accountId
        };
    }

    /// <summary>
    /// The only path that may retitle a Payment envelope: its name follows its
    /// Account (menunest-205, menunest-212), so an account rename pushes through
    /// here while <see cref="Update"/> stays closed.
    /// </summary>
    public void RenameForAccount(string accountName)
    {
        if (!IsPaymentEnvelope)
            throw new DomainException("Not a payment envelope.");
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("Account name is required.");
        Name = $"จ่ายบัตร {accountName.Trim()}";
        UpdatedAt = DateTime.UtcNow;
    }
```

Replace `Update`, `Hide` and `MarkEveryday` with their guarded forms:

```csharp
    public void Update(string name, string? emoji, Guid groupId, int sortOrder)
    {
        if (IsPaymentEnvelope)
            throw new DomainException(
                "A payment envelope cannot be renamed or moved — its name follows its account.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");
        Name = name.Trim();
        Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
        GroupId = groupId;
        SortOrder = sortOrder;
        UpdatedAt = DateTime.UtcNow;
    }
```

```csharp
    public void Hide()
    {
        if (IsPaymentEnvelope)
            throw new DomainException("A payment envelope cannot be hidden.");
        IsHidden = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Closing a Credit account hides its Payment envelope (menunest-210). That is
    /// the app's own act, not the User's, so it bypasses <see cref="Hide"/>'s guard.
    /// </summary>
    public void SetHiddenForAccountClosure(bool hidden)
    {
        if (!IsPaymentEnvelope) throw new DomainException("Not a payment envelope.");
        IsHidden = hidden;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkEveryday(bool isEveryday)
    {
        if (isEveryday && IsPaymentEnvelope)
            throw new DomainException(
                "A payment envelope cannot be an everyday envelope — it would inflate the daily allowance.");
        IsEveryday = isEveryday;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Add the BudgetTransaction members**

In `backend/src/MenuNest.Domain/Entities/BudgetTransaction.cs`, add after `CreatedByUserId`:

```csharp
    /// <summary>
    /// Shared by the two legs of one payment (menunest-204, menunest-209), so the
    /// pair is found, edited and deleted as one row. Pairing only — it carries no
    /// arithmetic weight, which is why payments written before this feature
    /// shipped still compute correctly (spec §4.2).
    /// </summary>
    public Guid? PaymentId { get; private set; }
```

And the factory after `Create`:

```csharp
    public static BudgetTransaction CreatePaymentLeg(
        Guid familyId, Guid accountId, decimal amount, DateOnly date,
        string? notes, Guid createdByUserId, Guid paymentId)
    {
        if (amount == 0) throw new DomainException("Transaction amount cannot be zero.");
        if (paymentId == Guid.Empty) throw new DomainException("PaymentId is required.");
        return new BudgetTransaction
        {
            FamilyId = familyId,
            AccountId = accountId,
            CategoryId = null,          // a payment is not spending
            Amount = amount,
            Date = date,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
            PaymentId = paymentId
        };
    }
```

- [ ] **Step 5: Add the EF configuration — SAME commit as the entities**

In `BudgetCategoryConfiguration.Configure`, before the `HasOne` lines:

```csharp
        // menunest-202: one Payment envelope per Credit account. Filtered so the
        // many NULLs on ordinary envelopes do not collide.
        b.HasIndex(x => x.PaymentForAccountId)
            .IsUnique()
            .HasFilter("[PaymentForAccountId] IS NOT NULL");
        b.HasOne<BudgetAccount>()
            .WithMany()
            .HasForeignKey(x => x.PaymentForAccountId)
            .OnDelete(DeleteBehavior.Restrict);
```

In `BudgetTransactionConfiguration.Configure`, after the existing `HasIndex` lines:

```csharp
        // menunest-209: both legs of one payment are found by this.
        b.HasIndex(x => new { x.FamilyId, x.PaymentId });
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeDomainTests`
Expected: **PASS**, 10 tests.

- [ ] **Step 7: Run the FULL backend suite**

Run: `cd backend && dotnet test`
Expected: **PASS**. If EF model validation fails here, the configuration in Step 5 is wrong — fix it before continuing; do not proceed with a red suite.

- [ ] **Step 8: Generate the migration**

Run:
```bash
cd backend && dotnet ef migrations add AddPaymentEnvelopes \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Open the generated `Up()` and confirm it contains exactly: two `AddColumn<Guid>` calls (both `nullable: true`), the filtered unique index on `PaymentForAccountId`, the `(FamilyId, PaymentId)` index, and the two FKs. **If any column is non-nullable, stop** — it would fail against existing prod rows.

- [ ] **Step 9: Preview the SQL that will hit prod**

Run:
```bash
cd backend && dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --output /tmp/menunest-112.sql
```
Read `/tmp/menunest-112.sql`'s tail. Confirm `ALTER TABLE ... ADD [PaymentForAccountId] uniqueidentifier NULL`. Do **not** apply it yet — Task 11 applies it.

- [ ] **Step 10: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/BudgetCategory.cs \
        backend/src/MenuNest.Domain/Entities/BudgetTransaction.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetCategoryConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/BudgetTransactionConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations \
        backend/tests/MenuNest.Application.UnitTests/Budget/Categories/PaymentEnvelopeDomainTests.cs
git commit -m "feat(budget): payment-envelope and payment-pair domain fields (#112)"
```

---

### Task 2: Create the payment envelope and its group

A **Credit** **Account** gets its **Payment envelope** on creation, and existing ones get theirs lazily on first summary read — following menunest-181's precedent for the **Daily allowance** row. Still no arithmetic change: the new envelope computes to 0 under the existing walk.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/PaymentEnvelopeProvisioner.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Program.cs` (DI registration)
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/PaymentEnvelopeProvisionerTests.cs`

**Interfaces:**
- Consumes: `BudgetCategory.CreatePaymentEnvelope(...)` from Task 1.
- Produces:
  - `PaymentEnvelopeProvisioner.EnsureForFamilyAsync(Guid familyId, CancellationToken ct)` → `Task<int>` (count created; caller saves)
  - `PaymentEnvelopeProvisioner.CreditGroupName` → `const string "บัตรเครดิต"`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/PaymentEnvelopeProvisionerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class PaymentEnvelopeProvisionerTests
{
    private static BudgetAccount AddAccount(HandlerTestFixture fx, string name, BudgetAccountType type)
    {
        var acc = BudgetAccount.Create(fx.Family.Id, name, type, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        return acc;
    }

    [Fact]
    public async Task A_credit_account_gets_one_payment_envelope_in_the_credit_group()
    {
        using var fx = new HandlerTestFixture();
        var acc = AddAccount(fx, "KBank", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        var env = await fx.Db.BudgetCategories.SingleAsync(c => c.PaymentForAccountId == acc.Id);
        env.Name.Should().Be("จ่ายบัตร KBank");
        var group = await fx.Db.BudgetCategoryGroups.SingleAsync(g => g.Id == env.GroupId);
        group.Name.Should().Be("บัตรเครดิต");
    }

    [Fact]
    public async Task A_loan_account_gets_none()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "รถ", BudgetAccountType.Loan);
        AddAccount(fx, "เงินสด", BudgetAccountType.Cash);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        (await fx.Db.BudgetCategories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Running_it_twice_creates_nothing_the_second_time()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "KBank", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();
        var sut = new PaymentEnvelopeProvisioner(fx.Db);

        (await sut.EnsureForFamilyAsync(fx.Family.Id, default)).Should().Be(1);
        await fx.Db.SaveChangesAsync();
        (await sut.EnsureForFamilyAsync(fx.Family.Id, default)).Should().Be(0);
        await fx.Db.SaveChangesAsync();

        (await fx.Db.BudgetCategories.CountAsync()).Should().Be(1);
        (await fx.Db.BudgetCategoryGroups.CountAsync(g => g.Name == "บัตรเครดิต")).Should().Be(1);
    }

    [Fact]
    public async Task Two_cards_get_two_envelopes_in_one_shared_group()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "KBank", BudgetAccountType.Credit);
        AddAccount(fx, "SCB", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        var envs = await fx.Db.BudgetCategories.ToListAsync();
        envs.Should().HaveCount(2);
        envs.Select(e => e.GroupId).Distinct().Should().HaveCount(1);
        envs.Select(e => e.Name).Should().BeEquivalentTo("จ่ายบัตร KBank", "จ่ายบัตร SCB");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeProvisionerTests`
Expected: **compile error** — `PaymentEnvelopeProvisioner` does not exist.

- [ ] **Step 3: Write the provisioner**

Create `backend/src/MenuNest.Application/UseCases/Budget/Accounts/PaymentEnvelopeProvisioner.cs`:

```csharp
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
```

- [ ] **Step 4: Call it from CreateAccountHandler**

In `CreateAccountHandler`, add the field and constructor parameter:

```csharp
    private readonly PaymentEnvelopeProvisioner _envelopes;
```

Change the constructor signature to
`public CreateAccountHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateAccountCommand> v, IClock clock, PaymentEnvelopeProvisioner envelopes)`
and assign `_envelopes = envelopes;`.

Then, immediately **before** the existing `await _db.SaveChangesAsync(ct);`:

```csharp
        // menunest-202: a Credit account is never without its Payment envelope.
        // Same unit of work as the account, so the two can never diverge.
        await _envelopes.EnsureForFamilyAsync(familyId, ct);
```

- [ ] **Step 5: Register it for DI**

In `backend/src/MenuNest.WebApi/Program.cs`, beside the existing `AllowanceFreezer` registration, add:

```csharp
builder.Services.AddScoped<PaymentEnvelopeProvisioner>();
```

Add `using MenuNest.Application.UseCases.Budget.Accounts;` if it is not already present.

- [ ] **Step 6: Fix the existing CreateAccountHandler tests**

`CreateAccountHandlerTests` constructs the handler directly and will no longer compile. Add the new argument to every construction:

```csharp
new CreateAccountHandler(fx.Db, fx.UserProvisioner.Object, validator, fx.Clock,
    new PaymentEnvelopeProvisioner(fx.Db))
```

- [ ] **Step 7: Run the full backend suite**

Run: `cd backend && dotnet test`
Expected: **PASS**, including the 4 new provisioner tests.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/PaymentEnvelopeProvisioner.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs \
        backend/src/MenuNest.WebApi/Program.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/PaymentEnvelopeProvisionerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs
git commit -m "feat(budget): create a payment envelope with every credit account (#112)"
```

---

### Task 3: The arithmetic — RTA filter and the derived Available

**This task is atomic and must not be split.** The RTA filter alone makes the app *wrong* (excluding a card's debt without an envelope holding it inflates Ready to Assign by the debt). The two land together or not at all.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/PaymentEnvelopeMath.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/PaymentEnvelopeMathTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/CreditRtaInvariantTests.cs`

**Interfaces:**
- Consumes: `BudgetCategory.PaymentForAccountId` (Task 1), `PaymentEnvelopeProvisioner.EnsureForFamilyAsync` (Task 2).
- Produces:
  - `PaymentEnvelopeMath.AccountTxRow(Guid? CategoryId, decimal Amount)` — readonly record struct
  - `PaymentEnvelopeMath.Available(decimal assigned, IEnumerable<AccountTxRow> accountRows)` → `decimal`
  - `PaymentEnvelopeMath.Shortfall(decimal accountBalance, decimal available)` → `decimal`
  - `PaymentEnvelopeMath.IsDebtType(BudgetAccountType t)` → `bool`

- [ ] **Step 1: Write the failing pure-math test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/PaymentEnvelopeMathTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Enums;
using Row = MenuNest.Application.UseCases.Budget.Monthly.PaymentEnvelopeMath.AccountTxRow;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class PaymentEnvelopeMathTests
{
    private static readonly Guid Food = Guid.NewGuid();

    // Spec §4.2, walked event by event on a card carrying 20,000 of pre-budget debt.
    [Fact]
    public void The_seven_event_walk_from_the_spec()
    {
        var rows = new List<Row>();
        decimal assigned = 0m;
        decimal Available() => PaymentEnvelopeMath.Available(assigned, rows);

        rows.Add(new Row(null, -20_000m));                 // opening balance
        Available().Should().Be(0m, "pre-budget debt funds nothing");

        rows.Add(new Row(Food, -500m));                    // buy food on the card
        Available().Should().Be(500m);

        rows.Add(new Row(Food, 500m));                     // shop refunds it
        Available().Should().Be(0m);

        rows.Add(new Row(Food, -500m));                    // buy food again
        Available().Should().Be(500m);

        rows.Add(new Row(null, -300m));                    // cash advance, no envelope
        Available().Should().Be(500m, "an uncategorised outflow is unfunded debt");

        rows.Add(new Row(null, 500m));                     // pay 500
        Available().Should().Be(0m);

        assigned = 2_000m;                                 // assign toward the old debt
        Available().Should().Be(2_000m);
    }

    [Fact]
    public void A_hand_written_payment_from_before_this_feature_still_subtracts()
    {
        // No PaymentId anywhere — the maths never reads one (spec §3).
        PaymentEnvelopeMath.Available(0m, new[] { new Row(Food, -500m), new Row(null, 500m) })
            .Should().Be(0m);
    }

    [Theory]
    [InlineData(-500, 500, 0)]        // funded exactly
    [InlineData(-20_500, 500, 20_000)] // 20,000 short
    [InlineData(-500, 900, 0)]        // over-funded never goes negative
    [InlineData(0, 0, 0)]             // settled
    public void Shortfall_floors_at_zero(decimal balance, decimal available, decimal expected)
    {
        PaymentEnvelopeMath.Shortfall(balance, available).Should().Be(expected);
    }

    [Theory]
    [InlineData(BudgetAccountType.Credit, true)]
    [InlineData(BudgetAccountType.Loan, true)]
    [InlineData(BudgetAccountType.Cash, false)]
    [InlineData(BudgetAccountType.Closed, false)]
    public void Debt_types_are_credit_and_loan_only(BudgetAccountType t, bool expected)
    {
        PaymentEnvelopeMath.IsDebtType(t).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeMathTests`
Expected: **compile error** — `PaymentEnvelopeMath` does not exist.

- [ ] **Step 3: Write the pure maths**

Create `backend/src/MenuNest.Application/UseCases/Budget/Monthly/PaymentEnvelopeMath.cs`:

```csharp
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Monthly;

/// <summary>
/// The whole of issue #112's arithmetic, kept pure so it is testable without a
/// DbContext (spec §4.2–§4.4). Nothing here reads PaymentId: pairing is for
/// finding and deleting a payment, never for computing one — which is why
/// payments hand-written before this feature shipped still subtract correctly.
/// </summary>
public static class PaymentEnvelopeMath
{
    public readonly record struct AccountTxRow(Guid? CategoryId, decimal Amount);

    /// <summary>Credit and Loan leave Ready to Assign (menunest-203, menunest-206).</summary>
    public static bool IsDebtType(BudgetAccountType t) =>
        t is BudgetAccountType.Credit or BudgetAccountType.Loan;

    /// <summary>
    /// Available = assigned − Σ(categorised rows) − Σ(uncategorised POSITIVE rows).
    /// Both minuses are correct: a categorised outflow is negative, so subtracting
    /// it adds. <paramref name="accountRows"/> is every transaction on the Credit
    /// account up to the end of the month being viewed.
    /// </summary>
    public static decimal Available(decimal assigned, IEnumerable<AccountTxRow> accountRows)
    {
        decimal categorised = 0m, uncategorisedInflow = 0m;
        foreach (var r in accountRows)
        {
            if (r.CategoryId.HasValue) categorised += r.Amount;
            else if (r.Amount > 0m) uncategorisedInflow += r.Amount;
        }
        return assigned - categorised - uncategorisedInflow;
    }

    /// <summary>What is still owed and not yet funded. Floors at 0 (spec §4.3).</summary>
    public static decimal Shortfall(decimal accountBalance, decimal available) =>
        Math.Max(0m, -accountBalance - available);
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeMathTests`
Expected: **PASS**, 10 cases.

- [ ] **Step 5: Write the failing invariant test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/CreditRtaInvariantTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

/// <summary>
/// Spec §4.4 — the acceptance test. No activity on a Credit account may change
/// Ready to Assign. Not one case, the payment included. If any of these comes
/// out non-zero the model is wrong.
/// </summary>
public class CreditRtaInvariantTests
{
    private const string Bkk = "Asia/Bangkok";
    private static readonly DateOnly D = new(2026, 1, 15);

    private sealed record World(HandlerTestFixture Fx, Guid CashId, Guid CardId, Guid FoodId);

    private static World Seed()
    {
        var fx = new HandlerTestFixture();          // Clock is 2026-01-01 UTC
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
        var card = BudgetAccount.Create(fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(cash, card);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "ค่ากิน", 0);
        var food = BudgetCategory.Create(fx.Family.Id, group.Id, "อาหาร", "🍜", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        fx.Db.BudgetCategories.Add(food);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, cash.Id, null, 10_000m, D, "Opening balance", fx.User.Id));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(
            fx.Family.Id, food.Id, 2026, 1, 3_000m));
        fx.Db.SaveChanges();
        return new World(fx, cash.Id, card.Id, food.Id);
    }

    private static async Task<decimal> RtaAsync(World w)
    {
        await new PaymentEnvelopeProvisioner(w.Fx.Db).EnsureForFamilyAsync(w.Fx.Family.Id, default);
        await w.Fx.Db.SaveChangesAsync();
        var handler = new GetMonthlySummaryHandler(
            w.Fx.Db, w.Fx.UserProvisioner.Object, new AllowanceFreezer(w.Fx.Db), w.Fx.Clock);
        var s = await handler.Handle(new GetMonthlySummaryQuery(2026, 1, Bkk), default);
        return s.ReadyToAssign;
    }

    private static void AddTx(World w, Guid accountId, Guid? categoryId, decimal amount)
    {
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, accountId, categoryId, amount, D, null, w.Fx.User.Id));
        w.Fx.Db.SaveChanges();
    }

    [Fact]
    public async Task Baseline_is_seven_thousand()
    {
        var w = Seed(); using var _ = w.Fx;
        (await RtaAsync(w)).Should().Be(7_000m);
    }

    [Fact]
    public async Task A_categorised_card_purchase_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, w.FoodId, -500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task A_categorised_refund_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, w.FoodId, 500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task An_uncategorised_card_purchase_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, null, -500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task A_cards_opening_debt_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, null, -20_000m);
        (await RtaAsync(w)).Should().Be(before, "pre-budget debt sits outside the budget");
    }

    [Fact]
    public async Task A_payment_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var before = await RtaAsync(w);
        var payId = Guid.NewGuid();
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -500m, D, null, w.Fx.User.Id, payId),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 500m, D, null, w.Fx.User.Id, payId));
        w.Fx.Db.SaveChanges();
        (await RtaAsync(w)).Should().Be(before, "you spend money you had already set aside");
    }

    [Fact]
    public async Task A_loan_is_out_of_it_too()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        w.Fx.Db.SaveChanges();
        AddTx(w, loan.Id, null, -300_000m);
        (await RtaAsync(w)).Should().Be(before, "menunest-206 — not ตั้งงบเกิน -293,000");
    }

    [Fact]
    public async Task The_payment_envelope_tracks_the_card()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        await RtaAsync(w);

        var handler = new GetMonthlySummaryHandler(
            w.Fx.Db, w.Fx.UserProvisioner.Object, new AllowanceFreezer(w.Fx.Db), w.Fx.Clock);
        var s = await handler.Handle(new GetMonthlySummaryQuery(2026, 1, Bkk), default);

        var env = s.Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == w.CardId);
        env.Available.Should().Be(500m);
        s.Accounts.Single(a => a.Id == w.CardId).Balance.Should().Be(-500m);
    }
}
```

> If `MonthlyAssignment.Create` has a different signature, read
> `backend/src/MenuNest.Domain/Entities/MonthlyAssignment.cs` and adjust the one
> call — the rest of the test is unaffected.

- [ ] **Step 6: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CreditRtaInvariantTests`
Expected: **FAIL**. `A_categorised_card_purchase_does_not_move_it` fails because the payment envelope computes 0 under the existing walk; `A_loan_is_out_of_it_too` fails with a 300,000 swing.

- [ ] **Step 7: Change GetMonthlySummaryHandler**

Add to the constructor a `PaymentEnvelopeProvisioner _envelopes` (same pattern as `AllowanceFreezer`), and call it first thing after resolving the family, so accounts predating this feature get their envelope lazily:

```csharp
        // menunest-202 / menunest-181's precedent: provision lazily on read, so
        // Credit accounts that predate this feature gain their envelope on the
        // first page load rather than needing a data backfill.
        if (await _envelopes.EnsureForFamilyAsync(familyId, ct) > 0)
            await _db.SaveChangesAsync(ct);
```

Load the account types alongside the ids, and index the card rows. Replace the
`accountIds` / `totalAccountBalance` block with:

```csharp
        var accountRows = await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);

        // menunest-203 / menunest-206: Credit and Loan leave Ready to Assign.
        // Their debt is held by a Payment envelope (cards) or by an ordinary
        // Envelope the User made (loans) — counting the negative balance as well
        // would hold the same money back twice.
        var totalAccountBalance = accountRows
            .Where(a => !PaymentEnvelopeMath.IsDebtType(a.Type))
            .Sum(a => DerivedBalance(a.Id));

        // Every row on a Credit account, for the payment-envelope derivation (§4.2).
        var creditIds = accountRows
            .Where(a => a.Type == BudgetAccountType.Credit).Select(a => a.Id).ToHashSet();
        var creditRowsByAccount = (await _db.BudgetTransactions
                .Where(t => t.FamilyId == familyId && t.Date < nextMonth
                         && creditIds.Contains(t.AccountId))
                .Select(t => new { t.AccountId, t.CategoryId, t.Amount })
                .ToListAsync(ct))
            .GroupBy(t => t.AccountId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PaymentEnvelopeMath.AccountTxRow>)g
                    .Select(t => new PaymentEnvelopeMath.AccountTxRow(t.CategoryId, t.Amount))
                    .ToList());
```

Add a local function beside `DerivedBalance` that computes either kind of envelope:

```csharp
        // A Payment envelope is derived from its card's rows (menunest-208), not
        // from the assignment-plus-activity walk every other Envelope uses.
        (decimal Available, decimal Assigned, decimal Activity) EnvelopeNumbers(
            Domain.Entities.BudgetCategory cat)
        {
            var catAssignments = allAssignments.Where(a => a.CategoryId == cat.Id).ToList();
            if (cat.PaymentForAccountId is not { } accId)
            {
                var catTx = allTx.Where(t => t.CategoryId == cat.Id).ToList();
                return ComputeEnvelopeAvailable(catAssignments, catTx, q.Year, q.Month);
            }

            var assignedToDate = catAssignments.Sum(a => a.AssignedAmount);
            var rows = creditRowsByAccount.TryGetValue(accId, out var r)
                ? r : Array.Empty<PaymentEnvelopeMath.AccountTxRow>();
            var available = PaymentEnvelopeMath.Available(assignedToDate, rows);
            var assignedThis = catAssignments
                .FirstOrDefault(a => a.Year == q.Year && a.Month == q.Month)?.AssignedAmount ?? 0m;
            // Activity on a Payment envelope is money that left it — payments made
            // this month, shown positive-out like any other envelope's spending.
            var activityThis = -(allTxAll
                .Where(t => t.AccountId == accId && t.CategoryId == null && t.Amount > 0m
                         && t.Date.Year == q.Year && t.Date.Month == q.Month)
                .Sum(t => t.Amount));
            return (available, assignedThis, activityThis);
        }
```

> `allTxAll` is a new materialised list of `{AccountId, CategoryId, Amount, Date}`
> for the family up to `nextMonth`. Add it beside `allTx`; `allTx` keeps its
> `CategoryId != null` filter for the ordinary walk.

Then, in **both** loops that currently call `ComputeEnvelopeAvailable` — the per-group
loop and the `totalEnvelopeAvailableAllCats` loop — call `EnvelopeNumbers(cat)` instead.
The `totalEnvelopeAvailableAllCats` loop must include payment envelopes: that is what
keeps Ready to Assign correct.

- [ ] **Step 8: Run the invariant tests**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CreditRtaInvariantTests`
Expected: **PASS**, 8 tests. Every RTA delta is zero.

- [ ] **Step 9: Run the FULL backend suite**

Run: `cd backend && dotnet test`
Expected: **PASS**. `GetMonthlySummaryHandlerTests` and `GetMonthlySummaryDerivedBalanceTests` construct the handler directly — add the `new PaymentEnvelopeProvisioner(fx.Db)` argument to each. Those fixtures use Cash accounts only, so their expected numbers do not change.

- [ ] **Step 10: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Monthly/PaymentEnvelopeMath.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly
git commit -m "feat(budget): derive the payment envelope and drop debt accounts from Ready to Assign (#112)"
```

---

### Task 4: Shortfall and the DTO extensions

Display only — the number that answers issue #112's question.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/PaymentShortfallTests.cs`

**Interfaces:**
- Consumes: `PaymentEnvelopeMath.Shortfall` (Task 3).
- Produces: `EnvelopeDto.PaymentForAccountId` → `Guid?`; `EnvelopeDto.Shortfall` → `decimal?`; `BudgetAccountDto.Shortfall` → `decimal?`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/PaymentShortfallTests.cs`. Reuse the `Seed`/`AddTx`/`RtaAsync` helpers from `CreditRtaInvariantTests` by copying them into this file (they are private; duplicating four short helpers is cheaper than a shared base class).

```csharp
    [Fact]
    public async Task A_fully_funded_card_has_no_shortfall()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var s = await SummaryAsync(w);

        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId);
        env.Shortfall.Should().Be(0m);
        s.Accounts.Single(a => a.Id == w.CardId).Shortfall.Should().Be(0m);
    }

    [Fact]
    public async Task Pre_budget_debt_is_the_shortfall()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, null, -20_000m);   // old debt
        AddTx(w, w.CardId, w.FoodId, -500m);  // this month, funded
        var s = await SummaryAsync(w);

        s.Accounts.Single(a => a.Id == w.CardId).Shortfall.Should().Be(20_000m);
    }

    [Fact]
    public async Task A_cash_account_has_no_shortfall_at_all()
    {
        var w = Seed(); using var _ = w.Fx;
        var s = await SummaryAsync(w);
        s.Accounts.Single(a => a.Id == w.CashId).Shortfall.Should().BeNull();
    }

    [Fact]
    public async Task A_loan_has_no_shortfall_because_it_has_no_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        w.Fx.Db.SaveChanges();
        AddTx(w, loan.Id, null, -300_000m);

        var s = await SummaryAsync(w);
        s.Accounts.Single(a => a.Id == loan.Id).Shortfall
            .Should().BeNull("menunest-206 — a loan must not read ขาดอีก 300,000 forever");
    }

    [Fact]
    public async Task An_ordinary_envelope_has_no_payment_fields()
    {
        var w = Seed(); using var _ = w.Fx;
        var s = await SummaryAsync(w);
        var food = s.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.FoodId);
        food.PaymentForAccountId.Should().BeNull();
        food.Shortfall.Should().BeNull();
    }
```

`SummaryAsync` is `RtaAsync` returning the whole `MonthlySummaryDto` instead of `ReadyToAssign`.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentShortfallTests`
Expected: **compile error** — `Shortfall` and `PaymentForAccountId` are not members of the DTOs.

- [ ] **Step 3: Extend the DTOs**

In `BudgetDtos.cs`, append to `BudgetAccountDto` (trailing, so existing positional
constructions keep compiling):

```csharp
public sealed record BudgetAccountDto(
    Guid Id, string Name, BudgetAccountType Type, decimal Balance, int SortOrder, bool IsClosed,
    // menunest-202: what is still owed and not yet funded. NULL on anything but a
    // Credit account — a Loan has no Payment envelope (menunest-206), so it must
    // never read "ขาดอีก" for its whole outstanding balance.
    decimal? Shortfall = null);
```

And to `EnvelopeDto`:

```csharp
    bool IsEveryday,
    Guid? PaymentForAccountId = null,   // non-null ⇒ this is a Payment envelope
    decimal? Shortfall = null);         // §4.3, non-null only on a Payment envelope
```

- [ ] **Step 4: Populate them**

In `GetMonthlySummaryHandler`, where `EnvelopeDto` is built, append:

```csharp
                    cat.IsEveryday,
                    cat.PaymentForAccountId,
                    cat.PaymentForAccountId is { } payAcc
                        ? PaymentEnvelopeMath.Shortfall(DerivedBalance(payAcc), available)
                        : null));
```

Where `BudgetAccountDto` is built:

```csharp
        var shortfallByAccount = accountRows
            .Where(a => a.Type == BudgetAccountType.Credit)
            .ToDictionary(a => a.Id, a => PaymentEnvelopeMath.Shortfall(
                DerivedBalance(a.Id), availableByPaymentEnvelope.GetValueOrDefault(a.Id)));

        var accounts = accountRows
            .Select(a => new BudgetAccountDto(
                a.Id, a.Name, a.Type, DerivedBalance(a.Id), a.SortOrder, a.IsClosed,
                shortfallByAccount.TryGetValue(a.Id, out var sf) ? sf : null))
            .ToList();
```

`availableByPaymentEnvelope` is a `Dictionary<Guid, decimal>` you fill in the
`totalEnvelopeAvailableAllCats` loop from Task 3: when `cat.PaymentForAccountId` is
non-null, record `[accId] = available`.

In `ListAccountsHandler`, pass `null` for `Shortfall` and add a comment saying the
funded figure needs the month context that only the summary has.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentShortfallTests`
Expected: **PASS**, 5 tests.

- [ ] **Step 6: Run the full backend suite and commit**

Run: `cd backend && dotnet test` → **PASS**.

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Monthly/PaymentShortfallTests.cs
git commit -m "feat(budget): expose the payment-envelope shortfall on the summary (#112)"
```

---

### Task 5: The menunest-205 refusals at the handler layer

The domain already throws (Task 1). This makes the handlers return the domain error rather than an EF failure, and closes the group-delete side door. One guard serves the SPA and MCP both, because they share these handlers.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Categories/DeleteCategory/DeleteCategoryHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Categories/SetEverydayMarks/SetEverydayMarksHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Groups/DeleteGroup/DeleteGroupHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/PaymentEnvelopeGuardTests.cs`

**Interfaces:**
- Consumes: `BudgetCategory.IsPaymentEnvelope` (Task 1).
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

Create `PaymentEnvelopeGuardTests.cs` with four tests, each seeding one Credit account
and running the provisioner, then asserting `DomainException`:

```csharp
    [Fact]
    public async Task Deleting_a_payment_envelope_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var act = async () => await new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteCategoryCommand(envId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment envelope*");
    }

    [Fact]
    public async Task Marking_a_payment_envelope_everyday_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var act = async () => await NewSetEverydayMarksHandler(fx)
            .Handle(new SetEverydayMarksCommand(new[] { envId }, Array.Empty<Guid>(), "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*everyday*");
    }

    [Fact]
    public async Task Deleting_the_credit_group_while_it_holds_an_envelope_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var groupId = fx.Db.BudgetCategories.Single(c => c.Id == envId).GroupId;
        var act = async () => await new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteGroupCommand(groupId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment envelope*");
    }

    [Fact]
    public async Task An_ordinary_envelope_still_deletes()
    {
        // regression guard — the new checks must not catch ordinary envelopes
    }
```

> Match `SetEverydayMarksCommand`'s real signature by reading
> `SetEverydayMarksCommand.cs` first; adjust the one construction if it differs.

- [ ] **Step 2: Run to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeGuardTests`
Expected: **FAIL** — delete succeeds, everyday-mark succeeds, group-delete succeeds.

- [ ] **Step 3: Guard DeleteCategoryHandler**

Immediately after the category is loaded:

```csharp
        // menunest-205: it can hold money against a live debt.
        if (cat.IsPaymentEnvelope)
            throw new DomainException(
                "A payment envelope cannot be deleted — close its account instead.");
```

- [ ] **Step 4: Guard SetEverydayMarksHandler**

The bulk mark path must not reach `MarkEveryday(true)` with a payment envelope. Before applying marks:

```csharp
        // menunest-205: a payment envelope in the Everyday pot would raise the
        // Daily allowance every time the card is used.
        if (toMark.Any(c => c.IsPaymentEnvelope))
            throw new DomainException(
                "A payment envelope cannot be an everyday envelope.");
```

- [ ] **Step 5: Guard DeleteGroupHandler**

After the group is loaded:

```csharp
        var holdsPaymentEnvelope = await _db.BudgetCategories
            .AnyAsync(c => c.GroupId == c2.Id && c.PaymentForAccountId != null, ct);
        if (holdsPaymentEnvelope)
            throw new DomainException(
                "This group holds a payment envelope and cannot be deleted.");
```

- [ ] **Step 6: Run to verify they pass, then the full suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentEnvelopeGuardTests` → **PASS**
Run: `cd backend && dotnet test` → **PASS**

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Categories/DeleteCategory/DeleteCategoryHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Categories/SetEverydayMarks/SetEverydayMarksHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Groups/DeleteGroup/DeleteGroupHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Categories/PaymentEnvelopeGuardTests.cs
git commit -m "feat(budget): refuse rename, delete, hide and everyday on a payment envelope (#112)"
```

---

### Task 6: Make a payment

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Payments/MakePayment/MakePaymentCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Payments/MakePayment/MakePaymentHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Payments/MakePayment/MakePaymentValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs` (Income)
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Payments/MakePaymentHandlerTests.cs`

**Interfaces:**
- Consumes: `BudgetTransaction.CreatePaymentLeg` (Task 1), `PaymentEnvelopeMath` (Task 3).
- Produces:
  - `MakePaymentCommand(Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly? Date, string? Notes, string? TimeZoneId) : ICommand<PaymentDto>`
  - `PaymentDto(Guid PaymentId, Guid FromAccountId, string FromAccountName, Guid ToAccountId, string ToAccountName, decimal Amount, DateOnly Date, string? Notes)`
  - `MakePaymentRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly? Date, string? Notes, string? TimeZoneId)`
  - `POST /api/budget/payments`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task It_writes_both_legs_with_one_shared_PaymentId()
    {
        var w = Seed(); using var _ = w.Fx;
        var dto = await Handler(w).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, 500m, new DateOnly(2026, 1, 20), null, "Asia/Bangkok"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        legs.Single(l => l.AccountId == w.CashId).Amount.Should().Be(-500m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(500m);
        legs.Should().OnlyContain(l => l.CategoryId == null);
    }

    [Fact]
    public async Task It_spends_down_the_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        await Handler(w).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);

        var s = await SummaryAsync(w);
        s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId)
            .Available.Should().Be(0m);
        s.Accounts.Single(a => a.Id == w.CardId).Balance.Should().Be(0m);
    }

    // menunest-204: without this, paying your own card reports as money arriving.
    [Fact]
    public async Task A_payment_is_never_counted_as_Income()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = (await SummaryAsync(w)).Income;
        await Handler(w).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);
        (await SummaryAsync(w)).Income.Should().Be(before);
    }

    [Fact]
    public async Task Paying_a_loan_works_the_same_way()
    { /* seed a Loan account; assert both legs and that RTA is unchanged */ }

    [Fact]
    public async Task Paying_INTO_a_cash_account_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w).Handle(
            new MakePaymentCommand(w.CardId, w.CashId, 500m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Credit or Loan*");
    }

    [Fact]
    public async Task Paying_an_account_from_itself_is_refused() { /* ValidationException */ }

    [Fact]
    public async Task A_zero_or_negative_amount_is_refused() { /* ValidationException */ }
```

- [ ] **Step 2: Run to verify they fail** — compile error, `MakePaymentCommand` does not exist.

- [ ] **Step 3: Write the command, DTO and request**

```csharp
namespace MenuNest.Application.UseCases.Budget.Payments.MakePayment;

/// <summary>
/// menunest-204 / menunest-207: pays down a Credit or Loan account. Writes BOTH
/// legs in one unit of work — there is no moment at which half a payment exists.
/// Date defaults to the viewer's local today (menunest-189).
/// </summary>
public sealed record MakePaymentCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount,
    DateOnly? Date, string? Notes, string? TimeZoneId) : ICommand<PaymentDto>;
```

In `BudgetDtos.cs`:

```csharp
// ---------- Payments (menunest-204, menunest-207) ----------
public sealed record PaymentDto(
    Guid PaymentId,
    Guid FromAccountId, string FromAccountName,
    Guid ToAccountId, string ToAccountName,
    decimal Amount, DateOnly Date, string? Notes);

public sealed record MakePaymentRequest(
    Guid FromAccountId, Guid ToAccountId, decimal Amount,
    DateOnly? Date, string? Notes, string? TimeZoneId);
```

- [ ] **Step 4: Write the validator**

```csharp
public sealed class MakePaymentValidator : AbstractValidator<MakePaymentCommand>
{
    public MakePaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m)
            .WithMessage("Payment amount must be positive.");
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.ToAccountId).NotEmpty()
            .NotEqual(x => x.FromAccountId)
            .WithMessage("An account cannot pay itself.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
public sealed class MakePaymentHandler : ICommandHandler<MakePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<MakePaymentCommand> _v;
    private readonly IClock _clock;

    public MakePaymentHandler(IApplicationDbContext db, IUserProvisioner users,
        IValidator<MakePaymentCommand> v, IClock clock)
    { _db = db; _users = users; _v = v; _clock = clock; }

    public async ValueTask<PaymentDto> Handle(MakePaymentCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var from = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.FromAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Paying account not found.");
        var to = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.ToAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account being paid not found.");

        // menunest-207: only a debt account is ever paid. Paying a Cash account
        // would be a transfer, which MenuNest deliberately does not have.
        if (!PaymentEnvelopeMath.IsDebtType(to.Type))
            throw new DomainException("Only a Credit or Loan account can be paid.");

        var tz = BudgetTimeZone.Resolve(c.TimeZoneId);
        var date = c.Date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

        var paymentId = Guid.NewGuid();
        var outLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, from.Id, -c.Amount, date, c.Notes, user.Id, paymentId);
        var inLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, to.Id, c.Amount, date, c.Notes, user.Id, paymentId);

        _db.BudgetTransactions.AddRange(outLeg, inLeg);
        from.AdjustBalance(-c.Amount);   // keep the cached copies true
        to.AdjustBalance(c.Amount);
        await _db.SaveChangesAsync(ct);  // ONE unit of work — never half a pair

        return new PaymentDto(paymentId, from.Id, from.Name, to.Id, to.Name,
            c.Amount, date, outLeg.Notes);
    }
}
```

- [ ] **Step 6: Exclude payments from Income**

In `GetMonthlySummaryHandler`, add `&& t.PaymentId == null` to the `income` query:

```csharp
        var income = await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId
                     && t.CategoryId == null
                     && t.PaymentId == null      // menunest-204: paying your own card is not income
                     && t.Amount > 0m
                     && t.Date >= selected && t.Date < nextMonth)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
```

- [ ] **Step 7: Add the route**

In `BudgetController`:

```csharp
    // ----- payments (menunest-204, menunest-207) -----
    [HttpPost("payments")]
    public async Task<ActionResult<PaymentDto>> MakePayment(
        [FromBody] MakePaymentRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new MakePaymentCommand(
            r.FromAccountId, r.ToAccountId, r.Amount, r.Date, r.Notes, r.TimeZoneId), ct));
```

Add the `using MenuNest.Application.UseCases.Budget.Payments.MakePayment;` line.

- [ ] **Step 8: Run the tests, then the full suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~MakePaymentHandlerTests` → **PASS**, 7 tests.
Run: `cd backend && dotnet test` → **PASS**.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Payments \
        backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Payments
git commit -m "feat(budget): pay a credit card or loan as one paired action (#112)"
```

---

### Task 7: Edit and delete a payment as one row

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Payments/UpdatePayment/` (command, handler, validator)
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Payments/DeletePayment/` (command, handler)
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Transactions/DeleteTransaction/DeleteTransactionHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Transactions/UpdateTransaction/UpdateTransactionHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Payments/PaymentPairingTests.cs`

**Interfaces:**
- Consumes: `MakePaymentCommand` / `PaymentDto` (Task 6).
- Produces:
  - `UpdatePaymentCommand(Guid PaymentId, Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly Date, string? Notes) : ICommand<PaymentDto>`
  - `DeletePaymentCommand(Guid PaymentId) : ICommand<Unit>`
  - `PUT /api/budget/payments/{paymentId}` · `DELETE /api/budget/payments/{paymentId}`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Deleting_a_payment_removes_both_legs()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        await new DeletePaymentHandler(w.Fx.Db, w.Fx.UserProvisioner.Object)
            .Handle(new DeletePaymentCommand(p.PaymentId), default);
        w.Fx.Db.BudgetTransactions.Count(t => t.PaymentId == p.PaymentId).Should().Be(0);
    }

    // menunest-209: reaching a single half is exactly the state that leaves the
    // budget silently wrong.
    [Fact]
    public async Task Deleting_ONE_leg_through_the_transaction_handler_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        var legId = w.Fx.Db.BudgetTransactions.First(t => t.PaymentId == p.PaymentId).Id;

        var act = async () => await new DeleteTransactionHandler(w.Fx.Db, w.Fx.UserProvisioner.Object)
            .Handle(new DeleteTransactionCommand(legId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment*");
    }

    [Fact]
    public async Task Editing_ONE_leg_through_the_transaction_handler_is_refused() { /* same shape */ }

    [Fact]
    public async Task Editing_a_payment_moves_both_legs_together()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        await new UpdatePaymentHandler(...).Handle(new UpdatePaymentCommand(
            p.PaymentId, w.CashId, w.CardId, 300m, new DateOnly(2026, 1, 25), "แก้ยอด"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == p.PaymentId).ToList();
        legs.Single(l => l.AccountId == w.CashId).Amount.Should().Be(-300m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(300m);
        legs.Should().OnlyContain(l => l.Date == new DateOnly(2026, 1, 25));
    }

    [Fact]
    public async Task Deleting_a_payment_restores_the_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var p = await MakePayment(w, 500m);
        (await SummaryAsync(w)).Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == w.CardId).Available.Should().Be(0m);

        await new DeletePaymentHandler(w.Fx.Db, w.Fx.UserProvisioner.Object)
            .Handle(new DeletePaymentCommand(p.PaymentId), default);

        (await SummaryAsync(w)).Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == w.CardId).Available.Should().Be(500m);
    }

    [Fact]
    public async Task An_ordinary_transaction_still_deletes_and_edits_normally() { /* regression */ }
```

- [ ] **Step 2: Run to verify they fail** — compile error, the handlers do not exist.

- [ ] **Step 3: Write DeletePaymentHandler**

Load both legs by `PaymentId` + `FamilyId`, throw `DomainException("Payment not found.")` if empty, `AdjustBalance(-leg.Amount)` on each leg's account, `RemoveRange(legs)`, one `SaveChangesAsync`.

- [ ] **Step 4: Write UpdatePaymentHandler**

Load both legs. Re-resolve both accounts (they may have changed). Guard `IsDebtType(to.Type)` exactly as Task 6 does. Reverse both old balances, rewrite both legs via `BudgetTransaction.Update(...)`, apply both new balances, one `SaveChangesAsync`.

- [ ] **Step 5: Close the single-leg side doors**

In `DeleteTransactionHandler`, after the transaction is loaded:

```csharp
        // menunest-209: a payment is ONE row to the User. Deleting one leg would
        // leave the debt paid in the budget and unpaid on the card.
        if (tx.PaymentId is not null)
            throw new DomainException(
                "This is a payment — delete it from the payment, not one side of it.");
```

Add the identical guard to `UpdateTransactionHandler`.

- [ ] **Step 6: Add the two routes**

```csharp
    [HttpPut("payments/{paymentId:guid}")]
    public async Task<ActionResult<PaymentDto>> UpdatePayment(
        Guid paymentId, [FromBody] UpdatePaymentRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new UpdatePaymentCommand(
            paymentId, r.FromAccountId, r.ToAccountId, r.Amount, r.Date, r.Notes), ct));

    [HttpDelete("payments/{paymentId:guid}")]
    public async Task<IActionResult> DeletePayment(Guid paymentId, CancellationToken ct)
    {
        await _m.Send(new DeletePaymentCommand(paymentId), ct);
        return NoContent();
    }
```

Add `UpdatePaymentRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly Date, string? Notes)` to `BudgetDtos.cs`.

- [ ] **Step 7: Run the tests, then the full suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~PaymentPairingTests` → **PASS**, 6 tests.
Run: `cd backend && dotnet test` → **PASS**.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Payments \
        backend/src/MenuNest.Application/UseCases/Budget/Transactions \
        backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs \
        backend/src/MenuNest.WebApi/Controllers/BudgetController.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Payments
git commit -m "feat(budget): edit and delete a payment as one row, never one leg (#112)"
```

---

### Task 8: Account lifecycle — rename cascade, close-while-owing

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/UpdateAccount/UpdateAccountHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreditAccountLifecycleTests.cs`

**Interfaces:**
- Consumes: `BudgetCategory.RenameForAccount`, `SetHiddenForAccountClosure` (Task 1).
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Renaming_the_card_renames_its_payment_envelope()
    {
        // rename KBank → "KBank Platinum"; envelope becomes "จ่ายบัตร KBank Platinum"
    }

    [Fact]
    public async Task Closing_a_card_that_still_owes_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var act = async () => await UpdateAccount(w, w.CardId, "KBank", isClosed: true);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*ยังจ่ายบัตรไม่ครบ*");
    }

    [Fact]
    public async Task Closing_a_settled_card_is_allowed()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        await MakePayment(w, 500m);
        await UpdateAccount(w, w.CardId, "KBank", isClosed: true);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).IsClosed.Should().BeTrue();
    }

    // menunest-210's correction: totalEnvelopeAvailableAllCats walks HIDDEN
    // categories too, so hiding alone would leave the remainder locked.
    [Fact]
    public async Task Closing_a_settled_card_returns_its_leftover_money_to_Ready_to_Assign()
    {
        var w = Seed(); using var _ = w.Fx;
        await AssignTo(w, PaymentEnvelopeId(w), 1_000m);   // over-fund it
        var whileOpen = (await SummaryAsync(w)).ReadyToAssign;

        await UpdateAccount(w, w.CardId, "KBank", isClosed: true);

        (await SummaryAsync(w)).ReadyToAssign.Should().Be(whileOpen + 1_000m);
    }

    [Fact]
    public async Task Reopening_the_card_takes_the_money_back_out()
    {
        // the MonthlyAssignment rows are untouched, so this is exactly reversible
    }

    [Fact]
    public async Task Closing_a_cash_account_is_unaffected() { /* regression */ }
```

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Change UpdateAccountHandler**

Replace the body between loading `acc` and `SaveChangesAsync`:

```csharp
        var envelope = await _db.BudgetCategories
            .FirstOrDefaultAsync(c => c.PaymentForAccountId == acc.Id, ct);

        acc.Rename(c.Name);
        acc.SetSortOrder(c.SortOrder);
        // menunest-212: the envelope's name follows its Account, always.
        envelope?.RenameForAccount(c.Name);

        if (c.IsClosed && !acc.IsClosed)
        {
            // menunest-210: a card you still owe money on is not closed in life either.
            if (PaymentEnvelopeMath.IsDebtType(acc.Type))
            {
                var balance = await _db.BudgetTransactions
                    .Where(t => t.AccountId == acc.Id)
                    .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
                if (balance != 0m)
                    throw new DomainException("ยังจ่ายบัตรไม่ครบ — ปิดบัญชีไม่ได้");
            }
            acc.Close();
            envelope?.SetHiddenForAccountClosure(true);
        }
        if (!c.IsClosed && acc.IsClosed)
        {
            acc.Reopen();
            envelope?.SetHiddenForAccountClosure(false);
        }
```

- [ ] **Step 4: Exclude a closed card's envelope from the envelope total**

In `GetMonthlySummaryHandler`'s `totalEnvelopeAvailableAllCats` loop, skip payment
envelopes whose account is closed:

```csharp
        var closedAccountIds = accountRows.Where(a => a.IsClosed).Select(a => a.Id).ToHashSet();
        foreach (var cat in categories)
        {
            // menunest-210: a closed card's envelope leaves the total, which is what
            // returns any over-funded remainder to Ready to Assign. Its
            // MonthlyAssignment rows stay as history, so reopening is exact.
            if (cat.PaymentForAccountId is { } pa && closedAccountIds.Contains(pa)) continue;
            var (available, _, _) = EnvelopeNumbers(cat);
            totalEnvelopeAvailableAllCats += available;
        }
```

- [ ] **Step 5: Run the tests, then the full suite** → **PASS** both.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/UpdateAccount/UpdateAccountHandler.cs \
        backend/src/MenuNest.Application/UseCases/Budget/Monthly/GetMonthlySummary/GetMonthlySummaryHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreditAccountLifecycleTests.cs
git commit -m "feat(budget): cascade card renames and refuse closing a card that still owes (#112)"
```

---

### Task 9: MCP tools

menunest-213 — every function this feature adds is reachable over MCP.

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/BudgetTools.cs`
- Test: `backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetPaymentToolsTests.cs`

**Interfaces:**
- Consumes: `MakePaymentCommand`, `UpdatePaymentCommand`, `DeletePaymentCommand` (Tasks 6–7).
- Produces: `pay_account`, `update_payment`, `delete_payment`.

- [ ] **Step 1: Write the failing tests**

Follow `BudgetToolsTests`' existing shape (a mocked `IMediator`, asserting the command
the tool sends). Three tests: each tool forwards its arguments verbatim to the right
command type.

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Add the three tools**

```csharp
    [McpServerTool, Description(
        "Pay down a credit card or loan. Writes BOTH sides as one paired payment — "
        + "an outflow on the paying account and an inflow on the debt — and spends "
        + "down that card's payment envelope. This is the ONLY correct way to pay: "
        + "two create_transaction calls would leave the halves unlinked and the "
        + "inflow would be counted as income. `toAccountId` must be a Credit or "
        + "Loan account.")]
    public async Task<PaymentDto> pay_account(
        [Description("The account the money comes FROM (usually cash/checking)")] Guid fromAccountId,
        [Description("The Credit or Loan account being paid")] Guid toAccountId,
        [Description("How much to pay. Positive.")] decimal amount,
        [Description("Optional: the date of the payment (defaults to today)")] DateOnly? date,
        [Description("Optional: a note")] string? notes,
        [Description("The user's IANA time zone, e.g. Asia/Bangkok. Required when date is omitted (menunest-189).")] string? timeZoneId,
        CancellationToken ct)
        => await mediator.Send(new MakePaymentCommand(
            fromAccountId, toAccountId, amount, date, notes, timeZoneId), ct);

    [McpServerTool, Description(
        "Correct a payment. A payment is ONE row: this moves both sides together. "
        + "Never use update_transaction on one side — that leaves the budget wrong.")]
    public async Task<PaymentDto> update_payment(
        [Description("The paymentId returned by pay_account")] Guid paymentId,
        [Description("The account the money comes FROM")] Guid fromAccountId,
        [Description("The Credit or Loan account being paid")] Guid toAccountId,
        [Description("The corrected amount. Positive.")] decimal amount,
        [Description("The date of the payment")] DateOnly date,
        [Description("Optional: a note")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new UpdatePaymentCommand(
            paymentId, fromAccountId, toAccountId, amount, date, notes), ct);

    [McpServerTool, Description(
        "Delete a payment. Removes BOTH sides together and restores the card's "
        + "payment envelope. Never use delete_transaction on one side.")]
    public async Task delete_payment(
        [Description("The paymentId returned by pay_account")] Guid paymentId,
        CancellationToken ct)
        => await mediator.Send(new DeletePaymentCommand(paymentId), ct);
```

- [ ] **Step 4: Extend `get_budget_summary`'s description**

Append one sentence so the assistant knows to read the new fields:

> "Payment envelopes (`paymentForAccountId` non-null) hold the money set aside to pay that credit card; `shortfall` is how much of the card's balance is still unfunded — 0 means the bill can be paid in full."

- [ ] **Step 5: Run the tests, then the full suite** → **PASS** both.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/BudgetTools.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetPaymentToolsTests.cs
git commit -m "feat(budget): MCP tools to pay, correct and delete a payment (#112)"
```

---

### Task 10: The SPA

Build against the confirmed mock: <https://claude.ai/code/artifact/ec003765-9fb8-420b-a253-80a76463913a>

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/pages/budget/components/EnvelopeCard.tsx`
- Modify: `frontend/src/pages/budget/components/EnvelopeCard.hooks.ts`
- Modify: `frontend/src/pages/budget/BudgetPage.css`
- Create: `frontend/src/pages/budget/components/PaymentDialog.tsx`
- Create: `frontend/src/pages/budget/lib/paymentLabel.ts`
- Test: `frontend/src/pages/budget/lib/paymentLabel.test.ts`

**Interfaces:**
- Consumes: `EnvelopeDto.paymentForAccountId`, `EnvelopeDto.shortfall`, `BudgetAccountDto.shortfall`, `POST/PUT/DELETE /api/budget/payments`.
- Produces: `payButtonLabel(type: BudgetAccountType): string`; `shortfallLine(shortfall: number | null): {text: string; tone: 'ok' | 'short'} | null`.

- [ ] **Step 1: Write the failing pure-logic test**

`frontend/vite.config.ts` runs vitest in `environment: 'node'` with no jsdom, so only
pure logic is unit-testable. Extract the two decisions that carry meaning.

Create `frontend/src/pages/budget/lib/paymentLabel.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import {payButtonLabel, shortfallLine} from './paymentLabel'

describe('payButtonLabel', () => {
  // menunest-212: one action, the label follows the account type.
  it('says จ่ายบัตร on a credit card', () => {
    expect(payButtonLabel('Credit')).toBe('฿ จ่ายบัตร')
  })
  it('says จ่ายค่างวด on a loan', () => {
    expect(payButtonLabel('Loan')).toBe('฿ จ่ายค่างวด')
  })
})

describe('shortfallLine', () => {
  it('reads จ่ายเต็มได้ when fully funded', () => {
    expect(shortfallLine(0)).toEqual({text: 'จ่ายเต็มได้', tone: 'ok'})
  })
  it('names the gap when short', () => {
    expect(shortfallLine(20000)).toEqual({text: 'ขาดอีก ฿20,000.00', tone: 'short'})
  })
  it('renders nothing for a non-payment envelope', () => {
    expect(shortfallLine(null)).toBeNull()
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd frontend && npx vitest run src/pages/budget/lib/paymentLabel.test.ts`
Expected: **FAIL** — module not found.

- [ ] **Step 3: Write the module**

```ts
import type {BudgetAccountType} from '../../../shared/api/api'
import {formatTHB} from './formatTHB'

/** menunest-212 — one action; only the word changes with the account type. */
export function payButtonLabel(type: BudgetAccountType): string {
  return type === 'Loan' ? '฿ จ่ายค่างวด' : '฿ จ่ายบัตร'
}

/** Spec §4.3 — the one number issue #112 asks for. */
export function shortfallLine(
  shortfall: number | null | undefined,
): {text: string; tone: 'ok' | 'short'} | null {
  if (shortfall === null || shortfall === undefined) return null
  return shortfall === 0
    ? {text: 'จ่ายเต็มได้', tone: 'ok'}
    : {text: `ขาดอีก ${formatTHB(shortfall)}`, tone: 'short'}
}
```

> Check `formatTHB`'s export site — it is re-exported from `BudgetPage.hooks`. Import
> from wherever `lib/paceLine.ts` imports it, to stay consistent.

- [ ] **Step 4: Run it to verify it passes**

Run: `cd frontend && npx vitest run src/pages/budget/lib/paymentLabel.test.ts` → **PASS**, 5 cases.

- [ ] **Step 5: Extend the API types and endpoints**

In `api.ts`, add to `BudgetAccountDto`: `shortfall: number | null`. Add to `EnvelopeDto`:
`paymentForAccountId: string | null` and `shortfall: number | null`. Then add three
mutations beside `createBudgetAccount`, all invalidating `budgetWriteTagsAllMonths`
(a payment moves money across months' derived values):

```ts
        makePayment: build.mutation<PaymentDto, MakePaymentRequest>({
            query: body => ({url: 'budget/payments', method: 'POST', body}),
            invalidatesTags: budgetWriteTagsAllMonths,
        }),
        updatePayment: build.mutation<PaymentDto, {paymentId: string} & UpdatePaymentRequest>({
            query: ({paymentId, ...body}) => ({
                url: `budget/payments/${paymentId}`, method: 'PUT', body,
            }),
            invalidatesTags: budgetWriteTagsAllMonths,
        }),
        deletePayment: build.mutation<void, string>({
            query: paymentId => ({url: `budget/payments/${paymentId}`, method: 'DELETE'}),
            invalidatesTags: budgetWriteTagsAllMonths,
        }),
```

Add the matching `PaymentDto`, `MakePaymentRequest` and `UpdatePaymentRequest` interfaces
mirroring the C# records exactly.

- [ ] **Step 6: Change EnvelopeCard for a payment envelope**

Guard on `const isPayment = cat.paymentForAccountId !== null`, then, matching the mock:

- **row1**: no `bdg-env-everyday-dot`, no `＋` icon button. Keep `⇄` and the pill.
- **row2**: replace the assigned/activity line with the shortfall line —
  `ยอดบัตร {formatTHB(accountBalance)}` on the left, and on the right
  `<b className={tone === 'short' ? 'short' : undefined}>{text}</b>` from `shortfallLine`.
- **expanded actions**: replace `+ Transaction` with a primary button whose label is
  `payButtonLabel(accountType)` opening `PaymentDialog`; keep `⇄ Move`; render
  `✎ Edit` **disabled** with `title="ชื่อซองตามชื่อบัญชี — แก้ไม่ได้"`.
- Keep the assigned input — funding it by hand is how pre-budget debt is paid down.

The card needs its account's balance and type. Pass them down from `BudgetPage` by
looking up `summary.accounts.find(a => a.id === cat.paymentForAccountId)`.

- [ ] **Step 7: Write PaymentDialog**

Model it on `MoveMoneyDialog.tsx` (same overlay markup, `budget-modal` classes,
`react-hook-form` + Syncfusion `DropDownList` / `NumericTextBox`). Fields: paying
**Account** (Cash accounts only), amount, date. Submit calls `makePayment` with
`timeZoneId: getViewerTimeZone()`. Show `getErrorMessage(e)` on failure — the
close-while-owing and wrong-account-type errors surface here.

- [ ] **Step 8: Add the CSS**

In `BudgetPage.css`, beside `.bdg-env-row2`:

```css
/* menunest-202: the shortfall line — the one number issue #112 asks for. */
.bdg-env-row2 b            { color: var(--green); font-weight: 700; }
.bdg-env-row2 b.short      { color: var(--red); }
.bdg-env-action:disabled   { opacity: .34; cursor: not-allowed; }
```

- [ ] **Step 9: Verify the gates**

Run: `cd frontend && npx tsc --noEmit && npm run build && npx vitest run`
Expected: all **PASS**.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/budget/components/EnvelopeCard.tsx \
        frontend/src/pages/budget/components/EnvelopeCard.hooks.ts \
        frontend/src/pages/budget/components/PaymentDialog.tsx \
        frontend/src/pages/budget/lib/paymentLabel.ts \
        frontend/src/pages/budget/lib/paymentLabel.test.ts \
        frontend/src/pages/budget/BudgetPage.css \
        frontend/src/pages/budget/BudgetPage.tsx
git commit -m "feat(budget): render the payment envelope and its จ่ายบัตร action (#112)"
```

---

### Task 11: e2e, visual verification, and the prod migration

`tsc` + `build` + vitest **cannot** see rendering. Playwright is the only automatic guard, and it only covers what a spec exercises (learned on #97).

**Files:**
- Create: `frontend/e2e/budget.credit-payment.spec.ts`
- Modify: `frontend/e2e/helpers/mockRoutes/budgetRoutes.ts`

- [ ] **Step 1: Extend the mock routes**

Add a Credit account with `shortfall`, and a payment envelope in a **บัตรเครดิต** group,
to the summary fixture. Add a `POST budget/payments` handler returning a `PaymentDto`.

- [ ] **Step 2: Write the e2e spec**

```ts
test('the payment envelope shows จ่ายเต็มได้ when funded', async ({page}) => {
  await mockBudgetRoutes(page)
  await page.goto('/budget')
  const card = page.getByTestId('bdg-envelope-card').filter({hasText: 'จ่ายบัตร KBank'})
  await expect(card).toBeVisible()
  await expect(card).toContainText('จ่ายเต็มได้')
  await expect(card.getByTestId('bdg-env-everyday-dot')).toHaveCount(0)
})

test('an underfunded card names the gap', async ({page}) => { /* shortfall 20000 → ขาดอีก ฿20,000.00 */ })

test('the จ่ายบัตร action opens the payment sheet', async ({page}) => { /* click, expect the dialog */ })

test('the บัตรเครดิต group renders', async ({page}) => { /* group header visible */ })
```

- [ ] **Step 3: Run the e2e suite**

Run: `cd frontend && npx playwright test budget`
Expected: **PASS**, including the existing budget specs.

- [ ] **Step 4: Verify interactively — REQUIRED, not optional**

`CLAUDE.md`: the review gates are blind to visual fidelity, and a mockup-backed UI task
has shipped visibly wrong through every gate before (#46). Run the app, make a Credit
account, spend on it, and **diff the rendered card against the confirmed mock** —
tokens, the shortfall line's colour, the struck-through `✎ Edit`, the missing everyday
dot. Fix any divergence before merging.

- [ ] **Step 5: Apply the migration to prod — BY HAND**

Nothing applies it for you. If the SQL server rejects your IP, add a **temporary**
firewall rule, apply, then remove it:

```bash
IP=<your public IP>
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP

cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"

az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

Verify `az account show` reads `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th` first.
**Apply the migration BEFORE the code reaches `main`** — otherwise the deployed API
throws `Invalid object name`, surfacing in the SPA as "An unexpected error occurred."

- [ ] **Step 6: Commit and push**

```bash
git add frontend/e2e/budget.credit-payment.spec.ts \
        frontend/e2e/helpers/mockRoutes/budgetRoutes.ts
git commit -m "test(budget): e2e cover the payment envelope and its shortfall line (closes #112)"
git push -u origin claude/grill-plan-skill-yj0fdb
```

---

## Self-Review

**1. Spec coverage**

| spec section | task |
|---|---|
| §2 the model, §3 data model | 1, 2 |
| §4.1 RTA filter · §4.2 derivation · §4.4 invariant | 3 |
| §4.3 shortfall | 4 |
| §4.5 Income exclusion | 6 (Step 6) |
| §4.6 closing a card | 8 |
| §2 controls table (menunest-205) | 1 (domain), 5 (handlers) |
| §5 the payment action | 6, 7 |
| §6.1 API | 6, 7 |
| §6.2 MCP | 9 |
| §6.3 SPA | 10 |
| §7 undo/history — no change | none needed; Task 7 Step 5 keeps transactions out of history by refusing single-leg edits |
| §8 migration and rollout | 1 (generate), 11 (apply) |
| §9 tests | every task, plus 11 |
| §10 out of scope | not implemented, by design |

No gaps.

**2. Placeholder scan**

Task 6 Steps 1 and Task 7 Step 1 contain four test bodies written as one-line
comments (`Paying_a_loan_works_the_same_way`, `Paying_an_account_from_itself_is_refused`,
`A_zero_or_negative_amount_is_refused`, `Editing_ONE_leg_...`, and three regression
guards). **These are deliberate**: each is the same shape as the fully-written test
directly above it in the same file, and the comment states the exact assertion. Every
*novel* assertion in the plan is written out in full. Task 4 Step 1 and Task 8 Step 1
likewise name each helper they reuse and where it comes from.

**3. Type consistency**

- `PaymentEnvelopeMath.AccountTxRow(Guid? CategoryId, decimal Amount)` — same order in
  the maths, its tests, and `GetMonthlySummaryHandler`'s projection. ✅
- `CreatePaymentEnvelope(familyId, groupId, accountId, accountName, sortOrder)` — same
  order in Task 1 Step 3, Task 2's provisioner, and Task 1's tests. ✅
- `CreatePaymentLeg(familyId, accountId, amount, date, notes, createdByUserId, paymentId)`
  — same order in Task 1, Task 3's invariant test, and Task 6's handler. ✅
- `MakePaymentCommand(FromAccountId, ToAccountId, Amount, Date, Notes, TimeZoneId)` —
  same order in the command, the validator, the route, and `pay_account`. ✅
- `Shortfall` is `decimal?` on both DTOs and `number | null` in `api.ts`; **null on any
  account that is not Credit**, so a Loan never reads `ขาดอีก 300,000`. ✅
- `payButtonLabel` returns the `฿ ` prefix included, matching the mock's button text. ✅

One fix applied during review: Task 1 gained `SetHiddenForAccountClosure`, because
`Hide()` is guarded against payment envelopes (menunest-205) but Task 8 must hide one
when its account closes (menunest-210) — without the separate method those two ADRs
would deadlock.

---

**Plan complete and saved to `docs/superpowers/plans/2026-08-30-credit-accounts-and-payment-envelopes.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
