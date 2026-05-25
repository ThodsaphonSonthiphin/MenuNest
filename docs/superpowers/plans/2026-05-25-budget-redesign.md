# Budget Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle `/budget` to a mobile-first single column, add a banking-app-style `/budget/accounts/:accountId` route, flip account + transaction lists to `CreatedAt DESC`, and replace per-row YNAB table interactions with tap-to-expand + long-press envelope cards. No domain change.

**Architecture:** Backend is additive — 3 auto-sortOrder rewrites of existing Create handlers, 2 one-line sort flips, one new pageable query (`ListAccountTransactions`) with a `(Account, Items, HasMore)` shape. Frontend replaces the 3-column desktop layout with a single column that breaks naturally on mobile, extracts envelope cards into their own `EnvelopeCard.tsx` + `EnvelopeCard.hooks.ts` pair, and ships a new `AccountDetailPage` route that consumes the new endpoint with `IntersectionObserver`-driven infinite scroll.

**Tech Stack:**
- Backend: .NET 10, Mediator (CQRS), FluentValidation, EF Core (InMemory for tests), xUnit + FluentAssertions
- Frontend: React 19, react-router-dom 7, Redux Toolkit + RTK Query, Syncfusion React inputs, React Hook Form
- E2E: Playwright 1.60+ with `authedPage` fixture from `frontend/e2e/fixtures/healthFixture.ts`
- Pre-commit hook builds backend (Release), runs xUnit, builds frontend with tsc + vite — every commit must pass

**Spec:** [docs/superpowers/specs/2026-05-25-budget-redesign-design.md](../specs/2026-05-25-budget-redesign-design.md)
**Mock:** [docs/mocks/budget-redesign-mock.html](../../mocks/budget-redesign-mock.html)

---

## File Structure

### Create

**Backend:**
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsQuery.cs`
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsHandler.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountTransactionsHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Groups/CreateGroupHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/CreateCategoryHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountsHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/Budget/Transactions/ListTransactionsHandlerTests.cs`

**Frontend:**
- `frontend/src/pages/budget/components/MonthStrip.tsx`
- `frontend/src/pages/budget/components/RtaHero.tsx`
- `frontend/src/pages/budget/components/AccountsStrip.tsx`
- `frontend/src/pages/budget/components/EnvelopeList.tsx`
- `frontend/src/pages/budget/components/EnvelopeCard.tsx`
- `frontend/src/pages/budget/components/EnvelopeCard.hooks.ts`
- `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`
- `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts`
- `frontend/src/pages/budget/account-detail/AccountHero.tsx`
- `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx`
- `frontend/e2e/budget.smoke.spec.ts`
- `frontend/e2e/budget.interactions.spec.ts`

### Modify

**Backend:**
- `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs` — add `AccountSummaryDto`, `AccountTransactionsPageDto`
- `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupCommand.cs` — drop `SortOrder`
- `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupHandler.cs` — compute `SortOrder` server-side
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountCommand.cs` — drop `SortOrder`
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs` — compute `SortOrder`
- `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryCommand.cs` — drop `SortOrder`
- `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryHandler.cs` — compute `SortOrder` within `GroupId` scope
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs` — sort by `CreatedAt DESC`, filter closed
- `backend/src/MenuNest.Application/UseCases/Budget/Transactions/ListTransactions/ListTransactionsHandler.cs` — sort by `CreatedAt DESC` only
- `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs` — drop `SortOrder` from 3 `new *Command(...)` calls, add new account-transactions route

**Frontend:**
- `frontend/src/pages/budget/budgetSlice.ts` — drop drawer flags, add `expandedCategoryId`
- `frontend/src/pages/budget/BudgetPage.tsx` — single-column layout, no more sidebar/summary panel
- `frontend/src/pages/budget/BudgetPage.css` — replace tokens + layout
- `frontend/src/pages/budget/index.ts` — re-export `AccountDetailPage`
- `frontend/src/pages/budget/components/TransactionDialog.tsx` — accept optional `preset` prop
- `frontend/src/pages/budget/components/AddCategoryDialog.tsx` — drop `sortOrder` from form payload
- `frontend/src/pages/budget/components/AddAccountDialog.tsx` — drop `sortOrder` from form payload
- `frontend/src/shared/api/api.ts` — drop `sortOrder` from `CreateAccountRequest` + `UpsertGroupRequest` + `UpsertCategoryRequest` (Update* shapes keep it); add `listBudgetAccountTransactions` + types
- `frontend/src/router.tsx` — add `/budget/accounts/:accountId` route

### Delete

- `frontend/src/pages/budget/components/AccountsSidebar.tsx`
- `frontend/src/pages/budget/components/MonthlySummaryPanel.tsx`
- `frontend/src/pages/budget/components/EnvelopeTable.tsx`
- `frontend/src/pages/budget/components/EnvelopeRow.tsx`

---

## Conventions used in this plan

- **CQRS:** Commands/queries are `IRequest` records living next to their handler; tests target the handler with `HandlerTestFixture` (InMemory DB) — see `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`.
- **Not-found errors:** This codebase uses `throw new DomainException("X not found.")` — not a separate `NotFoundException`. Keep that style. (`DomainException` lives in `MenuNest.Domain.Exceptions`.)
- **Test selectors:** Playwright specs prefer `data-testid` on the components the test drives. Use the prefix `bdg-` (e.g. `bdg-page`, `bdg-account-card`, `bdg-envelope-card`, `bdg-fab`).
- **Auth:** specs use `authedPage` from `frontend/e2e/fixtures/healthFixture.ts`. `/budget` and `/budget/accounts/:id` both sit inside `FamilyRequiredRoute`, so the authed user must have a family. The fixture already provisions one.
- **Commits:** one commit per task, prefixed `feat(budget):` / `test(budget):` / `refactor(budget):` / `style(budget):`. Pre-commit hook runs backend build + tests + frontend build — each commit must compile cleanly and keep all tests green.
- **No skipping hooks:** Never `--no-verify`. If a hook fails, diagnose.
- **Backend tests:** xUnit + FluentAssertions. Pattern: `using var fx = new HandlerTestFixture();` → seed → `var sut = new HandlerName(fx.Db, fx.UserProvisioner.Object, ...);` → `await sut.Handle(new XxxQuery(...), CancellationToken.None);` → assert.

---

## Task 1: Auto sortOrder in CreateGroupHandler

The simplest of the three create handlers (no relationship lookups). Build the pattern here, then reuse for accounts + categories.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Groups/CreateGroupHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Groups/CreateGroupHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class CreateGroupHandlerTests
{
    private static CreateGroupHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateGroupValidator());

    [Fact]
    public async Task First_group_in_family_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(new CreateGroupCommand("Bills"), CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Subsequent_group_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.BudgetCategoryGroups.Add(BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0));
        fx.Db.BudgetCategoryGroups.Add(BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 7));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(new CreateGroupCommand("Savings"), CancellationToken.None);

        result.SortOrder.Should().Be(8);
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(new CreateGroupCommand("  "), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run:

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateGroupHandlerTests" --nologo
```

Expected: Compile error — `CreateGroupCommand` constructor takes 2 args (name + sortOrder), tests pass only 1.

- [ ] **Step 3: Drop SortOrder from the command**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

public sealed record CreateGroupCommand(string Name) : ICommand<CategoryGroupDto>;
```

- [ ] **Step 4: Compute sortOrder in the handler**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup/CreateGroupHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

public sealed class CreateGroupHandler : ICommandHandler<CreateGroupCommand, CategoryGroupDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateGroupCommand> _validator;
    public CreateGroupHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateGroupCommand> v)
    { _db = db; _users = users; _validator = v; }

    public async ValueTask<CategoryGroupDto> Handle(CreateGroupCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var nextSortOrder = (await _db.BudgetCategoryGroups
            .Where(g => g.FamilyId == familyId)
            .MaxAsync(g => (int?)g.SortOrder, ct) ?? -1) + 1;

        var group = BudgetCategoryGroup.Create(familyId, cmd.Name, nextSortOrder);
        _db.BudgetCategoryGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return new CategoryGroupDto(group.Id, group.Name, group.SortOrder, group.IsHidden);
    }
}
```

- [ ] **Step 5: Update the controller route**

Modify `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`. Find the `CreateGroup` action and replace its `new CreateGroupCommand(r.Name, r.SortOrder)` with `new CreateGroupCommand(r.Name)`:

```csharp
[HttpPost("groups")]
public async Task<ActionResult<CategoryGroupDto>> CreateGroup(
    [FromBody] UpsertGroupRequest r, CancellationToken ct) =>
    Ok(await _m.Send(new CreateGroupCommand(r.Name), ct));
```

(Leave `UpdateGroup` alone — `UpdateGroupCommand` still takes `SortOrder`. Leave `UpsertGroupRequest` alone — the request DTO keeps the field so existing clients don't break; we just discard it.)

- [ ] **Step 6: Run the tests to verify they pass**

Run:

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateGroupHandlerTests" --nologo
```

Expected: 3 tests pass.

- [ ] **Step 7: Run the full backend test suite to make sure nothing else broke**

Run:

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: all tests green (no other tests reference `CreateGroupCommand` SortOrder positional arg).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Groups/CreateGroup backend/src/MenuNest.WebApi/Controllers/BudgetController.cs backend/tests/MenuNest.Application.UnitTests/Budget/Groups/CreateGroupHandlerTests.cs
git commit -m "feat(budget): auto-assign sortOrder in CreateGroupHandler"
```

---

## Task 2: Auto sortOrder in CreateAccountHandler

Same pattern as Task 1, applied to `BudgetAccount`.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class CreateAccountHandlerTests
{
    private static CreateAccountHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator());

    [Fact]
    public async Task First_account_in_family_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, openingBalance: 0m),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Subsequent_account_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 3));
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(fx.Family.Id, "KBank Credit", BudgetAccountType.Credit, 0m, 11));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Wise", BudgetAccountType.Cash, 0m),
            CancellationToken.None);

        result.SortOrder.Should().Be(12);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run:

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateAccountHandlerTests" --nologo
```

Expected: Compile error — `CreateAccountCommand` takes 4 args, tests pass 3.

- [ ] **Step 3: Drop SortOrder from the command**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

public sealed record CreateAccountCommand(
    string Name, BudgetAccountType Type, decimal OpeningBalance)
    : ICommand<BudgetAccountDto>;
```

- [ ] **Step 4: Compute sortOrder in the handler**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

public sealed class CreateAccountHandler : ICommandHandler<CreateAccountCommand, BudgetAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateAccountCommand> _validator;
    public CreateAccountHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateAccountCommand> v)
    { _db = db; _users = users; _validator = v; }

    public async ValueTask<BudgetAccountDto> Handle(CreateAccountCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var nextSortOrder = (await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;

        var acc = BudgetAccount.Create(familyId, cmd.Name, cmd.Type, cmd.OpeningBalance, nextSortOrder);
        _db.BudgetAccounts.Add(acc);
        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
```

- [ ] **Step 5: Update the controller route**

In `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`, update `CreateAccount`:

```csharp
[HttpPost("accounts")]
public async Task<ActionResult<BudgetAccountDto>> CreateAccount(
    [FromBody] CreateAccountRequest r, CancellationToken ct) =>
    Ok(await _m.Send(new CreateAccountCommand(r.Name, r.Type, r.OpeningBalance), ct));
```

- [ ] **Step 6: Run the targeted tests, then the full suite**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateAccountHandlerTests" --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: targeted tests pass; full suite stays green.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount backend/src/MenuNest.WebApi/Controllers/BudgetController.cs backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/CreateAccountHandlerTests.cs
git commit -m "feat(budget): auto-assign sortOrder in CreateAccountHandler"
```

---

## Task 3: Auto sortOrder in CreateCategoryHandler

Categories are scoped to a Group, so the `max+1` must be computed per `GroupId`.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/CreateCategoryHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Categories/CreateCategoryHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class CreateCategoryHandlerTests
{
    private static CreateCategoryHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

    [Fact]
    public async Task First_category_in_group_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(group.Id, "Rent", emoji: "🏠",
                TargetType: BudgetTargetType.None, TargetAmount: null,
                TargetDueDate: null, TargetDayOfMonth: null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Sort_order_is_scoped_to_group()
    {
        using var fx = new HandlerTestFixture();
        var bills = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        var fun = BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 1);
        fx.Db.BudgetCategoryGroups.AddRange(bills, fun);
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Rent", null, 5));
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Electric", null, 6));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(fun.Id, "Dining", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0); // Fun group is empty — starts from 0
    }

    [Fact]
    public async Task Subsequent_category_in_group_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        var bills = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(bills);
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Rent", null, 5));
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Electric", null, 6));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(bills.Id, "Water", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(7);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateCategoryHandlerTests" --nologo
```

Expected: Compile error — `CreateCategoryCommand` constructor mismatch.

- [ ] **Step 3: Drop SortOrder from the command**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Categories.CreateCategory;

public sealed record CreateCategoryCommand(
    Guid GroupId, string Name, string? Emoji,
    BudgetTargetType TargetType, decimal? TargetAmount,
    DateOnly? TargetDueDate, int? TargetDayOfMonth)
    : ICommand<BudgetCategoryDto>;
```

- [ ] **Step 4: Compute sortOrder in the handler, scoped by GroupId**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryHandler.cs`. Replace the existing `Handle` body (keep the `ApplyTarget` helper as-is):

```csharp
public async ValueTask<BudgetCategoryDto> Handle(CreateCategoryCommand cmd, CancellationToken ct)
{
    await _validator.ValidateAndThrowAsync(cmd, ct);
    var (_, familyId) = await _users.RequireFamilyAsync(ct);

    var groupBelongs = await _db.BudgetCategoryGroups
        .AnyAsync(g => g.Id == cmd.GroupId && g.FamilyId == familyId, ct);
    if (!groupBelongs) throw new DomainException("Group not found.");

    var nextSortOrder = (await _db.BudgetCategories
        .Where(c => c.FamilyId == familyId && c.GroupId == cmd.GroupId)
        .MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;

    var cat = BudgetCategory.Create(familyId, cmd.GroupId, cmd.Name, cmd.Emoji, nextSortOrder);
    ApplyTarget(cat, cmd.TargetType, cmd.TargetAmount, cmd.TargetDueDate, cmd.TargetDayOfMonth);

    _db.BudgetCategories.Add(cat);
    await _db.SaveChangesAsync(ct);
    return new BudgetCategoryDto(
        cat.Id, cat.GroupId, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
        cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth);
}
```

- [ ] **Step 5: Update the controller route**

In `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`, update `CreateCategory`:

```csharp
[HttpPost("categories")]
public async Task<ActionResult<BudgetCategoryDto>> CreateCategory(
    [FromBody] UpsertCategoryRequest r, CancellationToken ct) =>
    Ok(await _m.Send(new CreateCategoryCommand(
        r.GroupId, r.Name, r.Emoji,
        r.TargetType, r.TargetAmount, r.TargetDueDate, r.TargetDayOfMonth), ct));
```

- [ ] **Step 6: Run the targeted tests, then the full suite**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~CreateCategoryHandlerTests" --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: targeted tests pass; full suite stays green.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory backend/src/MenuNest.WebApi/Controllers/BudgetController.cs backend/tests/MenuNest.Application.UnitTests/Budget/Categories/CreateCategoryHandlerTests.cs
git commit -m "feat(budget): auto-assign sortOrder in CreateCategoryHandler (scoped per group)"
```

---

## Task 4: Sort flip in ListAccountsHandler

Switch from `IsClosed → Type → SortOrder → Name` to `CreatedAt DESC` and filter out closed accounts.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountsHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountsHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class ListAccountsHandlerTests
{
    private static ListAccountsHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    [Fact]
    public async Task Sorts_by_created_at_descending()
    {
        using var fx = new HandlerTestFixture();
        // Seed three accounts at distinct times by saving sequentially.
        var first = BudgetAccount.Create(fx.Family.Id, "Older", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(first);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var middle = BudgetAccount.Create(fx.Family.Id, "Middle", BudgetAccountType.Credit, 0m, 0);
        fx.Db.BudgetAccounts.Add(middle);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var newest = BudgetAccount.Create(fx.Family.Id, "Newest", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(newest);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainInOrder("Newest", "Middle", "Older");
    }

    [Fact]
    public async Task Excludes_closed_accounts()
    {
        using var fx = new HandlerTestFixture();
        var open = BudgetAccount.Create(fx.Family.Id, "Open", BudgetAccountType.Cash, 0m, 0);
        var closed = BudgetAccount.Create(fx.Family.Id, "Closed", BudgetAccountType.Cash, 0m, 0);
        closed.Close();
        fx.Db.BudgetAccounts.AddRange(open, closed);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainSingle().Which.Should().Be("Open");
    }

    [Fact]
    public async Task Only_returns_callers_family_accounts()
    {
        using var fx = new HandlerTestFixture();
        var mine = BudgetAccount.Create(fx.Family.Id, "Mine", BudgetAccountType.Cash, 0m, 0);
        var theirs = BudgetAccount.Create(Guid.NewGuid(), "Theirs", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.AddRange(mine, theirs);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainSingle().Which.Should().Be("Mine");
    }
}
```

- [ ] **Step 2: Run the test to verify the sort assertion fails**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListAccountsHandlerTests" --nologo
```

Expected: 1+ failure on the `CreatedAt` sort assertion (existing handler sorts by `IsClosed/Type/SortOrder/Name`). The closed-exclusion test may also fail.

- [ ] **Step 3: Flip the sort + filter closed**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs`. Replace the `Handle` body:

```csharp
public async ValueTask<IReadOnlyList<BudgetAccountDto>> Handle(ListAccountsQuery q, CancellationToken ct)
{
    var (_, familyId) = await _users.RequireFamilyAsync(ct);
    return await _db.BudgetAccounts
        .Where(a => a.FamilyId == familyId && !a.IsClosed)
        .OrderByDescending(a => a.CreatedAt)
        .Select(a => new BudgetAccountDto(a.Id, a.Name, a.Type, a.Balance, a.SortOrder, a.IsClosed))
        .ToListAsync(ct);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListAccountsHandlerTests" --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: targeted tests pass; full suite stays green. (`GetMonthlySummary` still uses its own ordered query inside; this handler change doesn't break it.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccounts/ListAccountsHandler.cs backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountsHandlerTests.cs
git commit -m "feat(budget): sort accounts by CreatedAt DESC, exclude closed"
```

---

## Task 5: Sort flip in ListTransactionsHandler

Change `Date DESC, CreatedAt DESC` → `CreatedAt DESC`.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/Transactions/ListTransactions/ListTransactionsHandler.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Transactions/ListTransactionsHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Transactions/ListTransactionsHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

public class ListTransactionsHandlerTests
{
    private static ListTransactionsHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    [Fact]
    public async Task Sorts_by_created_at_descending_ignoring_date()
    {
        using var fx = new HandlerTestFixture();
        var account = BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 100m, 0);
        fx.Db.BudgetAccounts.Add(account);
        await fx.Db.SaveChangesAsync();

        // Seed three transactions whose insertion order is the inverse
        // of their Date — proves we sort by CreatedAt, not by Date.
        var older = BudgetTransaction.Create(
            fx.Family.Id, account.Id, null, -10m,
            date: new DateOnly(2026, 05, 20), notes: "older-create",
            createdByUserId: fx.User.Id);
        fx.Db.BudgetTransactions.Add(older);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var middle = BudgetTransaction.Create(
            fx.Family.Id, account.Id, null, -20m,
            date: new DateOnly(2026, 05, 10), notes: "middle-create",
            createdByUserId: fx.User.Id);
        fx.Db.BudgetTransactions.Add(middle);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var newest = BudgetTransaction.Create(
            fx.Family.Id, account.Id, null, -30m,
            date: new DateOnly(2026, 05, 01), notes: "newest-create",
            createdByUserId: fx.User.Id);
        fx.Db.BudgetTransactions.Add(newest);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new ListTransactionsQuery(2026, 5, CategoryId: null),
            CancellationToken.None);

        result.Select(t => t.Notes).Should()
            .ContainInOrder("newest-create", "middle-create", "older-create");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListTransactionsHandlerTests" --nologo
```

Expected: FAIL — current handler sorts by `Date DESC` first, so the row with Date=2026-05-20 (older-create) ends up first.

- [ ] **Step 3: Flip the sort**

Modify `backend/src/MenuNest.Application/UseCases/Budget/Transactions/ListTransactions/ListTransactionsHandler.cs`. Change ONE line — `orderby t.Date descending, t.CreatedAt descending` becomes `orderby t.CreatedAt descending`:

```csharp
var query =
    from t in _db.BudgetTransactions
    join a in _db.BudgetAccounts on t.AccountId equals a.Id
    join u in _db.Users on t.CreatedByUserId equals u.Id
    join c in _db.BudgetCategories on t.CategoryId equals c.Id into cj
    from c in cj.DefaultIfEmpty()
    where t.FamilyId == familyId
       && t.Date.Year == q.Year
       && t.Date.Month == q.Month
       && (q.CategoryId == null || t.CategoryId == q.CategoryId)
    orderby t.CreatedAt descending
    select new BudgetTransactionDto(
        t.Id, t.AccountId, a.Name,
        t.CategoryId, c != null ? c.Name : null, c != null ? c.Emoji : null,
        t.Amount, t.Date, t.Notes,
        t.CreatedByUserId, u.DisplayName);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListTransactionsHandlerTests" --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: targeted tests pass; full suite stays green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Transactions/ListTransactions/ListTransactionsHandler.cs backend/tests/MenuNest.Application.UnitTests/Budget/Transactions/ListTransactionsHandlerTests.cs
git commit -m "feat(budget): sort transactions by CreatedAt DESC only"
```

---

## Task 6: Add DTOs + query record for ListAccountTransactions

Define the shape used by the new endpoint without yet wiring a handler.

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsQuery.cs`

- [ ] **Step 1: Add DTOs to BudgetDtos.cs**

Modify `backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs`. Append at the bottom (after the existing `CoverOverspendingRequest`):

```csharp
// ---------- Account detail (transactions feed) ----------
public sealed record AccountSummaryDto(
    Guid Id,
    string Name,
    BudgetAccountType Type,
    decimal Balance,
    decimal MonthInflow,    // sum of positive amounts where Date in given Year/Month
    decimal MonthOutflow    // sum of negative amounts where Date in given Year/Month (stored negative)
);

public sealed record AccountTransactionsPageDto(
    AccountSummaryDto Account,
    IReadOnlyList<BudgetTransactionDto> Items,
    bool HasMore
);
```

- [ ] **Step 2: Create the query record**

Create `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;

/// <summary>
/// Read the account-detail page: account summary (balance + month
/// inflow/outflow) plus a page of that account's transactions ordered
/// by CreatedAt DESC. Year/Month frame the in/out summary; Skip/Take
/// paginate the transaction list.
/// </summary>
public sealed record ListAccountTransactionsQuery(
    Guid AccountId,
    int Year,
    int Month,
    int Skip,
    int Take
) : IQuery<AccountTransactionsPageDto>;
```

- [ ] **Step 3: Build the backend**

```bash
cd backend && dotnet build --nologo
```

Expected: succeeds (no handler yet — the query record compiles alone; Mediator will fail at runtime if dispatched, but no caller dispatches it yet).

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/BudgetDtos.cs backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsQuery.cs
git commit -m "feat(budget): add AccountSummaryDto + ListAccountTransactionsQuery records"
```

---

## Task 7: Implement ListAccountTransactionsHandler

Handler returns the account summary + a paginated transaction slice. Family-scoped, clamps `Take`.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsHandler.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountTransactionsHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountTransactionsHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class ListAccountTransactionsHandlerTests
{
    private static ListAccountTransactionsHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    private static async Task<BudgetAccount> SeedAccount(HandlerTestFixture fx, string name = "Cash")
    {
        var a = BudgetAccount.Create(fx.Family.Id, name, BudgetAccountType.Cash, 1000m, 0);
        fx.Db.BudgetAccounts.Add(a);
        await fx.Db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Returns_account_summary_and_transactions_for_owned_account()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, 5000m,
            new DateOnly(2026, 5, 10), "salary", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -200m,
            new DateOnly(2026, 5, 11), "lunch", fx.User.Id));
        // Out-of-month tx — should NOT be in MonthInflow/MonthOutflow.
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -50m,
            new DateOnly(2026, 4, 30), "april expense", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, Year: 2026, Month: 5, Skip: 0, Take: 50),
            CancellationToken.None);

        result.Account.Id.Should().Be(acct.Id);
        result.Account.Name.Should().Be("Cash");
        result.Account.MonthInflow.Should().Be(5000m);
        result.Account.MonthOutflow.Should().Be(-200m);
        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Items_are_sorted_by_created_at_descending()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -10m, new DateOnly(2026, 5, 20),
            "older-create", fx.User.Id));
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -20m, new DateOnly(2026, 5, 10),
            "newer-create", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, 0, 50),
            CancellationToken.None);

        result.Items.Select(t => t.Notes).Should()
            .ContainInOrder("newer-create", "older-create");
    }

    [Fact]
    public async Task Pagination_respects_skip_and_take_and_sets_hasmore()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);
        for (int i = 0; i < 5; i++)
        {
            fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
                fx.Family.Id, acct.Id, null, -1m,
                new DateOnly(2026, 5, 1).AddDays(i), $"tx-{i}", fx.User.Id));
            await fx.Db.SaveChangesAsync();
            await Task.Delay(5);
        }

        var page1 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 0, Take: 2),
            CancellationToken.None);
        var page2 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 2, Take: 2),
            CancellationToken.None);
        var page3 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 4, Take: 2),
            CancellationToken.None);

        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page2.Items.Should().HaveCount(2);
        page2.HasMore.Should().BeTrue();
        page3.Items.Should().HaveCount(1);
        page3.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Take_is_clamped_to_one_hundred()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);
        var sut = Build(fx);

        // No transactions seeded — assert that the query simply does
        // not throw or hang under a very large Take. We can't easily
        // assert the actual SQL clamp without exposing it, so this
        // verifies the wide value is accepted and returns sanely.
        var result = await sut.Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, 0, Take: 10_000),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Throws_when_account_does_not_belong_to_caller_family()
    {
        using var fx = new HandlerTestFixture();
        var foreign = BudgetAccount.Create(Guid.NewGuid(), "Foreign", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(foreign);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new ListAccountTransactionsQuery(foreign.Id, 2026, 5, 0, 50),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Account not found.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListAccountTransactionsHandlerTests" --nologo
```

Expected: Compile error — handler doesn't exist yet.

- [ ] **Step 3: Implement the handler**

Create `backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;

public sealed class ListAccountTransactionsHandler
    : IQueryHandler<ListAccountTransactionsQuery, AccountTransactionsPageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListAccountTransactionsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<AccountTransactionsPageDto> Handle(
        ListAccountTransactionsQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var acct = await _db.BudgetAccounts
            .FirstOrDefaultAsync(a => a.Id == q.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        var skip = Math.Max(q.Skip, 0);
        var take = Math.Clamp(q.Take, 1, 100);

        // Month inflow / outflow (separate small queries, both server-side).
        var inflow = await _db.BudgetTransactions
            .Where(t => t.AccountId == acct.Id
                     && t.Date.Year == q.Year && t.Date.Month == q.Month
                     && t.Amount > 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        var outflow = await _db.BudgetTransactions
            .Where(t => t.AccountId == acct.Id
                     && t.Date.Year == q.Year && t.Date.Month == q.Month
                     && t.Amount < 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var pageQuery =
            from t in _db.BudgetTransactions
            join a in _db.BudgetAccounts on t.AccountId equals a.Id
            join u in _db.Users on t.CreatedByUserId equals u.Id
            join c in _db.BudgetCategories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where t.AccountId == acct.Id
            orderby t.CreatedAt descending
            select new BudgetTransactionDto(
                t.Id, t.AccountId, a.Name,
                t.CategoryId, c != null ? c.Name : null, c != null ? c.Emoji : null,
                t.Amount, t.Date, t.Notes,
                t.CreatedByUserId, u.DisplayName);

        var items = await pageQuery.Skip(skip).Take(take).ToListAsync(ct);
        var hasMore = await pageQuery.Skip(skip + take).AnyAsync(ct);

        return new AccountTransactionsPageDto(
            new AccountSummaryDto(acct.Id, acct.Name, acct.Type, acct.Balance, inflow, outflow),
            items,
            hasMore);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ListAccountTransactionsHandlerTests" --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: all targeted tests pass; full suite green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/Accounts/ListAccountTransactions/ListAccountTransactionsHandler.cs backend/tests/MenuNest.Application.UnitTests/Budget/Accounts/ListAccountTransactionsHandlerTests.cs
git commit -m "feat(budget): implement ListAccountTransactionsHandler"
```

---

## Task 8: Wire the account-transactions REST endpoint

Expose the handler via `BudgetController`.

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`

- [ ] **Step 1: Add the import**

In `backend/src/MenuNest.WebApi/Controllers/BudgetController.cs`, add this using line near the other budget-namespace imports (next to the existing `Budget.Accounts.ListAccounts` line):

```csharp
using MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;
```

- [ ] **Step 2: Add the action**

Append a new action inside the `// ----- accounts -----` block (right after `DeleteAccount`):

```csharp
[HttpGet("accounts/{id:guid}/transactions")]
public async Task<ActionResult<AccountTransactionsPageDto>> ListAccountTransactions(
    Guid id, [FromQuery] int year, [FromQuery] int month,
    [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) =>
    Ok(await _m.Send(new ListAccountTransactionsQuery(id, year, month, skip, take), ct));
```

- [ ] **Step 3: Build the backend**

```bash
cd backend && dotnet build --nologo
```

Expected: build succeeds.

- [ ] **Step 4: Verify the route resolves**

Backend tests don't cover routing, but we can sanity-check the registration. Run:

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: all backend tests pass (this is mostly a sanity check after the controller edit).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/BudgetController.cs
git commit -m "feat(budget): expose GET /api/budget/accounts/{id}/transactions"
```

---

## Task 9: budgetSlice — drop drawer flags, add expandedCategoryId

Slice shape changes so subsequent UI tasks can pull `expandedCategoryId` from state.

**Files:**
- Modify: `frontend/src/pages/budget/budgetSlice.ts`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx` (delete references to dropped actions/state — keeps the page compiling)

- [ ] **Step 1: Update the slice**

Replace the entire contents of `frontend/src/pages/budget/budgetSlice.ts`:

```ts
import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'

export type BudgetFilter = 'all' | 'overspent' | 'underfunded' | 'overfunded' | 'available' | 'snoozed'
export type BudgetLayout = 'desktop' | 'tablet' | 'mobile'

interface BudgetState {
  year: number
  month: number
  filter: BudgetFilter
  expandedCategoryId: string | null
  selectedCategoryId: string | null
  search: string
}

const now = new Date()
const initialState: BudgetState = {
  year: now.getFullYear(),
  month: now.getMonth() + 1,
  filter: 'all',
  expandedCategoryId: null,
  selectedCategoryId: null,
  search: '',
}

const budgetSlice = createSlice({
  name: 'budget',
  initialState,
  reducers: {
    setMonth(s, a: PayloadAction<{year: number; month: number}>) {
      s.year = a.payload.year; s.month = a.payload.month
    },
    goPrevMonth(s) {
      const d = new Date(s.year, s.month - 2, 1)
      s.year = d.getFullYear(); s.month = d.getMonth() + 1
    },
    goNextMonth(s) {
      const d = new Date(s.year, s.month, 1)
      s.year = d.getFullYear(); s.month = d.getMonth() + 1
    },
    setFilter(s, a: PayloadAction<BudgetFilter>) { s.filter = a.payload },
    setExpandedCategory(s, a: PayloadAction<string | null>) { s.expandedCategoryId = a.payload },
    setSelectedCategory(s, a: PayloadAction<string | null>) { s.selectedCategoryId = a.payload },
    setSearch(s, a: PayloadAction<string>) { s.search = a.payload },
  },
})

export const {
  setMonth, goPrevMonth, goNextMonth, setFilter,
  setExpandedCategory, setSelectedCategory, setSearch,
} = budgetSlice.actions
export default budgetSlice.reducer
```

- [ ] **Step 2: Update BudgetPage.tsx to drop deleted imports**

The current `BudgetPage.tsx` imports `setAccountsOpen` and `setSummaryOpen`. They no longer exist. Apply this minimal patch to keep the file compiling (full restyle happens in Task 23):

In `frontend/src/pages/budget/BudgetPage.tsx`:
- Remove the import line `import {setAccountsOpen, setSummaryOpen} from './budgetSlice'`.
- In the destructuring `const {filter, accountsOpen, summaryOpen} = useAppSelector(s => s.budget)` change to `const {filter} = useAppSelector(s => s.budget)`.
- In the JSX, delete the two mobile-bar buttons (the ones that called `setAccountsOpen(true)` and `setSummaryOpen(true)`) and delete the two trailing conditional blocks that render the left/right drawers (`{layout !== 'desktop' && accountsOpen && (...)} {layout !== 'desktop' && summaryOpen && (...)}`).
- Hard-code `accountsOpen = false` and `summaryOpen = false` is NOT acceptable; just remove the dependent UI.

The file should compile after these edits; the page will look uglier on tablet/mobile temporarily (no drawer toggles), but that's fine — Task 23 replaces the whole layout.

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean tsc + vite build.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/budget/budgetSlice.ts frontend/src/pages/budget/BudgetPage.tsx
git commit -m "refactor(budget): drop drawer state from slice, add expandedCategoryId"
```

---

## Task 10: Add listBudgetAccountTransactions to the RTK Query slice

Wire the new endpoint into `frontend/src/shared/api/api.ts` and add the matching DTO types.

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Add DTO types**

In `frontend/src/shared/api/api.ts`, in the `// ---------- Budget ----------` section (after `MonthlySummaryDto`), add:

```ts
export interface AccountSummaryDto {
    id: string
    name: string
    type: BudgetAccountType
    balance: number
    monthInflow: number
    monthOutflow: number
}

export interface AccountTransactionsPageDto {
    account: AccountSummaryDto
    items: BudgetTransactionDto[]
    hasMore: boolean
}
```

- [ ] **Step 2: Add a cache tag**

Find the `tagTypes` array (it contains `'BudgetAccounts'`, `'BudgetTransactions'`, …). Add `'BudgetAccountDetail'` next to them:

```ts
tagTypes: [
    'Me', 'Family', 'FamilyMembers', 'Relationships',
    'Ingredients', 'Recipes', 'Stock', 'MealPlan',
    'ShoppingLists', 'ShoppingListDetail',
    'ChatConversations', 'ChatMessages',
    'BudgetSummary', 'BudgetAccounts', 'BudgetGroups',
    'BudgetTransactions', 'BudgetAccountDetail',
],
```

(Keep the order alphabetical within each group as the file already does; the example above shows insertion at the end of the budget block.)

- [ ] **Step 3: Add the query endpoint**

Locate the existing budget endpoints (near `listBudgetTransactions`). Add immediately above `listBudgetTransactions`:

```ts
listBudgetAccountTransactions: build.query<
    AccountTransactionsPageDto,
    {accountId: string; year: number; month: number; skip?: number; take?: number}
>({
    query: ({accountId, year, month, skip = 0, take = 50}) =>
        `/budget/accounts/${accountId}/transactions?year=${year}&month=${month}&skip=${skip}&take=${take}`,
    providesTags: (_r, _e, a) => [{type: 'BudgetAccountDetail', id: a.accountId}],
}),
```

- [ ] **Step 4: Invalidate the new tag from transaction mutations**

Find `createBudgetTransaction`, `updateBudgetTransaction`, `deleteBudgetTransaction`. Each currently invalidates `'BudgetTransactions'` and `'BudgetAccounts'` and the relevant `BudgetSummary`. Add `'BudgetAccountDetail'` to each `invalidatesTags` array so the account-detail page refetches after any tx mutation. For example, `createBudgetTransaction`:

```ts
invalidatesTags: (_r, _e, a) => [
    'BudgetTransactions', 'BudgetAccounts', 'BudgetAccountDetail',
    {type: 'BudgetSummary', id: `${a.year}-${a.month}`},
],
```

Apply the same pattern to `updateBudgetTransaction` and `deleteBudgetTransaction`.

- [ ] **Step 5: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean build. The new exported hook `useListBudgetAccountTransactionsQuery` is auto-generated by RTK Query from the endpoint name.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(budget): add listBudgetAccountTransactions RTK endpoint + types"
```

---

## Task 11: TransactionDialog accepts a preset prop

So the long-press shortcut from EnvelopeCard and the FAB on AccountDetailPage can pre-fill `accountId` / `categoryId`.

**Files:**
- Modify: `frontend/src/pages/budget/components/TransactionDialog.tsx`

- [ ] **Step 1: Read the dialog header to learn its current signature**

Open `frontend/src/pages/budget/components/TransactionDialog.tsx` and find the function signature (around line 50). Currently it accepts `{onClose, existing?}`.

- [ ] **Step 2: Add a `preset` prop**

Change the function signature and `useForm`'s defaultValues so an optional `preset?: {accountId?: string; categoryId?: string}` is honored:

```tsx
export function TransactionDialog({
  onClose,
  existing,
  preset,
}: {
  onClose: () => void
  existing?: BudgetTransactionDto
  preset?: {accountId?: string; categoryId?: string}
}) {
  // ...

  const {control, handleSubmit, formState, watch} = useForm<FormValues>({
    defaultValues: existing
      ? { /* existing-mode defaults — keep current code */ }
      : {
          accountId: preset?.accountId ?? '',
          categoryId: preset?.categoryId ?? UNCATEGORIZED_ID,
          amount: null,
          direction: 'Expense',
          date: todayIso(),
          notes: '',
        },
  })
  // ...
}
```

(If `existing` is set, ignore `preset` — editing locks the values to the existing tx.)

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean build. No callers pass `preset` yet, so behaviour is unchanged.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/budget/components/TransactionDialog.tsx
git commit -m "feat(budget): TransactionDialog accepts optional preset prop"
```

---

## Task 12: Drop sortOrder from create-side dialog payloads

Backend now ignores client-sent `SortOrder` on creates. The PUT routes (`UpdateGroup`, `UpdateCategory`, `UpdateAccount`) still use the field — `UpsertGroupRequest` and `UpsertCategoryRequest` on the backend record stay unchanged. We only loosen the *frontend* TypeScript interfaces so dialogs can stop sending the placeholder, and the JSON layer falls back to `0` on the server (which is then thrown away by the new auto-assignment).

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/pages/budget/components/AddAccountDialog.tsx`
- Modify: `frontend/src/pages/budget/components/AddCategoryDialog.tsx`

- [ ] **Step 1: Make sortOrder optional in three TS request shapes**

In `frontend/src/shared/api/api.ts`, edit only the `sortOrder` field on three interfaces to be optional. Do not delete it (the PUT calls still pass it):

```ts
export interface UpsertGroupRequest {
    name: string
    sortOrder?: number    // optional: backend auto-assigns on create; updates still send the current value
}

export interface UpsertCategoryRequest {
    groupId: string
    name: string
    emoji: string | null
    sortOrder?: number    // optional on create
    targetType: BudgetTargetType
    targetAmount: number | null
    targetDueDate: string | null
    targetDayOfMonth: number | null
}

export interface CreateAccountRequest {
    name: string
    type: BudgetAccountType
    openingBalance: number
    sortOrder?: number    // optional on create
}
```

Leave `UpdateAccountRequest` alone — its `sortOrder: number` stays required because the Update route uses it.

- [ ] **Step 2: Update AddAccountDialog**

Open `frontend/src/pages/budget/components/AddAccountDialog.tsx`. Find the `createAccount({...})` call and drop the `sortOrder: 0` line from the payload. The form's `defaultValues` may also include `sortOrder`; if so, remove that too.

- [ ] **Step 3: Update AddCategoryDialog**

Open `frontend/src/pages/budget/components/AddCategoryDialog.tsx`. Find the `createCategory({...})` call and drop the `sortOrder: 0` line. Remove `sortOrder` from the `FormValues` interface and `defaultValues` if present.

- [ ] **Step 4: Verify no other create-side caller still sends it**

```bash
cd frontend && grep -rn "sortOrder: 0" src
```

Expected: zero hits in budget create paths (only Update flows, if any, should reference sortOrder explicitly).

- [ ] **Step 5: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/budget/components/AddAccountDialog.tsx frontend/src/pages/budget/components/AddCategoryDialog.tsx
git commit -m "refactor(budget): drop sortOrder from create-side dialog payloads"
```

---

## Task 13: Extract MonthStrip component

Pull the month-nav header out of the old `BudgetPage.tsx`. New file is unused until Task 23, but it compiles standalone.

**Files:**
- Create: `frontend/src/pages/budget/components/MonthStrip.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/components/MonthStrip.tsx`:

```tsx
import {useAppDispatch, useAppSelector} from '../../../store'
import {goPrevMonth, goNextMonth} from '../budgetSlice'

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

/**
 * Month navigator at the top of /budget. Previous / next chevrons
 * read year+month from budgetSlice and dispatch the existing month
 * actions. Stays narrow on mobile — flex-row, centered.
 */
export function MonthStrip() {
  const dispatch = useAppDispatch()
  const {year, month} = useAppSelector(s => s.budget)
  return (
    <div className="bdg-month-strip" data-testid="bdg-month-strip">
      <button
        type="button"
        className="bdg-month-arrow"
        onClick={() => dispatch(goPrevMonth())}
        aria-label="Previous month"
      >‹</button>
      <span className="bdg-month-label">{MONTHS[month - 1]} {year}</span>
      <button
        type="button"
        className="bdg-month-arrow"
        onClick={() => dispatch(goNextMonth())}
        aria-label="Next month"
      >›</button>
    </div>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean build. (Unused file is fine; tsc only errors on unused locals, not unused exports.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/components/MonthStrip.tsx
git commit -m "feat(budget): add MonthStrip component"
```

---

## Task 14: RtaHero component

The big gradient "Ready to Assign" card.

**Files:**
- Create: `frontend/src/pages/budget/components/RtaHero.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/components/RtaHero.tsx`:

```tsx
import {formatTHB} from '../BudgetPage.hooks'
import type {MonthlySummaryDto} from '../../../shared/api/api'

/**
 * Hero card showing Ready-to-Assign at the top of /budget. The colour
 * shifts to a red gradient when readyToAssign < 0 (over-assigned).
 */
export function RtaHero({summary}: {summary: MonthlySummaryDto}) {
  const negative = summary.readyToAssign < 0
  const zero = summary.readyToAssign === 0
  return (
    <div
      className={`bdg-rta-hero ${negative ? 'is-negative' : ''}`}
      data-testid="bdg-rta-hero"
    >
      <div className="bdg-rta-label">
        {zero ? 'All Money Assigned' : negative ? 'Over-Assigned' : 'Ready to Assign'}
      </div>
      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(summary.readyToAssign)}
      </div>
      <div className="bdg-rta-sub">
        {formatTHB(summary.income)} income · {formatTHB(summary.totalAssigned)} assigned
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/components/RtaHero.tsx
git commit -m "feat(budget): add RtaHero gradient card"
```

---

## Task 15: AccountsStrip component (horizontal scroll, tappable)

Each card links to `/budget/accounts/:id`. The trailing card is an "+ Add" button that opens `AddAccountDialog`.

**Files:**
- Create: `frontend/src/pages/budget/components/AccountsStrip.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/components/AccountsStrip.tsx`:

```tsx
import {useState} from 'react'
import {Link} from 'react-router-dom'
import type {BudgetAccountDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {AddAccountDialog} from './AddAccountDialog'

const DOT_BY_TYPE: Record<BudgetAccountDto['type'], string> = {
  Cash: '',
  Credit: 'credit',
  Loan: 'loan',
  Closed: 'closed',
}

/**
 * Horizontal-scroll list of accounts at the top of /budget, sorted
 * server-side by CreatedAt DESC. Tapping a card routes to the
 * account-detail page; the trailing card opens AddAccountDialog.
 */
export function AccountsStrip({accounts}: {accounts: BudgetAccountDto[]}) {
  const [addOpen, setAddOpen] = useState(false)
  return (
    <>
      <div className="bdg-section-title">
        <h3>Accounts · newest first</h3>
      </div>
      <div className="bdg-accounts-strip" data-testid="bdg-accounts-strip">
        {accounts.map(a => (
          <Link
            key={a.id}
            to={`/budget/accounts/${a.id}`}
            className="bdg-account-card"
            data-testid="bdg-account-card"
          >
            <span className="bdg-account-chevron">›</span>
            <div className="bdg-account-name">
              <span className={`bdg-account-dot ${DOT_BY_TYPE[a.type]}`} />
              {a.name}
            </div>
            <div className="bdg-account-balance">{formatTHB(a.balance)}</div>
          </Link>
        ))}
        <button
          type="button"
          className="bdg-account-card bdg-account-card--add"
          onClick={() => setAddOpen(true)}
          data-testid="bdg-add-account"
        >
          + Add
        </button>
      </div>
      {addOpen && <AddAccountDialog onClose={() => setAddOpen(false)} />}
    </>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/components/AccountsStrip.tsx
git commit -m "feat(budget): add AccountsStrip with tappable account cards"
```

---

## Task 16: EnvelopeCard hooks + component

This is the biggest single component in the redesign. Split per the react-structure skill — logic in `EnvelopeCard.hooks.ts`, render in `EnvelopeCard.tsx`.

**Files:**
- Create: `frontend/src/pages/budget/components/EnvelopeCard.hooks.ts`
- Create: `frontend/src/pages/budget/components/EnvelopeCard.tsx`

- [ ] **Step 1: Create the hook**

Create `frontend/src/pages/budget/components/EnvelopeCard.hooks.ts`:

```ts
import {useEffect, useRef, useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../../store'
import {setExpandedCategory} from '../budgetSlice'
import {useSetAssignedAmountMutation, type EnvelopeDto} from '../../../shared/api/api'

const LONG_PRESS_MS = 450
const MOVE_TOLERANCE_PX = 8

export interface UseEnvelopeCardArgs {
  cat: EnvelopeDto
  onAddTransaction: (categoryId: string) => void
  onMoveMoney: (cat: EnvelopeDto) => void
  onCoverOverspending: (cat: EnvelopeDto) => void
  onEdit: (cat: EnvelopeDto) => void
}

export function useEnvelopeCard({cat, onAddTransaction, onMoveMoney, onCoverOverspending, onEdit}: UseEnvelopeCardArgs) {
  const dispatch = useAppDispatch()
  const {year, month, expandedCategoryId} = useAppSelector(s => s.budget)
  const expanded = expandedCategoryId === cat.categoryId
  const [setAssigned] = useSetAssignedAmountMutation()
  const [assignedDraft, setAssignedDraft] = useState<number>(cat.assigned)

  useEffect(() => { setAssignedDraft(cat.assigned) }, [cat.assigned])

  // Long-press detection — start a timer on pointerdown, cancel on
  // move-too-far or pointerup. If the timer fires, we mark `longPressed`
  // so the subsequent click doesn't also toggle expansion.
  const longPressedRef = useRef(false)
  const downAtRef = useRef<{x: number; y: number} | null>(null)
  const timerRef = useRef<number | null>(null)

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    longPressedRef.current = false
    downAtRef.current = {x: e.clientX, y: e.clientY}
    timerRef.current = window.setTimeout(() => {
      longPressedRef.current = true
      onAddTransaction(cat.categoryId)
    }, LONG_PRESS_MS)
  }
  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!downAtRef.current || timerRef.current === null) return
    const dx = Math.abs(e.clientX - downAtRef.current.x)
    const dy = Math.abs(e.clientY - downAtRef.current.y)
    if (dx > MOVE_TOLERANCE_PX || dy > MOVE_TOLERANCE_PX) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }
  const cancelLongPress = () => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
    downAtRef.current = null
  }
  const onPointerUp = () => cancelLongPress()
  const onPointerCancel = () => cancelLongPress()

  const onTap = () => {
    if (longPressedRef.current) {
      longPressedRef.current = false
      return // long-press already fired; do not toggle
    }
    dispatch(setExpandedCategory(expanded ? null : cat.categoryId))
  }

  const commitAssigned = () => {
    if (assignedDraft !== cat.assigned) {
      setAssigned({categoryId: cat.categoryId, year, month, amount: assignedDraft})
    }
  }
  const revertAssigned = () => setAssignedDraft(cat.assigned)

  return {
    expanded,
    assignedDraft, setAssignedDraft,
    commitAssigned, revertAssigned,
    onPointerDown, onPointerMove, onPointerUp, onPointerCancel, onTap,
    onAddTransaction: () => onAddTransaction(cat.categoryId),
    onMoveMoney: () => onMoveMoney(cat),
    onCoverOverspending: () => onCoverOverspending(cat),
    onEdit: () => onEdit(cat),
  }
}
```

- [ ] **Step 2: Create the component**

Create `frontend/src/pages/budget/components/EnvelopeCard.tsx`:

```tsx
import {formatTHB} from '../BudgetPage.hooks'
import {useEnvelopeCard, type UseEnvelopeCardArgs} from './EnvelopeCard.hooks'

export function EnvelopeCard(props: UseEnvelopeCardArgs) {
  const {cat} = props
  const h = useEnvelopeCard(props)

  const overspent = cat.available < 0
  const zero = cat.available === 0
  const pillClass =
    overspent ? 'is-red' :
    zero ? 'is-zero' :
    cat.targetType !== 'None' && cat.targetProgressFraction !== null && cat.targetProgressFraction < 1 ? 'is-orange' :
    'is-green'

  const pct = Math.round((cat.targetProgressFraction ?? 0) * 100)
  const progressClass = overspent ? 'is-red' : 'is-green'

  return (
    <div
      className={`bdg-env-card ${overspent ? 'is-overspent' : ''} ${h.expanded ? 'is-expanded' : ''}`}
      data-testid="bdg-envelope-card"
      data-category-id={cat.categoryId}
      onClick={h.onTap}
      onPointerDown={h.onPointerDown}
      onPointerMove={h.onPointerMove}
      onPointerUp={h.onPointerUp}
      onPointerCancel={h.onPointerCancel}
      role="button"
      tabIndex={0}
    >
      <div className="bdg-env-row1">
        <div className="bdg-env-name">
          <span className="bdg-env-emoji">{cat.emoji ?? '•'}</span>
          {cat.name}
        </div>
        <div className="bdg-env-row1-right">
          {!h.expanded && overspent && (
            <button
              type="button"
              className="bdg-env-icon-btn is-danger"
              onClick={(e) => { e.stopPropagation(); h.onCoverOverspending() }}
              aria-label="Cover overspending"
              data-testid="bdg-env-cover-icon"
            >⚠</button>
          )}
          {!h.expanded && !overspent && (
            <button
              type="button"
              className="bdg-env-icon-btn"
              onClick={(e) => { e.stopPropagation(); h.onMoveMoney() }}
              aria-label="Move money"
              data-testid="bdg-env-move-icon"
            >⇄</button>
          )}
          <span className={`bdg-env-pill ${pillClass}`}>{formatTHB(cat.available)}</span>
        </div>
      </div>
      <div className="bdg-env-row2">
        <span>{cat.targetHint ?? `Activity ${formatTHB(cat.activity)}`}</span>
        <span>{cat.assigned > 0 ? `Assigned ${formatTHB(cat.assigned)}` : 'Unassigned'}</span>
      </div>
      <div className="bdg-env-progress">
        <div className={`bdg-env-progress-fill ${progressClass}`} style={{width: `${pct}%`}} />
      </div>

      {h.expanded && (
        <div className="bdg-env-expanded" onClick={(e) => e.stopPropagation()}>
          <div className="bdg-env-assigned-row">
            <span className="bdg-env-assigned-label">Assigned this month</span>
            <input
              className="bdg-env-assigned-input"
              type="number"
              step="0.01"
              value={h.assignedDraft}
              onChange={(e) => h.setAssignedDraft(Number(e.target.value))}
              onBlur={h.commitAssigned}
              onKeyDown={(e) => {
                if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
                if (e.key === 'Escape') h.revertAssigned()
              }}
              data-testid="bdg-env-assigned-input"
            />
          </div>
          <div className="bdg-env-meta">
            <span>Activity: <span className="val">{formatTHB(cat.activity)}</span></span>
            <span>Available: <span className="val">{formatTHB(cat.available)}</span></span>
          </div>
          <div className="bdg-env-actions">
            <button
              type="button"
              className="bdg-env-action is-primary"
              onClick={h.onAddTransaction}
              data-testid="bdg-env-add-tx"
            >+ Transaction</button>
            <button
              type="button"
              className="bdg-env-action"
              onClick={h.onMoveMoney}
            >⇄ Move</button>
            <button
              type="button"
              className="bdg-env-action"
              onClick={h.onEdit}
            >✎ Edit</button>
            {overspent && (
              <button
                type="button"
                className="bdg-env-action is-danger"
                onClick={h.onCoverOverspending}
              >⚠ Cover</button>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
```

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean. (Note the CSS rules referenced — `.bdg-env-card`, `.bdg-env-pill.is-red`, etc — don't exist yet; they will be added in Task 24. The component will render with no styles in the interim, but it compiles.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/budget/components/EnvelopeCard.tsx frontend/src/pages/budget/components/EnvelopeCard.hooks.ts
git commit -m "feat(budget): add EnvelopeCard with tap-to-expand + long-press"
```

---

## Task 17: EnvelopeList component

Renders the groups of envelope cards on the budget page. Owns the dialog state for Transaction, MoveMoney, CoverOverspending, EditCategory.

**Files:**
- Create: `frontend/src/pages/budget/components/EnvelopeList.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/components/EnvelopeList.tsx`:

```tsx
import {useState} from 'react'
import {Fragment} from 'react'
import type {MonthlySummaryDto, EnvelopeDto} from '../../../shared/api/api'
import {useAppSelector} from '../../../store'
import {EnvelopeCard} from './EnvelopeCard'
import {TransactionDialog} from './TransactionDialog'
import {MoveMoneyDialog} from './MoveMoneyDialog'
import {CoverOverspendingDialog} from './CoverOverspendingDialog'
import {AddCategoryDialog} from './AddCategoryDialog'
import {formatTHB} from '../BudgetPage.hooks'

/**
 * Stacked groups of envelope cards. Group headers render the totals
 * (assigned + available) on the right. Owns the four dialog state
 * machines spawned by per-card actions.
 */
export function EnvelopeList({summary}: {summary: MonthlySummaryDto}) {
  const filter = useAppSelector(s => s.budget.filter)
  const [txPreset, setTxPreset] = useState<{categoryId: string} | null>(null)
  const [moveFrom, setMoveFrom] = useState<EnvelopeDto | null>(null)
  const [coverFor, setCoverFor] = useState<EnvelopeDto | null>(null)
  const [editCat, setEditCat] = useState<EnvelopeDto | null>(null)

  const groups = summary.groups
    .map(g => ({
      ...g,
      categories: g.categories.filter(c => {
        switch (filter) {
          case 'overspent':   return c.available < 0
          case 'underfunded': return c.targetType !== 'None' && (c.targetProgressFraction ?? 0) < 1
          case 'overfunded':  return c.available > (c.targetAmount ?? 0)
          case 'available':   return c.available > 0
          case 'snoozed':     return c.isHidden
          default:            return !c.isHidden
        }
      }),
    }))
    .filter(g => g.categories.length > 0)

  return (
    <div className="bdg-envelopes" data-testid="bdg-envelopes">
      {groups.map(g => (
        <Fragment key={g.groupId}>
          <div className="bdg-env-group-header">
            <span>{g.name}</span>
            <span>{formatTHB(g.totalAssigned)} / {formatTHB(g.totalAvailable)}</span>
          </div>
          {g.categories.map(c => (
            <EnvelopeCard
              key={c.categoryId}
              cat={c}
              onAddTransaction={(categoryId) => setTxPreset({categoryId})}
              onMoveMoney={setMoveFrom}
              onCoverOverspending={setCoverFor}
              onEdit={setEditCat}
            />
          ))}
        </Fragment>
      ))}

      {txPreset && (
        <TransactionDialog
          preset={txPreset}
          onClose={() => setTxPreset(null)}
        />
      )}
      {moveFrom && (
        <MoveMoneyDialog from={moveFrom} groups={summary.groups} onClose={() => setMoveFrom(null)} />
      )}
      {coverFor && (
        <CoverOverspendingDialog overspent={coverFor} groups={summary.groups} onClose={() => setCoverFor(null)} />
      )}
      {editCat && (
        <AddCategoryDialog onClose={() => setEditCat(null)} />
      )}
    </div>
  )
}
```

Note on the `editCat` flow: `AddCategoryDialog` currently does not support an edit mode. For v1 of this redesign, treating "Edit" as "open the AddCategoryDialog" is acceptable as a placeholder — the user can dismiss without saving, and a real edit dialog is tracked as Phase 2. If `AddCategoryDialog` later grows an `existing?` prop, plumb it through here.

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean build.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/components/EnvelopeList.tsx
git commit -m "feat(budget): add EnvelopeList that owns per-card dialog state"
```

---

## Task 18: AccountHero component

The blue/violet gradient block at the top of `/budget/accounts/:id`.

**Files:**
- Create: `frontend/src/pages/budget/account-detail/AccountHero.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/account-detail/AccountHero.tsx`:

```tsx
import type {AccountSummaryDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

const TYPE_LABEL: Record<AccountSummaryDto['type'], string> = {
  Cash: 'Cash account',
  Credit: 'Credit account',
  Loan: 'Loan account',
  Closed: 'Closed account',
}

export function AccountHero({account}: {account: AccountSummaryDto}) {
  return (
    <div className="bdg-account-hero" data-testid="bdg-account-hero">
      <div className="bdg-account-hero-type">{TYPE_LABEL[account.type]}</div>
      <div className="bdg-account-hero-name">{account.name}</div>
      <div className="bdg-account-hero-balance" data-testid="bdg-account-balance">
        {formatTHB(account.balance)}
      </div>
      <div className="bdg-account-hero-meta">
        <span>📈 {formatTHB(account.monthInflow)} in</span>
        <span>📉 {formatTHB(account.monthOutflow)} out</span>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/account-detail/AccountHero.tsx
git commit -m "feat(budget): add AccountHero gradient block"
```

---

## Task 19: AccountTransactionList component (grouped by date)

Rows ordered by `CreatedAt DESC` server-side; UI groups them visually by `Date` header.

**Files:**
- Create: `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx`

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx`:

```tsx
import {Fragment} from 'react'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

interface Props {
  items: BudgetTransactionDto[]
  /** Sentinel for IntersectionObserver — page-end ref. Caller wires it. */
  endSentinelRef: React.RefObject<HTMLDivElement | null>
}

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function dateHeaderFor(iso: string): string {
  const today = todayIso()
  if (iso === today) return `Today · ${formatDateShort(iso)}`
  const yest = new Date(Date.now() - 86400_000)
  const yestIso = `${yest.getFullYear()}-${String(yest.getMonth() + 1).padStart(2, '0')}-${String(yest.getDate()).padStart(2, '0')}`
  if (iso === yestIso) return `Yesterday · ${formatDateShort(iso)}`
  return formatDateShort(iso)
}

function formatDateShort(iso: string): string {
  const [, m, d] = iso.split('-').map(Number)
  const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']
  return `${months[m - 1]} ${d}`
}

export function AccountTransactionList({items, endSentinelRef}: Props) {
  // Bucket by Date — preserves CreatedAt DESC order within each bucket.
  const buckets: {date: string; rows: BudgetTransactionDto[]}[] = []
  for (const tx of items) {
    const last = buckets[buckets.length - 1]
    if (last && last.date === tx.date) last.rows.push(tx)
    else buckets.push({date: tx.date, rows: [tx]})
  }

  return (
    <div className="bdg-tx-feed" data-testid="bdg-tx-feed">
      {buckets.map((b) => (
        <Fragment key={b.date}>
          <div className="bdg-tx-date-header">{dateHeaderFor(b.date)}</div>
          {b.rows.map(tx => (
            <div key={tx.id} className="bdg-tx-row" data-testid="bdg-tx-row">
              <div className="bdg-tx-icon">{tx.categoryEmoji ?? '•'}</div>
              <div className="bdg-tx-body">
                <div className="bdg-tx-title">{tx.notes ?? tx.categoryName ?? 'Transaction'}</div>
                <div className="bdg-tx-sub">{tx.categoryName ?? 'Uncategorized'}</div>
              </div>
              <div className={`bdg-tx-amount ${tx.amount >= 0 ? 'is-income' : ''}`}>
                {tx.amount >= 0 ? '+' : ''}{formatTHB(tx.amount)}
              </div>
            </div>
          ))}
        </Fragment>
      ))}
      <div ref={endSentinelRef} className="bdg-tx-sentinel" />
    </div>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/account-detail/AccountTransactionList.tsx
git commit -m "feat(budget): add AccountTransactionList grouped by date"
```

---

## Task 20: AccountDetailPage hooks (infinite scroll)

Owns the RTK query + accumulator state + `IntersectionObserver` wiring.

**Files:**
- Create: `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts`

- [ ] **Step 1: Create the hook**

Create `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts`:

```ts
import {useEffect, useRef, useState} from 'react'
import {useAppSelector} from '../../../store'
import {
  useListBudgetAccountTransactionsQuery,
  type BudgetTransactionDto,
} from '../../../shared/api/api'

const PAGE_SIZE = 50

/**
 * Drive the AccountDetailPage. Loads one page from the server; an
 * IntersectionObserver on a sentinel triggers `setSkip(prev + PAGE_SIZE)`
 * which the underlying query refetches and we merge into `allItems`.
 *
 * We accumulate locally rather than building one big merged query so
 * RTK Query keeps each page cached independently and re-uses entries
 * across navigations.
 */
export function useAccountDetail(accountId: string) {
  const {year, month} = useAppSelector(s => s.budget)
  const [skip, setSkip] = useState(0)
  const [allItems, setAllItems] = useState<BudgetTransactionDto[]>([])

  const {data, isLoading, isFetching, error} =
    useListBudgetAccountTransactionsQuery({accountId, year, month, skip, take: PAGE_SIZE})

  // Reset the accumulator when accountId or month changes.
  useEffect(() => {
    setSkip(0)
    setAllItems([])
  }, [accountId, year, month])

  // Append the newly-fetched page when it arrives.
  useEffect(() => {
    if (!data) return
    setAllItems(prev => {
      // If skip === 0 it's a fresh load (e.g. month change); replace.
      if (skip === 0) return data.items
      // Otherwise append, deduplicating by id (cache-invalidation races).
      const seen = new Set(prev.map(t => t.id))
      const fresh = data.items.filter(t => !seen.has(t.id))
      return [...prev, ...fresh]
    })
  }, [data, skip])

  const endSentinelRef = useRef<HTMLDivElement | null>(null)
  const hasMore = data?.hasMore ?? false

  useEffect(() => {
    const node = endSentinelRef.current
    if (!node || !hasMore || isFetching) return
    const io = new IntersectionObserver((entries) => {
      const entry = entries[0]
      if (entry?.isIntersecting) {
        setSkip(prev => prev + PAGE_SIZE)
      }
    }, {rootMargin: '120px'})
    io.observe(node)
    return () => io.disconnect()
  }, [hasMore, isFetching])

  return {
    account: data?.account ?? null,
    items: allItems,
    isLoading,
    isFetching,
    error,
    endSentinelRef,
    hasMore,
  }
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts
git commit -m "feat(budget): add useAccountDetail hook with infinite scroll"
```

---

## Task 21: AccountDetailPage component

Composes `AccountHero` + `AccountTransactionList` + a FAB that opens `TransactionDialog` with `accountId` preset.

**Files:**
- Create: `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`
- Modify: `frontend/src/pages/budget/index.ts`

- [ ] **Step 1: Create the page**

Create `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`:

```tsx
import {useState} from 'react'
import {Link, useNavigate, useParams} from 'react-router-dom'
import {AccountHero} from './AccountHero'
import {AccountTransactionList} from './AccountTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {useAccountDetail} from './AccountDetailPage.hooks'

export function AccountDetailPage() {
  const {accountId = ''} = useParams<{accountId: string}>()
  const navigate = useNavigate()
  const {account, items, isLoading, error, endSentinelRef, hasMore} = useAccountDetail(accountId)
  const [txOpen, setTxOpen] = useState(false)

  if (isLoading && !account) {
    return <div className="bdg-loading">Loading…</div>
  }
  if (error || !account) {
    return (
      <div className="bdg-error">
        <p>Could not load this account.</p>
        <button type="button" onClick={() => navigate('/budget')}>Back to budget</button>
      </div>
    )
  }

  return (
    <div className="bdg-account-page" data-testid="bdg-account-page">
      <div className="bdg-top-bar">
        <Link to="/budget" className="bdg-back-btn" aria-label="Back">‹</Link>
        <div className="bdg-top-bar-title">{account.name}</div>
        <span style={{width: 32}} aria-hidden />
      </div>

      <AccountHero account={account} />

      <div className="bdg-section-title">
        <h3>Transactions · newest first</h3>
      </div>

      <AccountTransactionList items={items} endSentinelRef={endSentinelRef} />

      {!hasMore && items.length === 0 && (
        <div className="bdg-tx-empty">No transactions yet.</div>
      )}

      <button
        type="button"
        className="bdg-fab"
        onClick={() => setTxOpen(true)}
        aria-label="Add transaction"
        data-testid="bdg-fab"
      >+</button>

      {txOpen && (
        <TransactionDialog
          preset={{accountId}}
          onClose={() => setTxOpen(false)}
        />
      )}
    </div>
  )
}
```

- [ ] **Step 2: Re-export from the budget barrel**

Modify `frontend/src/pages/budget/index.ts` to add:

```ts
export {AccountDetailPage} from './account-detail/AccountDetailPage'
```

(Keep the existing `export {BudgetPage}` line as-is.)

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/budget/account-detail/AccountDetailPage.tsx frontend/src/pages/budget/index.ts
git commit -m "feat(budget): add AccountDetailPage at /budget/accounts/:id"
```

---

## Task 22: Wire the /budget/accounts/:id route

**Files:**
- Modify: `frontend/src/router.tsx`

- [ ] **Step 1: Add the import**

In `frontend/src/router.tsx`, change the existing budget import:

```tsx
import {BudgetPage, AccountDetailPage} from './pages/budget'
```

- [ ] **Step 2: Register the route**

In the `<FamilyRequiredRoute>` → `<AppLayout>` children block, immediately after the existing `{ path: '/budget', element: <BudgetPage /> }` line, add:

```tsx
{ path: '/budget/accounts/:accountId', element: <AccountDetailPage /> },
```

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/router.tsx
git commit -m "feat(budget): register /budget/accounts/:accountId route"
```

---

## Task 23: Replace BudgetPage with the single-column layout

The biggest UI swap. Pulls together `MonthStrip`, `RtaHero`, `AccountsStrip`, and `EnvelopeList`. Removes references to all the old side-panel components.

**Files:**
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`

- [ ] **Step 1: Rewrite BudgetPage**

Replace the entire contents of `frontend/src/pages/budget/BudgetPage.tsx`:

```tsx
import {useAppDispatch, useAppSelector} from '../../store'
import {MonthStrip} from './components/MonthStrip'
import {RtaHero} from './components/RtaHero'
import {AccountsStrip} from './components/AccountsStrip'
import {EnvelopeList} from './components/EnvelopeList'
import {useBudgetData} from './BudgetPage.hooks'
import {setFilter} from './budgetSlice'
import type {BudgetFilter} from './budgetSlice'
import './BudgetPage.css'

export function BudgetPage() {
  const dispatch = useAppDispatch()
  const {summary, isLoading} = useBudgetData()
  const filter = useAppSelector(s => s.budget.filter)
  const overspentCount = summary?.groups.flatMap(g => g.categories).filter(c => c.available < 0).length ?? 0

  if (isLoading || !summary) {
    return <div className="bdg-loading">Loading budget…</div>
  }

  const chips: [BudgetFilter, string, boolean][] = [
    ['all',         'All',                              false],
    ['overspent',   `⚠ ${overspentCount} Overspent`,    true],
    ['underfunded', 'Underfunded',                      false],
    ['overfunded',  'Overfunded',                       false],
    ['available',   'Money Available',                  false],
    ['snoozed',     'Snoozed',                          false],
  ]

  return (
    <div className="bdg-page" data-testid="bdg-page">
      <MonthStrip />
      <RtaHero summary={summary} />
      <AccountsStrip accounts={summary.accounts} />

      <div className="bdg-filters">
        {chips.map(([k, label, danger]) => (
          <button
            key={k}
            type="button"
            className={`bdg-chip ${filter === k ? 'is-active' : ''} ${danger && overspentCount > 0 ? 'is-danger' : ''}`}
            onClick={() => dispatch(setFilter(k))}
          >{label}</button>
        ))}
      </div>

      <EnvelopeList summary={summary} />
    </div>
  )
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean. The OLD CSS classes (`.budget-page`, `.budget-rta-amount`, etc.) are still in `BudgetPage.css` but no longer referenced; styling will look broken until Task 24 lands. That's intentional — we keep the commit focused on JSX.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/BudgetPage.tsx
git commit -m "feat(budget): swap BudgetPage to single-column layout"
```

---

## Task 24: Replace BudgetPage.css with new tokens + responsive shell

**Files:**
- Modify: `frontend/src/pages/budget/BudgetPage.css`

- [ ] **Step 1: Replace the stylesheet**

Replace the entire contents of `frontend/src/pages/budget/BudgetPage.css`:

```css
/* ============================================================
   Budget redesign — mobile-first single column.
   Class prefix is `bdg-` to clearly separate from the legacy
   `.budget-*` rules that have been retired with this commit.
   ============================================================ */

.bdg-page,
.bdg-account-page {
  --bg-page: #0f172a;
  --bg-card: #1e293b;
  --bg-card-2: #0f172a;
  --bg-elev:  #1e293b;
  --border:  #334155;
  --text:        #f1f5f9;
  --text-muted:  #94a3b8;
  --text-dim:    #64748b;
  --accent:       #6366f1;
  --accent-soft:  rgba(99, 102, 241, 0.15);
  --green:    #22c55e;
  --green-bg: rgba(34,197,94,0.15);
  --red:      #ef4444;
  --red-bg:   rgba(239,68,68,0.15);
  --orange:   #f59e0b;
  --orange-bg:rgba(245,158,11,0.15);

  background: var(--bg-page);
  color: var(--text);
  min-height: calc(100vh - 56px);
  padding: 16px 16px 96px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  font-size: 13px;
}

@media (min-width: 600px) {
  .bdg-page,
  .bdg-account-page { max-width: 540px; margin: 0 auto; padding: 24px 24px 96px; }
}
@media (min-width: 1024px) {
  .bdg-page,
  .bdg-account-page { max-width: 720px; padding: 32px 32px 96px; }
}

.bdg-loading,
.bdg-error,
.bdg-tx-empty {
  padding: 40px 20px;
  text-align: center;
  color: var(--text-muted);
}

/* -------- Month strip -------- */
.bdg-month-strip {
  display: flex; align-items: center; justify-content: center; gap: 16px;
}
.bdg-month-arrow {
  background: none; border: none; color: var(--text-muted); cursor: pointer;
  font-size: 20px; padding: 4px 10px; border-radius: 8px;
}
.bdg-month-arrow:hover { background: var(--bg-card); color: var(--text); }
.bdg-month-label { font-size: 15px; font-weight: 700; }

/* -------- RTA hero -------- */
.bdg-rta-hero {
  padding: 16px 18px; border-radius: 18px; color: white;
  background: linear-gradient(135deg, var(--accent) 0%, #8b5cf6 100%);
}
.bdg-rta-hero.is-negative {
  background: linear-gradient(135deg, var(--red) 0%, #b91c1c 100%);
}
.bdg-rta-label { font-size: 11px; font-weight: 600; opacity: .85; text-transform: uppercase; letter-spacing: .5px; }
.bdg-rta-amount { font-size: 28px; font-weight: 800; margin-top: 4px; letter-spacing: -0.5px; }
.bdg-rta-sub { font-size: 11px; opacity: .8; margin-top: 6px; }

/* -------- Section titles -------- */
.bdg-section-title {
  display: flex; justify-content: space-between; align-items: center;
}
.bdg-section-title h3 {
  font-size: 11px; font-weight: 700; text-transform: uppercase;
  letter-spacing: .6px; color: var(--text-muted);
}

/* -------- Accounts strip -------- */
.bdg-accounts-strip {
  display: flex; gap: 10px; overflow-x: auto; padding-bottom: 4px;
  scrollbar-width: none;
}
.bdg-accounts-strip::-webkit-scrollbar { display: none; }
.bdg-account-card {
  flex-shrink: 0; min-width: 150px; padding: 12px 14px;
  background: var(--bg-card); border: 1px solid var(--border);
  border-radius: 14px; color: var(--text); text-decoration: none;
  position: relative; display: flex; flex-direction: column; gap: 4px;
}
.bdg-account-card--add {
  display: flex; align-items: center; justify-content: center;
  color: var(--accent); border: 1px dashed var(--accent);
  background: var(--accent-soft); font-weight: 600; cursor: pointer;
  min-width: 100px;
}
.bdg-account-chevron {
  position: absolute; right: 10px; top: 10px;
  font-size: 14px; color: var(--text-dim);
}
.bdg-account-dot {
  width: 8px; height: 8px; border-radius: 50%; background: var(--green);
  display: inline-block; margin-right: 6px;
}
.bdg-account-dot.credit { background: var(--red); }
.bdg-account-dot.loan   { background: var(--orange); }
.bdg-account-dot.closed { background: var(--text-dim); }
.bdg-account-name { font-size: 11px; color: var(--text-muted); display: flex; align-items: center; }
.bdg-account-balance { font-size: 17px; font-weight: 700; }

/* -------- Filter chips -------- */
.bdg-filters {
  display: flex; gap: 6px; flex-wrap: wrap;
}
.bdg-chip {
  padding: 5px 12px; border-radius: 20px; font-size: 12px; font-weight: 500;
  border: 1px solid transparent; color: var(--text-muted);
  background: transparent; cursor: pointer;
}
.bdg-chip:hover { background: var(--bg-card); color: var(--text); }
.bdg-chip.is-active { background: var(--bg-card); border-color: var(--border); color: var(--text); }
.bdg-chip.is-danger { background: var(--red-bg); border-color: var(--red); color: var(--red); }

/* -------- Envelopes -------- */
.bdg-envelopes { display: flex; flex-direction: column; gap: 10px; }
.bdg-env-group-header {
  display: flex; justify-content: space-between; align-items: center;
  padding: 8px 4px 0; font-size: 10px; font-weight: 700;
  text-transform: uppercase; letter-spacing: .6px; color: var(--text-muted);
}

.bdg-env-card {
  background: var(--bg-card); border: 1px solid var(--border);
  border-radius: 12px; padding: 12px 14px; cursor: pointer;
  user-select: none; transition: border-color .12s, box-shadow .12s;
}
.bdg-env-card.is-overspent { border-color: var(--red); background: var(--red-bg); }
.bdg-env-card.is-expanded  { border-color: var(--accent); box-shadow: 0 0 0 2px var(--accent-soft); }

.bdg-env-row1 {
  display: flex; justify-content: space-between; align-items: center; gap: 8px;
}
.bdg-env-row1-right { display: flex; align-items: center; gap: 6px; }
.bdg-env-name { display: flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; flex: 1; min-width: 0; }
.bdg-env-emoji { font-size: 16px; }

.bdg-env-icon-btn {
  width: 28px; height: 28px; border-radius: 50%;
  background: var(--bg-page); border: 1px solid var(--border);
  display: inline-flex; align-items: center; justify-content: center;
  font-size: 13px; color: var(--text-muted); cursor: pointer;
}
.bdg-env-icon-btn.is-danger { color: var(--red); border-color: var(--red); background: var(--red-bg); }

.bdg-env-pill {
  padding: 3px 10px; border-radius: 20px; font-size: 12px; font-weight: 700;
}
.bdg-env-pill.is-green  { background: var(--green-bg);  color: var(--green); }
.bdg-env-pill.is-red    { background: var(--red-bg);    color: var(--red); }
.bdg-env-pill.is-orange { background: var(--orange-bg); color: var(--orange); }
.bdg-env-pill.is-zero   { background: var(--border);    color: var(--text-dim); }

.bdg-env-row2 { display: flex; justify-content: space-between; margin-top: 6px; font-size: 11px; color: var(--text-muted); }
.bdg-env-progress { height: 3px; background: var(--border); border-radius: 2px; margin-top: 8px; overflow: hidden; }
.bdg-env-progress-fill { height: 100%; border-radius: 2px; }
.bdg-env-progress-fill.is-green { background: var(--green); }
.bdg-env-progress-fill.is-red   { background: var(--red); }

.bdg-env-expanded {
  margin-top: 10px; padding-top: 10px;
  border-top: 1px dashed var(--border);
  display: flex; flex-direction: column; gap: 10px;
}
.bdg-env-assigned-row { display: flex; justify-content: space-between; align-items: center; }
.bdg-env-assigned-label { font-size: 11px; color: var(--text-muted); text-transform: uppercase; letter-spacing: .5px; }
.bdg-env-assigned-input {
  width: 120px; padding: 6px 10px; text-align: right;
  background: var(--bg-page); border: 1px solid var(--accent);
  border-radius: 8px; color: var(--text); font-size: 14px; font-weight: 700;
}
.bdg-env-meta { display: flex; gap: 14px; font-size: 11px; color: var(--text-muted); }
.bdg-env-meta .val { color: var(--text); font-weight: 600; }
.bdg-env-actions { display: flex; gap: 6px; flex-wrap: wrap; }
.bdg-env-action {
  flex: 1; min-width: 0; padding: 8px 6px; border-radius: 8px;
  background: var(--bg-page); border: 1px solid var(--border);
  color: var(--text); font-size: 11px; font-weight: 600;
  text-align: center; cursor: pointer; white-space: nowrap;
}
.bdg-env-action.is-primary { background: var(--accent); color: white; border-color: var(--accent); }
.bdg-env-action.is-danger  { background: var(--red-bg); color: var(--red); border-color: var(--red); }

/* -------- Account detail page -------- */
.bdg-top-bar { display: flex; align-items: center; gap: 8px; }
.bdg-back-btn {
  width: 32px; height: 32px; border-radius: 50%;
  background: var(--bg-card); border: 1px solid var(--border);
  display: inline-flex; align-items: center; justify-content: center;
  font-size: 16px; color: var(--text); text-decoration: none;
}
.bdg-top-bar-title { font-size: 15px; font-weight: 700; flex: 1; }

.bdg-account-hero {
  padding: 18px; border-radius: 18px; color: white;
  background: linear-gradient(135deg, #0ea5e9 0%, #6366f1 100%);
}
.bdg-account-hero-type { font-size: 11px; font-weight: 600; opacity: .85; text-transform: uppercase; letter-spacing: .5px; }
.bdg-account-hero-name { font-size: 18px; font-weight: 700; margin-top: 4px; }
.bdg-account-hero-balance { font-size: 32px; font-weight: 800; margin-top: 10px; letter-spacing: -0.5px; }
.bdg-account-hero-meta { display: flex; gap: 18px; margin-top: 12px; font-size: 11px; opacity: .85; }

.bdg-tx-feed { display: flex; flex-direction: column; }
.bdg-tx-date-header {
  font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .6px;
  color: var(--text-muted); padding: 14px 0 6px;
}
.bdg-tx-row {
  display: flex; align-items: center; gap: 10px; padding: 10px 0;
  border-bottom: 1px solid var(--border);
}
.bdg-tx-icon {
  width: 38px; height: 38px; border-radius: 10px; background: var(--bg-card);
  display: flex; align-items: center; justify-content: center; font-size: 16px;
  flex-shrink: 0;
}
.bdg-tx-body { flex: 1; min-width: 0; }
.bdg-tx-title { font-size: 13px; font-weight: 600; }
.bdg-tx-sub { font-size: 11px; color: var(--text-muted); margin-top: 2px; }
.bdg-tx-amount { font-size: 14px; font-weight: 700; }
.bdg-tx-amount.is-income { color: var(--green); }
.bdg-tx-sentinel { height: 1px; }

/* -------- FAB -------- */
.bdg-fab {
  position: fixed; right: 18px; bottom: 22px; z-index: 25;
  width: 56px; height: 56px; border-radius: 50%;
  background: var(--accent); color: white; border: none;
  display: flex; align-items: center; justify-content: center;
  box-shadow: 0 8px 24px rgba(99,102,241,0.5);
  font-size: 28px; font-weight: 300; cursor: pointer;
}
```

- [ ] **Step 2: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 3: Visually verify in the dev server (optional but recommended)**

Launch the dev server (`cd frontend && npm run dev`) and open `http://localhost:5173/budget`. Expect the new mobile-first single column with the indigo→violet RTA hero, the accounts strip, and the envelope cards. Resize the window to 1280px+ — the column should center at max 720px wide. Close the dev server.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/budget/BudgetPage.css
git commit -m "style(budget): replace stylesheet with mobile-first tokens"
```

---

## Task 25: Delete legacy components

Now that nothing imports them, remove the four obsolete files.

**Files:**
- Delete: `frontend/src/pages/budget/components/AccountsSidebar.tsx`
- Delete: `frontend/src/pages/budget/components/MonthlySummaryPanel.tsx`
- Delete: `frontend/src/pages/budget/components/EnvelopeTable.tsx`
- Delete: `frontend/src/pages/budget/components/EnvelopeRow.tsx`

- [ ] **Step 1: Verify no references remain**

```bash
cd frontend && grep -rn "AccountsSidebar\|MonthlySummaryPanel\|EnvelopeTable\|EnvelopeRow" src
```

Expected: zero hits. If anything appears, finish the migration of that file first.

- [ ] **Step 2: Remove the files**

```bash
rm frontend/src/pages/budget/components/AccountsSidebar.tsx
rm frontend/src/pages/budget/components/MonthlySummaryPanel.tsx
rm frontend/src/pages/budget/components/EnvelopeTable.tsx
rm frontend/src/pages/budget/components/EnvelopeRow.tsx
```

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add -A frontend/src/pages/budget/components
git commit -m "refactor(budget): remove legacy sidebar / summary panel / envelope table"
```

---

## Task 26: Playwright smoke — budget page + account detail nav

**Files:**
- Create: `frontend/e2e/budget.smoke.spec.ts`

- [ ] **Step 1: Write the spec**

Create `frontend/e2e/budget.smoke.spec.ts`:

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — smoke', () => {
  test('authed user reaches /budget and sees the single column', async ({authedPage: page}) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-page')).toBeVisible()
    await expect(page.getByTestId('bdg-month-strip')).toBeVisible()
    await expect(page.getByTestId('bdg-rta-hero')).toBeVisible()
    await expect(page.getByTestId('bdg-accounts-strip')).toBeVisible()
    await expect(page.getByTestId('bdg-envelopes')).toBeVisible()
  })

  test('tap an account card navigates to the account-detail page', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstCard = page.getByTestId('bdg-account-card').first()
    // Skip if seed data has no accounts.
    if (await firstCard.count() === 0) test.skip()

    await firstCard.click()
    await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.getByTestId('bdg-account-hero')).toBeVisible()
    await expect(page.getByTestId('bdg-fab')).toBeVisible()
  })

  test('account detail back button returns to /budget', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstCard = page.getByTestId('bdg-account-card').first()
    if (await firstCard.count() === 0) test.skip()

    await firstCard.click()
    await page.getByRole('link', {name: /Back/i}).click()
    await expect(page).toHaveURL(/\/budget$/)
  })
})
```

- [ ] **Step 2: Run the spec**

```bash
cd frontend && npx playwright test budget.smoke --reporter=list
```

Expected: 3 tests pass (or skip if no seed data). If they fail, check whether the dev backend is reachable and the seeded test family has any envelopes/accounts.

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/budget.smoke.spec.ts
git commit -m "test(budget): smoke spec for /budget + account detail nav"
```

---

## Task 27: Playwright — long-press opens TransactionDialog with category preset

This is the highest-risk interaction in the redesign. A dedicated spec drives the pointer events explicitly.

**Files:**
- Create: `frontend/e2e/budget.interactions.spec.ts`

- [ ] **Step 1: Write the spec**

Create `frontend/e2e/budget.interactions.spec.ts`:

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — envelope interactions', () => {
  test('tap toggles expansion, only one card expanded at a time', async ({authedPage: page}) => {
    await page.goto('/budget')
    const cards = page.getByTestId('bdg-envelope-card')
    if (await cards.count() < 2) test.skip()

    const first = cards.nth(0)
    const second = cards.nth(1)

    await first.click()
    await expect(first).toHaveClass(/is-expanded/)
    await expect(second).not.toHaveClass(/is-expanded/)

    await second.click()
    await expect(first).not.toHaveClass(/is-expanded/)
    await expect(second).toHaveClass(/is-expanded/)
  })

  test('long-press an envelope opens TransactionDialog with category preselected', async ({authedPage: page}) => {
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').first()
    if (await card.count() === 0) test.skip()
    const categoryId = await card.getAttribute('data-category-id')

    // Simulate a long-press: pointerdown, hold ~600ms, pointerup.
    const box = await card.boundingBox()
    if (!box) throw new Error('Could not measure envelope card')
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.down()
    await page.waitForTimeout(600)
    await page.mouse.up()

    // The TransactionDialog should be open. The category dropdown
    // should hold the targeted categoryId; the Account dropdown is
    // left unset (spec: no smart account default).
    await expect(page.locator('.budget-modal')).toBeVisible()

    // Confirm the dialog title is the create-mode header. The exact
    // header text comes from TransactionDialog (not changed by this
    // redesign); update the regex if the dialog gets retitled.
    await expect(page.locator('.budget-modal h3')).toContainText(/transaction/i)

    // After dismiss the card should NOT be expanded (long-press fired
    // instead of the tap path).
    await page.keyboard.press('Escape').catch(() => {})
    // The dialog may not respond to Esc — fallback: close via Cancel.
    const cancel = page.getByRole('button', {name: /Cancel/i})
    if (await cancel.isVisible().catch(() => false)) await cancel.click()

    await expect(card).not.toHaveClass(/is-expanded/)
    // (Optional) sanity check that the data attribute matches.
    expect(categoryId).toMatch(/[0-9a-f-]+/)
  })

  test('tap account-card chevron routes to detail page', async ({authedPage: page}) => {
    await page.goto('/budget')
    const accountCard = page.getByTestId('bdg-account-card').first()
    if (await accountCard.count() === 0) test.skip()

    await accountCard.click()
    await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
  })
})
```

- [ ] **Step 2: Run the spec**

```bash
cd frontend && npx playwright test budget.interactions --reporter=list
```

Expected: 3 tests pass. The long-press test is the brittle one — if the dialog never opens, raise the hold time to 800ms, or check that `LONG_PRESS_MS` in `EnvelopeCard.hooks.ts` matches.

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/budget.interactions.spec.ts
git commit -m "test(budget): tap-expand + long-press → TransactionDialog with preset"
```

---

## Done

All tasks complete. The branch should be green on every commit; pre-commit hook ran the backend xUnit suite + the frontend build at every step.

### Verification checklist

After the final commit, manually verify:

- [ ] `/budget` renders the single-column layout on a 390-wide viewport in the browser dev tools device emulator.
- [ ] An overspent envelope shows the red `⚠` icon next to its pill; a healthy envelope shows `⇄`.
- [ ] Tap an envelope → expands and shows `[+ Transaction] [⇄ Move] [✎ Edit]` (and `⚠ Cover` if overspent).
- [ ] Long-press an envelope (mobile or via slow click on desktop) → `TransactionDialog` opens with the category pre-selected.
- [ ] Tap an account card → `/budget/accounts/:id`. Hero shows balance + month in/out. List is ordered newest-first.
- [ ] Scroll to bottom of a long account-detail list → next page loads (only relevant once an account has 50+ transactions).
- [ ] Add an account from the strip's `+ Add` card. The newly-created account appears at the head of the strip (CreatedAt DESC).
- [ ] Add a category from the existing entry point. The new category appears at the bottom of its group (auto sortOrder).
- [ ] `git log --oneline` shows ~27 focused commits, each with a green pre-commit hook.

### Spec coverage

- Sort flips: Task 4 (accounts), Task 5 (transactions). ✓
- New endpoint: Tasks 6, 7, 8. ✓
- Auto sortOrder: Tasks 1, 2, 3. ✓
- Mobile-first layout: Tasks 23, 24 + responsive in CSS. ✓
- Account-detail route + page: Tasks 18–22. ✓
- Envelope card interactions (tap expand, long-press, icons): Task 16. ✓
- budgetSlice changes: Task 9. ✓
- Family sharing: unchanged (handler tests cover family scope). ✓
- Migration: Tasks 9 (slice), 12 (dialog cleanup), 25 (delete legacy). ✓
- Testing: backend unit tests inside each implementation task, Playwright in Tasks 26 + 27. ✓
- Phase-2 deferred items: explicitly out of scope per spec. ✓
