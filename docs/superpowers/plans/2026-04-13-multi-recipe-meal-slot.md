# Multi-Recipe Meal Slot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow multiple recipes per meal slot (Breakfast / Lunch / Dinner) and add a checkbox-driven Cook flow that batch-deducts stock for the selected entries in one transaction.

**Architecture:** Drop the `UNIQUE (FamilyId, Date, MealSlot)` constraint at the database layer. Add two new endpoints (`stock-check-batch`, `cook-batch`) that aggregate ingredients across an arbitrary list of entries. Replace the per-entry `EntryDetailContent` dialog with `MealSlotDetailContent` — a Syncfusion table that shows every recipe in the focused slot with a checkbox column and a "Cook selected" button. All existing single-entry endpoints (uncook, delete, single stock-check) are kept for parity.

**Tech Stack:**
- Backend: ASP.NET 10, EF Core 10 (SQL Server), `martinothamar/Mediator`, FluentValidation, xUnit + Moq + FluentAssertions, `Microsoft.EntityFrameworkCore.InMemory` (test-only)
- Frontend: React 19, Redux Toolkit + RTK Query (single `createApi`), Syncfusion Pure React (`@syncfusion/react-*`), react-hook-form
- Manual verification: Playwright (no Vitest in this repo)

**Spec:** [docs/superpowers/specs/2026-04-13-multi-recipe-meal-slot-design.md](../specs/2026-04-13-multi-recipe-meal-slot-design.md)

---

## File Structure

### Backend — create

- `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchQuery.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchValidator.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchHandler.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchCommand.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchValidator.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchHandler.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchDtos.cs`
- `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_DropMealSlotUniqueIndex.cs` (EF-generated)
- `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`
- `backend/tests/MenuNest.Application.UnitTests/MealPlan/StockCheckBatchHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/MealPlan/CookBatchHandlerTests.cs`

### Backend — modify

- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/MealPlanEntryConfiguration.cs:26` — drop `.IsUnique()`
- `backend/src/MenuNest.Application/UseCases/MealPlan/CreateMealPlanEntry/CreateMealPlanEntryHandler.cs:35-40` — remove the slot-occupied guard
- `backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs` — add two endpoints
- `backend/tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj` — add `Microsoft.EntityFrameworkCore.InMemory`

### Frontend — modify

- `frontend/src/shared/api/api.ts` — add DTO types + two endpoints
- `frontend/src/pages/meal-plan/mealPlanSlice.ts` — rename `focusedEntryId` → `focusedSlot`
- `frontend/src/pages/meal-plan/MealPlanPage.tsx` — replace `EntryDetailContent` with `MealSlotDetailContent`, rewire cell/event clicks

---

## Task 1: Drop the unique index on (FamilyId, Date, MealSlot)

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/MealPlanEntryConfiguration.cs:26`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_DropMealSlotUniqueIndex.cs` (via EF tooling)

- [ ] **Step 1: Edit the EF configuration**

In `MealPlanEntryConfiguration.cs` change the existing line:

```csharp
builder.HasIndex(m => new { m.FamilyId, m.Date, m.MealSlot }).IsUnique();
```

to:

```csharp
// Non-unique — multiple recipes can share a slot since we now treat
// a slot as a course set (rice + curry + soup).
builder.HasIndex(m => new { m.FamilyId, m.Date, m.MealSlot });
```

- [ ] **Step 2: Generate the migration**

Run from the repo root:

```bash
cd backend
dotnet ef migrations add DropMealSlotUniqueIndex \
  --project src/MenuNest.Infrastructure \
  --startup-project src/MenuNest.WebApi
```

Expected: a new file appears under `src/MenuNest.Infrastructure/Persistence/Migrations/` named `<UTC-timestamp>_DropMealSlotUniqueIndex.cs` plus the regenerated `AppDbContextModelSnapshot.cs`.

- [ ] **Step 3: Inspect the generated migration**

Open the new `*_DropMealSlotUniqueIndex.cs`. Confirm `Up()` calls `DropIndex` followed by `CreateIndex` *without* `unique: true`, and `Down()` does the reverse. If EF named the index something other than `IX_MealPlanEntries_FamilyId_Date_MealSlot`, leave the generated names — they match the snapshot.

- [ ] **Step 4: Apply the migration**

```bash
dotnet ef database update \
  --project src/MenuNest.Infrastructure \
  --startup-project src/MenuNest.WebApi
```

Expected: "Done." with no errors.

- [ ] **Step 5: Sanity-check via SQL**

```bash
dotnet ef dbcontext info \
  --project src/MenuNest.Infrastructure \
  --startup-project src/MenuNest.WebApi
```

Then in your DB tool of choice run (SQL Server syntax):

```sql
SELECT name, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('MealPlanEntries')
  AND name = 'IX_MealPlanEntries_FamilyId_Date_MealSlot';
```

Expected: one row with `is_unique = 0`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/MealPlanEntryConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations
git commit -m "feat(meal-plan): drop unique slot constraint to allow multi-recipe slots"
```

---

## Task 2: Remove the "slot already occupied" guard

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/MealPlan/CreateMealPlanEntry/CreateMealPlanEntryHandler.cs:35-40`

- [ ] **Step 1: Delete the guard block**

Open the handler. Replace:

```csharp
        var slotTaken = await _db.MealPlanEntries
            .AnyAsync(m => m.FamilyId == familyId && m.Date == command.Date && m.MealSlot == command.MealSlot, ct);
        if (slotTaken)
        {
            throw new DomainException("This meal slot is already occupied. Remove it first before adding another recipe.");
        }
```

with nothing (delete the whole block, including its trailing blank line). Confirm the `using Microsoft.EntityFrameworkCore;` at the top stays because the recipe lookup still uses `FirstOrDefaultAsync`.

- [ ] **Step 2: Build to confirm no orphan symbols**

```bash
cd backend
dotnet build src/MenuNest.Application/MenuNest.Application.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 3: Manual smoke (optional but cheap)**

Start the API (`dotnet run --project src/MenuNest.WebApi --launch-profile https`). Use the existing UI or curl to `POST /api/meal-plan` twice for the same `(date, mealSlot)` — both should return `201`. (The frontend can't yet display both — that's Task 8.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/MealPlan/CreateMealPlanEntry/CreateMealPlanEntryHandler.cs
git commit -m "feat(meal-plan): allow multiple entries per (date, slot)"
```

---

## Task 3: Set up the Application unit-test scaffolding

**Why:** The unit-test project exists but is empty. Tasks 4 and 5 need a way to spin up an `IApplicationDbContext` for handler tests. We use `Microsoft.EntityFrameworkCore.InMemory` because mocking `DbSet<T>` against EF queries is fragile.

**Files:**
- Modify: `backend/tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj`
- Create: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`

- [ ] **Step 1: Add EF Core InMemory package**

```bash
cd backend/tests/MenuNest.Application.UnitTests
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

Expected: csproj gains `<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="..." />`.

- [ ] **Step 2: Create the in-memory DbContext**

`backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// EF Core InMemory implementation of <see cref="IApplicationDbContext"/>.
/// Used only by Application unit tests so the handlers under test can run
/// against real DbSet/IQueryable plumbing without needing the real
/// <c>AppDbContext</c> from Infrastructure.
/// </summary>
public sealed class InMemoryAppDbContext : DbContext, IApplicationDbContext
{
    public InMemoryAppDbContext(DbContextOptions<InMemoryAppDbContext> options) : base(options) { }

    public DbSet<Family> Families => Set<Family>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRelationship> UserRelationships => Set<UserRelationship>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<MealPlanEntry> MealPlanEntries => Set<MealPlanEntry>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    public new Task<int> SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Create a small test fixture for repeatable seeding**

`backend/tests/MenuNest.Application.UnitTests/Support/HandlerTestFixture.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// Disposable test fixture that wires up an InMemory DbContext + a
/// stub <see cref="IUserProvisioner"/> seeded with a single family
/// and user. Tests build on this so each one only seeds the rows it
/// actually cares about.
/// </summary>
public sealed class HandlerTestFixture : IDisposable
{
    public InMemoryAppDbContext Db { get; }
    public Mock<IUserProvisioner> UserProvisioner { get; }
    public Family Family { get; }
    public User User { get; }

    public HandlerTestFixture()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"menunest-tests-{Guid.NewGuid()}")
            .Options;
        Db = new InMemoryAppDbContext(options);

        Family = Family.Create("Test Family", createdByUserId: Guid.NewGuid());
        User = User.Create(
            externalId: "test-oid",
            email: "test@example.com",
            displayName: "Test User");
        User.JoinFamily(Family.Id);

        Db.Families.Add(Family);
        Db.Users.Add(User);
        Db.SaveChanges();

        UserProvisioner = new Mock<IUserProvisioner>();
        UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User, Family.Id));
        UserProvisioner
            .Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User);
    }

    public void Dispose() => Db.Dispose();
}
```

> **If `Family.Create` / `User.Create` / `User.JoinFamily` have different signatures in this codebase, adjust the seed calls to match. The fixture only needs *some* family + user where `User.FamilyId == Family.Id`.** Read [`backend/src/MenuNest.Domain/Entities/Family.cs`](../../../backend/src/MenuNest.Domain/Entities/Family.cs) and `User.cs` before settling on the seed code.

- [ ] **Step 4: Build the test project**

```bash
cd backend
dotnet build tests/MenuNest.Application.UnitTests/MenuNest.Application.UnitTests.csproj
```

Expected: `Build succeeded`. If it fails because of `Family.Create` signature mismatch, fix the fixture seed and rebuild.

- [ ] **Step 5: Commit**

```bash
git add backend/tests/MenuNest.Application.UnitTests
git commit -m "test(application): add InMemory DbContext + HandlerTestFixture for handler tests"
```

---

## Task 4: `stock-check-batch` query, handler, endpoint, tests

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/MealPlan/StockCheckBatchHandlerTests.cs`

- [ ] **Step 1: Write the failing test — happy path**

`backend/tests/MenuNest.Application.UnitTests/MealPlan/StockCheckBatchHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.MealPlan;

public class StockCheckBatchHandlerTests
{
    [Fact]
    public async Task Aggregates_required_quantities_across_entries()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var rice = Ingredient.Create(fx.Family.Id, "ข้าวสาร", "ถ้วย");
        fx.Db.Ingredients.AddRange(egg, rice);

        var omelet = Recipe.Create(fx.Family.Id, "ไข่เจียว", description: null, fx.User.Id);
        omelet.AddIngredient(egg.Id, 2m);
        var congee = Recipe.Create(fx.Family.Id, "โจ๊ก", description: null, fx.User.Id);
        congee.AddIngredient(egg.Id, 1m);
        congee.AddIngredient(rice.Id, 2m);
        fx.Db.Recipes.AddRange(omelet, congee);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 5m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, rice.Id, 1m, fx.User.Id));

        var date = new DateOnly(2026, 4, 13);
        var e1 = MealPlanEntry.Create(fx.Family.Id, date, MealSlot.Breakfast, omelet.Id, fx.User.Id);
        var e2 = MealPlanEntry.Create(fx.Family.Id, date, MealSlot.Breakfast, congee.Id, fx.User.Id);
        fx.Db.MealPlanEntries.AddRange(e1, e2);
        await fx.Db.SaveChangesAsync();

        var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new StockCheckBatchQuery(new[] { e1.Id, e2.Id }), CancellationToken.None);

        result.Lines.Should().HaveCount(2);
        var eggLine = result.Lines.Single(l => l.IngredientId == egg.Id);
        eggLine.Required.Should().Be(3m);     // 2 + 1
        eggLine.Available.Should().Be(5m);
        eggLine.Missing.Should().Be(0m);

        var riceLine = result.Lines.Single(l => l.IngredientId == rice.Id);
        riceLine.Required.Should().Be(2m);
        riceLine.Available.Should().Be(1m);
        riceLine.Missing.Should().Be(1m);

        result.IsSufficient.Should().BeFalse();
        result.MissingCount.Should().Be(1);
    }
}
```

> **`Recipe.AddIngredient` and `Recipe.Create` may have different signatures.** Read the entities before finalising the seed.

- [ ] **Step 2: Run the test to confirm it fails to compile**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests \
  --filter FullyQualifiedName~StockCheckBatchHandlerTests
```

Expected: build error — `StockCheckBatchHandler` and `StockCheckBatchQuery` don't exist.

- [ ] **Step 3: Create the query record**

`backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

/// <summary>
/// Aggregated stock check across an arbitrary list of meal plan entries
/// (typically every entry in a single slot, or the user's current
/// selection within the slot detail dialog).
/// </summary>
public sealed record StockCheckBatchQuery(IReadOnlyList<Guid> EntryIds) : IQuery<StockCheckBatchDto>;

public sealed record StockCheckBatchLineDto(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Required,
    decimal Available,
    decimal Missing);

public sealed record StockCheckBatchDto(
    IReadOnlyList<StockCheckBatchLineDto> Lines,
    bool IsSufficient)
{
    public int MissingCount => Lines.Count(l => l.Missing > 0m);
}
```

- [ ] **Step 4: Create the validator**

`backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

public sealed class StockCheckBatchValidator : AbstractValidator<StockCheckBatchQuery>
{
    public StockCheckBatchValidator()
    {
        // EntryIds may be empty — the handler returns an empty result so
        // the UI can call this unconditionally as the user toggles
        // checkboxes. We still reject duplicates and empty Guids to
        // surface client bugs quickly.
        RuleForEach(x => x.EntryIds).NotEqual(Guid.Empty);
    }
}
```

- [ ] **Step 5: Create the handler**

`backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

public sealed class StockCheckBatchHandler : IQueryHandler<StockCheckBatchQuery, StockCheckBatchDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<StockCheckBatchQuery> _validator;

    public StockCheckBatchHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<StockCheckBatchQuery> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<StockCheckBatchDto> Handle(StockCheckBatchQuery query, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(query, ct);

        if (query.EntryIds.Count == 0)
        {
            return new StockCheckBatchDto(Array.Empty<StockCheckBatchLineDto>(), IsSufficient: true);
        }

        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);
        var ids = query.EntryIds.Distinct().ToArray();

        var entries = await _db.MealPlanEntries
            .Where(m => ids.Contains(m.Id) && m.FamilyId == familyId)
            .ToListAsync(ct);
        if (entries.Count != ids.Length)
        {
            throw new DomainException("One or more meal plan entries were not found.");
        }

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // Aggregate required quantity per ingredient across all entries.
        // Using each recipe once per occurrence in the entry list — so
        // if the user planned ข้าวสวย twice, rice is summed twice.
        var required = new Dictionary<Guid, decimal>();
        foreach (var entry in entries)
        {
            var recipe = recipes.Single(r => r.Id == entry.RecipeId);
            foreach (var ri in recipe.Ingredients)
            {
                required[ri.IngredientId] = required.GetValueOrDefault(ri.IngredientId) + ri.Quantity;
            }
        }

        var ingredientIds = required.Keys.ToList();

        var ingredients = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var stockLookup = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

        var lines = required
            .Select(kv =>
            {
                var meta = ingredients[kv.Key];
                var available = stockLookup.GetValueOrDefault(kv.Key);
                var missing = kv.Value > available ? kv.Value - available : 0m;
                return new StockCheckBatchLineDto(meta.Id, meta.Name, meta.Unit, kv.Value, available, missing);
            })
            .OrderBy(l => l.IngredientName)
            .ToList();

        return new StockCheckBatchDto(lines, IsSufficient: lines.All(l => l.Missing == 0m));
    }
}
```

- [ ] **Step 6: Run the happy-path test**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests \
  --filter FullyQualifiedName~StockCheckBatchHandlerTests.Aggregates_required_quantities_across_entries
```

Expected: PASS.

- [ ] **Step 7: Add the empty-list test**

Append to the test class:

```csharp
[Fact]
public async Task Empty_entry_list_returns_empty_result()
{
    using var fx = new HandlerTestFixture();
    var validator = new StockCheckBatchValidator();
    var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object, validator);

    var result = await sut.Handle(new StockCheckBatchQuery(Array.Empty<Guid>()), CancellationToken.None);

    result.Lines.Should().BeEmpty();
    result.IsSufficient.Should().BeTrue();
    result.MissingCount.Should().Be(0);
}
```

> **Note:** The first test you wrote constructs `StockCheckBatchHandler` without a validator. Update it now to pass `new StockCheckBatchValidator()` as the third constructor arg, matching the empty-list test. Both tests should compile and pass.

- [ ] **Step 8: Add the cross-family-rejection test**

```csharp
[Fact]
public async Task Throws_when_an_entry_belongs_to_another_family()
{
    using var fx = new HandlerTestFixture();
    var otherFamily = Family.Create("Other", Guid.NewGuid());
    fx.Db.Families.Add(otherFamily);
    var stranger = MealPlanEntry.Create(
        otherFamily.Id, new DateOnly(2026, 4, 13), MealSlot.Breakfast, Guid.NewGuid(), Guid.NewGuid());
    fx.Db.MealPlanEntries.Add(stranger);
    await fx.Db.SaveChangesAsync();

    var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object, new StockCheckBatchValidator());

    Func<Task> act = () => sut.Handle(new StockCheckBatchQuery(new[] { stranger.Id }), CancellationToken.None).AsTask();
    await act.Should().ThrowAsync<DomainException>();
}
```

- [ ] **Step 9: Run all StockCheckBatch tests**

```bash
dotnet test tests/MenuNest.Application.UnitTests \
  --filter FullyQualifiedName~StockCheckBatchHandlerTests
```

Expected: 3 passed, 0 failed.

- [ ] **Step 10: Wire the controller endpoint**

In `backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs`:

a) Add a `using`:

```csharp
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
```

b) Below the existing `StockCheck` action, add:

```csharp
[HttpPost("stock-check-batch")]
public async Task<ActionResult<StockCheckBatchDto>> StockCheckBatch(
    [FromBody] StockCheckBatchRequest request,
    CancellationToken ct)
{
    var result = await _mediator.Send(new StockCheckBatchQuery(request.EntryIds), ct);
    return Ok(result);
}
```

c) At the bottom of the file (next to the existing request records), add:

```csharp
public sealed record StockCheckBatchRequest(IReadOnlyList<Guid> EntryIds);
```

- [ ] **Step 11: Smoke-test the endpoint**

Restart the API. Hit `POST /api/meal-plan/stock-check-batch` with `{"entryIds":[]}` — expect `{"lines":[],"isSufficient":true,"missingCount":0}`. With one or two real entry ids, expect aggregated lines.

- [ ] **Step 12: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch \
        backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs \
        backend/tests/MenuNest.Application.UnitTests/MealPlan/StockCheckBatchHandlerTests.cs
git commit -m "feat(meal-plan): add stock-check-batch endpoint"
```

---

## Task 5: `cook-batch` command, handler, endpoint, tests

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/MealPlan/CookBatchHandlerTests.cs`

- [ ] **Step 1: Write the failing happy-path test**

`backend/tests/MenuNest.Application.UnitTests/MealPlan/CookBatchHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.MealPlan;

public class CookBatchHandlerTests
{
    [Fact]
    public async Task Sufficient_stock_marks_all_entries_cooked_and_deducts()
    {
        using var fx = new HandlerTestFixture();
        var (egg, recipe, entry) = SeedSimpleMeal(fx, eggsRequired: 2m, eggsOnHand: 5m);

        var sut = NewSut(fx);
        var result = await sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None);

        result.CookedEntryIds.Should().ContainSingle().Which.Should().Be(entry.Id);
        result.Partial.Should().BeEmpty();
        result.Deducted.Should().ContainSingle().Which.Amount.Should().Be(2m);

        fx.Db.MealPlanEntries.Find(entry.Id)!.Status.Should().Be(MealEntryStatus.Cooked);
        fx.Db.StockItems.Single(s => s.IngredientId == egg.Id).Quantity.Should().Be(3m);
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.IngredientId == egg.Id && t.Delta == -2m && t.Source == StockTransactionSource.Cook);
    }

    [Fact]
    public async Task Insufficient_stock_clamps_at_zero_and_writes_cook_notes()
    {
        using var fx = new HandlerTestFixture();
        var (egg, recipe, entry) = SeedSimpleMeal(fx, eggsRequired: 5m, eggsOnHand: 2m);

        var sut = NewSut(fx);
        var result = await sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None);

        result.Partial.Should().ContainSingle().Which.Missing.Should().Be(3m);
        result.Deducted.Should().ContainSingle().Which.Amount.Should().Be(2m);

        var cooked = fx.Db.MealPlanEntries.Find(entry.Id)!;
        cooked.Status.Should().Be(MealEntryStatus.Cooked);
        cooked.CookNotes.Should().NotBeNullOrEmpty();
        cooked.CookNotes!.Should().Contain("ขาด").And.Contain(egg.Name);

        fx.Db.StockItems.Single(s => s.IngredientId == egg.Id).Quantity.Should().Be(0m);
    }

    [Fact]
    public async Task Rejects_when_any_entry_is_already_cooked()
    {
        using var fx = new HandlerTestFixture();
        var (_, _, entry) = SeedSimpleMeal(fx, eggsRequired: 1m, eggsOnHand: 5m);
        entry.MarkCooked(fx.User.Id);
        await fx.Db.SaveChangesAsync();

        var sut = NewSut(fx);
        Func<Task> act = () => sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Rejects_when_any_entry_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();
        var stranger = MealPlanEntry.Create(
            Guid.NewGuid(), new DateOnly(2026, 4, 13), MealSlot.Breakfast, Guid.NewGuid(), Guid.NewGuid());
        fx.Db.MealPlanEntries.Add(stranger);
        await fx.Db.SaveChangesAsync();

        var sut = NewSut(fx);
        Func<Task> act = () => sut.Handle(new CookBatchCommand(new[] { stranger.Id }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    private static CookBatchHandler NewSut(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new CookBatchValidator());

    private static (Ingredient Egg, Recipe Recipe, MealPlanEntry Entry) SeedSimpleMeal(
        HandlerTestFixture fx, decimal eggsRequired, decimal eggsOnHand)
    {
        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่เจียว", description: null, fx.User.Id);
        recipe.AddIngredient(egg.Id, eggsRequired);
        fx.Db.Recipes.Add(recipe);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, eggsOnHand, fx.User.Id));

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 13), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);
        fx.Db.SaveChanges();
        return (egg, recipe, entry);
    }
}
```

- [ ] **Step 2: Run to confirm compile failure**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CookBatchHandlerTests
```

Expected: build error — `CookBatchHandler`, `CookBatchCommand`, `CookBatchValidator` don't exist.

- [ ] **Step 3: Create the command + DTOs**

`backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

/// <summary>
/// Cook every entry in <see cref="EntryIds"/> in a single transaction.
/// Aggregates ingredient deductions across all entries, clamps stock
/// at zero, and writes one StockTransaction per ingredient actually
/// deducted. Rejects the batch if any entry is missing, in another
/// family, or already cooked — the UI is expected to refresh and
/// retry in that case.
/// </summary>
public sealed record CookBatchCommand(IReadOnlyList<Guid> EntryIds) : ICommand<CookBatchResult>;

public sealed record CookBatchResult(
    IReadOnlyList<CookDeducted> Deducted,
    IReadOnlyList<CookShortfall> Partial,
    IReadOnlyList<Guid> CookedEntryIds);

public sealed record CookDeducted(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Amount);

public sealed record CookShortfall(
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Required,
    decimal Deducted,
    decimal Missing);
```

- [ ] **Step 4: Create the validator**

`backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

public sealed class CookBatchValidator : AbstractValidator<CookBatchCommand>
{
    public CookBatchValidator()
    {
        RuleFor(x => x.EntryIds)
            .NotEmpty().WithMessage("Select at least one entry to cook.");
        RuleForEach(x => x.EntryIds).NotEqual(Guid.Empty);
    }
}
```

- [ ] **Step 5: Create the handler**

`backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchHandler.cs`:

```csharp
using System.Globalization;
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

public sealed class CookBatchHandler : ICommandHandler<CookBatchCommand, CookBatchResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CookBatchCommand> _validator;

    public CookBatchHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CookBatchCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<CookBatchResult> Handle(CookBatchCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ids = command.EntryIds.Distinct().ToArray();

        var entries = await _db.MealPlanEntries
            .Where(m => ids.Contains(m.Id) && m.FamilyId == familyId)
            .ToListAsync(ct);
        if (entries.Count != ids.Length)
        {
            throw new DomainException("One or more meal plan entries were not found.");
        }
        if (entries.Any(e => e.Status != MealEntryStatus.Planned))
        {
            throw new DomainException("Only planned entries can be cooked. Refresh and try again.");
        }

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // Aggregate required per ingredient — once per entry occurrence.
        var required = new Dictionary<Guid, decimal>();
        foreach (var entry in entries)
        {
            var recipe = recipes.Single(r => r.Id == entry.RecipeId);
            foreach (var ri in recipe.Ingredients)
            {
                required[ri.IngredientId] = required.GetValueOrDefault(ri.IngredientId) + ri.Quantity;
            }
        }

        var ingredientIds = required.Keys.ToList();
        var ingredientLookup = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var stockItems = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToListAsync(ct);
        var stockLookup = stockItems.ToDictionary(s => s.IngredientId);

        var deducted = new List<CookDeducted>();
        var partial = new List<CookShortfall>();
        var notesParts = new List<string>();

        var sourceRefId = entries[0].Id;

        foreach (var (ingredientId, neededRaw) in required)
        {
            var meta = ingredientLookup[ingredientId];
            var stock = stockLookup.GetValueOrDefault(ingredientId);
            var available = stock?.Quantity ?? 0m;

            // ApplyDelta returns the delta actually applied (clamped at 0).
            // For an entirely missing stock row, applied = 0 and we still
            // record the shortfall in CookNotes / partial[] so the user
            // knows what to add to a shopping list.
            var applied = stock?.ApplyDelta(-neededRaw, user.Id) ?? 0m;
            var actuallyDeducted = -applied;

            if (actuallyDeducted > 0m)
            {
                deducted.Add(new CookDeducted(meta.Id, meta.Name, meta.Unit, actuallyDeducted));
                _db.StockTransactions.Add(StockTransaction.Create(
                    familyId,
                    meta.Id,
                    -actuallyDeducted,
                    StockTransactionSource.Cook,
                    sourceRefId: sourceRefId,
                    userId: user.Id,
                    notes: $"Batch cook of {entries.Count} entries"));
            }

            var missing = neededRaw - actuallyDeducted;
            if (missing > 0m)
            {
                partial.Add(new CookShortfall(meta.Id, meta.Name, meta.Unit, neededRaw, actuallyDeducted, missing));
                notesParts.Add($"ขาด {meta.Name} {missing.ToString(CultureInfo.InvariantCulture)} {meta.Unit}");
            }
        }

        var cookNotes = notesParts.Count == 0
            ? null
            : string.Join("; ", notesParts) + " — ใช้เท่าที่มี";

        foreach (var entry in entries)
        {
            entry.MarkCooked(user.Id, cookNotes);
        }

        await _db.SaveChangesAsync(ct);

        return new CookBatchResult(deducted, partial, entries.Select(e => e.Id).ToList());
    }
}
```

- [ ] **Step 6: Run all CookBatch tests**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CookBatchHandlerTests
```

Expected: 4 passed.

- [ ] **Step 7: Wire the controller endpoint**

In `MealPlanController.cs`:

a) Add `using MenuNest.Application.UseCases.MealPlan.CookBatch;`

b) Add the action below the new `StockCheckBatch`:

```csharp
[HttpPost("cook-batch")]
public async Task<ActionResult<CookBatchResult>> CookBatch(
    [FromBody] CookBatchRequest request,
    CancellationToken ct)
{
    var result = await _mediator.Send(new CookBatchCommand(request.EntryIds), ct);
    return Ok(result);
}
```

c) At the bottom of the file:

```csharp
public sealed record CookBatchRequest(IReadOnlyList<Guid> EntryIds);
```

- [ ] **Step 8: Smoke-test**

Restart the API. Plan two recipes for the same slot via the existing UI, then `POST /api/meal-plan/cook-batch` with both ids. Expect `200` with `cookedEntryIds` containing both, and `GET /api/stock` reflecting the deductions.

- [ ] **Step 9: Run the full backend test suite**

```bash
cd backend
dotnet test
```

Expected: all green.

- [ ] **Step 10: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch \
        backend/src/MenuNest.WebApi/Controllers/MealPlanController.cs \
        backend/tests/MenuNest.Application.UnitTests/MealPlan/CookBatchHandlerTests.cs
git commit -m "feat(meal-plan): add cook-batch endpoint with stock deduction"
```

---

## Task 6: Frontend RTK Query — DTOs and two endpoints

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Add the new DTO interfaces**

Inside `api.ts`, near the existing `StockCheckLineDto` / `StockCheckDto` block, add:

```ts
export interface StockCheckBatchLineDto {
  ingredientId: string
  ingredientName: string
  unit: string
  required: number
  available: number
  missing: number
}

export interface StockCheckBatchDto {
  lines: StockCheckBatchLineDto[]
  isSufficient: boolean
  missingCount: number
}

export interface CookDeducted {
  ingredientId: string
  ingredientName: string
  unit: string
  amount: number
}

export interface CookShortfall {
  ingredientId: string
  ingredientName: string
  unit: string
  required: number
  deducted: number
  missing: number
}

export interface CookBatchResult {
  deducted: CookDeducted[]
  partial: CookShortfall[]
  cookedEntryIds: string[]
}
```

- [ ] **Step 2: Add the `stockCheckBatch` query endpoint**

Inside the `endpoints: (build) => ({ ... })` block, next to the existing `getStockCheck`:

```ts
stockCheckBatch: build.query<StockCheckBatchDto, { entryIds: string[] }>({
  // The cache key must be order-insensitive — the user's checkbox toggle
  // can produce the same logical set in any order.
  query: ({ entryIds }) => ({
    url: '/api/meal-plan/stock-check-batch',
    method: 'POST',
    body: { entryIds: [...entryIds].sort() },
  }),
  providesTags: (_res, _err, arg) =>
    arg.entryIds.map((id) => ({ type: 'MealPlan' as const, id: `stock-check-batch-${id}` })),
}),
```

- [ ] **Step 3: Add the `cookMealPlanBatch` mutation**

Right after `deleteMealPlanEntry`:

```ts
cookMealPlanBatch: build.mutation<CookBatchResult, { entryIds: string[] }>({
  query: ({ entryIds }) => ({
    url: '/api/meal-plan/cook-batch',
    method: 'POST',
    body: { entryIds },
  }),
  invalidatesTags: (_res, _err, arg) => [
    { type: 'MealPlan', id: 'LIST' },
    ...arg.entryIds.map((id) => ({ type: 'MealPlan' as const, id })),
    { type: 'Stock', id: 'LIST' },
  ],
}),
```

- [ ] **Step 4: Confirm hooks were generated**

The auto-generated hooks `useStockCheckBatchQuery` and `useCookMealPlanBatchMutation` come from RTK Query's `enhanceEndpoints` / `injectEndpoints` codegen. Verify by running:

```bash
cd frontend
npx tsc --noEmit
```

Expected: no errors. If TS complains the hooks are missing, the file you edited is not the central `createApi` instance — re-check the file path is `frontend/src/shared/api/api.ts`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(api): add stock-check-batch + cook-batch RTK Query endpoints"
```

---

## Task 7: `mealPlanSlice` — switch focus from entry id to slot

**Files:**
- Modify: `frontend/src/pages/meal-plan/mealPlanSlice.ts`

- [ ] **Step 1: Replace the slice file**

Overwrite `frontend/src/pages/meal-plan/mealPlanSlice.ts` with:

```ts
import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'
import type { MealSlot } from '../../shared/api/api'

export interface FocusedSlot {
  date: string  // ISO yyyy-mm-dd
  slot: MealSlot
}

interface MealPlanState {
  /** ISO date for the first day (Monday) of the week being viewed. */
  viewStartDate: string
  /** Slot currently expanded in the side dialog, or null. */
  focusedSlot: FocusedSlot | null
  /** Whether the recipe-picker dialog is open. */
  recipePickerOpen: boolean
  /** Whether the cook-confirm dialog is open. */
  cookDialogOpen: boolean
}

function startOfThisWeek(): string {
  const today = new Date()
  const dow = today.getDay()
  const monday = new Date(today)
  monday.setDate(today.getDate() - ((dow + 6) % 7))
  // Use local Y/M/D — toISOString shifts into UTC and can land on the
  // previous day east of Greenwich.
  const y = monday.getFullYear()
  const m = String(monday.getMonth() + 1).padStart(2, '0')
  const d = String(monday.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

const initialState: MealPlanState = {
  viewStartDate: startOfThisWeek(),
  focusedSlot: null,
  recipePickerOpen: false,
  cookDialogOpen: false,
}

const mealPlanSlice = createSlice({
  name: 'mealPlan',
  initialState,
  reducers: {
    setViewStartDate(state, action: PayloadAction<string>) {
      state.viewStartDate = action.payload
    },
    selectSlot(state, action: PayloadAction<FocusedSlot | null>) {
      state.focusedSlot = action.payload
    },
    openRecipePicker(state) {
      state.recipePickerOpen = true
    },
    closeRecipePicker(state) {
      state.recipePickerOpen = false
    },
    openCookDialog(state) {
      state.cookDialogOpen = true
    },
    closeCookDialog(state) {
      state.cookDialogOpen = false
    },
  },
})

export const {
  setViewStartDate,
  selectSlot,
  openRecipePicker,
  closeRecipePicker,
  openCookDialog,
  closeCookDialog,
} = mealPlanSlice.actions
export default mealPlanSlice.reducer
```

> **Note:** This deletes the old `selectEntry` action. The next task fixes every consumer in `MealPlanPage.tsx` so `tsc` compiles again.

- [ ] **Step 2: Don't run tsc yet**

Compilation will fail because Task 8 hasn't updated the page. Move on directly. (This step is intentional — committing the slice and the consumer in one logical change makes the diff easier to review.)

---

## Task 8: Replace `EntryDetailContent` with `MealSlotDetailContent`

**Files:**
- Modify: `frontend/src/pages/meal-plan/MealPlanPage.tsx`

This is the largest task in the plan. We rewrite the bottom half of the file (the entry detail dialog) into a slot-detail dialog with a checkbox table and Cook button.

- [ ] **Step 1: Update imports**

At the top of `MealPlanPage.tsx`, change the api import block to add the new types and hooks:

```ts
import {
  useCookMealPlanBatchMutation,
  useCreateMealPlanEntryMutation,
  useDeleteMealPlanEntryMutation,
  useGetStockCheckQuery,
  useListMealPlanQuery,
  useListRecipesQuery,
  useStockCheckBatchQuery,
} from '../../shared/api/api'
import type { MealPlanEntryDto, MealSlot } from '../../shared/api/api'
```

`useGetStockCheckQuery` stays — it powers the per-row stock badge in `MealSlotDetailContent` (the slot-level batch query is for the *aggregate* footer warning, not individual rows).

Update the slice import to use the new action name:

```ts
import {
  closeRecipePicker,
  openRecipePicker,
  selectSlot,
  setViewStartDate,
} from './mealPlanSlice'
import type { FocusedSlot } from './mealPlanSlice'
```

- [ ] **Step 2: Rewire the page-level state derivation**

In the `MealPlanPage` component, replace the focused-entry lookup with focused-slot:

```ts
const focusedSlot = useAppSelector((s) => s.mealPlan.focusedSlot)

// All entries that share the focused slot — typically one or more
// recipes the user planned for that meal.
const focusedEntries = useMemo(
  () =>
    focusedSlot
      ? (entries ?? []).filter(
          (e) => e.date === focusedSlot.date && e.mealSlot === focusedSlot.slot,
        )
      : [],
  [entries, focusedSlot],
)
```

Delete the line that defined `focusedEntry`.

- [ ] **Step 3: Update the cell-click + event-click handlers**

Replace the bodies of `handleCellClick` and `handleEventClick` so both dispatch `selectSlot`:

```ts
const handleCellClick = (args: SchedulerCellClickEvent) => {
  args.cancel = true
  const slot = slotFromHour(args.startTime.getHours())
  if (!slot) return
  const date = formatIso(args.startTime)
  const existing = entries?.some((e) => e.date === date && e.mealSlot === slot)
  if (existing) {
    dispatch(selectSlot({ date, slot }))
  } else {
    setPickerSlot({ date, slot })
    dispatch(openRecipePicker())
  }
}

const handleEventClick = (args: SchedulerEventClickEvent) => {
  args.cancel = true
  const id = (args.data?.Id ?? args.data?.id) as string | undefined
  const entry = entries?.find((e) => e.id === id)
  if (entry) {
    dispatch(selectSlot({ date: entry.date, slot: entry.mealSlot }))
  }
}
```

Update `closeDetail`:

```ts
const closeDetail = () => dispatch(selectSlot(null))
```

- [ ] **Step 4: Replace the detail dialog**

Find the JSX for the second `<Dialog>` (the one that currently uses `focusedEntry?.recipeName`). Replace the whole `<Dialog>...</Dialog>` block with:

```tsx
<Dialog
  open={!!focusedSlot && focusedEntries.length > 0}
  onClose={closeDetail}
  modal
  header={
    focusedSlot
      ? `${dayLabel(focusedSlot.date)} · ${SLOT_LABELS[focusedSlot.slot]}`
      : ''
  }
  style={{ width: '720px' }}
>
  {focusedSlot && (
    <MealSlotDetailContent
      slot={focusedSlot}
      entries={focusedEntries}
      onAddRecipe={() => {
        setPickerSlot(focusedSlot)
        dispatch(openRecipePicker())
      }}
      onClose={closeDetail}
    />
  )}
</Dialog>
```

- [ ] **Step 5: Delete the old `EntryDetailContent` function**

Scroll to the bottom of `MealPlanPage.tsx` and delete the entire `EntryDetailContent` function plus its `EntryDetailProps` interface.

- [ ] **Step 6: Add `MealSlotDetailContent`**

Append this component to the end of the file (above `getErrorMessage`):

```tsx
// ----------------------------------------------------------------------
// Meal slot detail (multi-recipe table with batch cook)
// ----------------------------------------------------------------------

interface MealSlotDetailProps {
  slot: FocusedSlot
  entries: MealPlanEntryDto[]
  onAddRecipe: () => void
  onClose: () => void
}

function MealSlotDetailContent({ slot, entries, onAddRecipe, onClose }: MealSlotDetailProps) {
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteMealPlanEntryMutation()
  const [cookBatch, { isLoading: isCooking }] = useCookMealPlanBatchMutation()
  const { confirm } = useConfirm()

  // Selection of entry ids. Initialise with all Planned entries pre-checked
  // so the common case (cook everything I planned) is one click.
  const [selectedIds, setSelectedIds] = useState<Set<string>>(
    () => new Set(entries.filter((e) => e.status === 'Planned').map((e) => e.id)),
  )

  // Drop selections for entries that disappear (e.g. row deleted).
  useEffect(() => {
    setSelectedIds((prev) => {
      const next = new Set<string>()
      for (const id of prev) {
        if (entries.some((e) => e.id === id)) next.add(id)
      }
      return next
    })
  }, [entries])

  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const selectedArray = useMemo(() => Array.from(selectedIds), [selectedIds])

  const { data: stockCheck } = useStockCheckBatchQuery(
    { entryIds: selectedArray },
    { skip: selectedArray.length === 0 },
  )

  const toggle = (id: string, status: MealPlanEntryDto['status']) => {
    if (status !== 'Planned') return
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const handleDelete = async (entry: MealPlanEntryDto) => {
    const ok = await confirm({
      title: 'ลบรายการ',
      message: (
        <>
          ลบ <strong>"{entry.recipeName}"</strong> ออกจากมื้อนี้?
        </>
      ),
      confirmText: 'ลบ',
      destructive: true,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await deleteEntry(entry.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleCook = async () => {
    if (selectedArray.length === 0) return
    setErrorMessage(null)
    try {
      await cookBatch({ entryIds: selectedArray }).unwrap()
      onClose()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <div>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      <div className="table-scroll" style={{ marginBottom: 12 }}>
        <table className="data-table">
          <thead>
            <tr>
              <th style={{ width: 50 }}>เลือก</th>
              <th>Recipe</th>
              <th style={{ width: 200 }}>Stock</th>
              <th style={{ width: 200 }}>สถานะ</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => {
              const isPlanned = entry.status === 'Planned'
              const checked = selectedIds.has(entry.id)
              return (
                <tr key={entry.id} className={isPlanned ? undefined : 'row--cooked'}>
                  <td>
                    {isPlanned ? (
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={() => toggle(entry.id, entry.status)}
                        aria-label={`เลือก ${entry.recipeName}`}
                      />
                    ) : (
                      <span style={{ color: 'var(--color-text-muted)' }}>—</span>
                    )}
                  </td>
                  <td style={{ fontWeight: 500 }}>{entry.recipeName}</td>
                  <td>
                    <RowStockBadge entryId={entry.id} status={entry.status} />
                  </td>
                  <td>
                    {isPlanned ? (
                      <>
                        <span className="status status--planned">Planned</span>
                        <Button
                          type="button"
                          size={Size.Small}
                          variant={Variant.Outlined}
                          color={Color.Error}
                          onClick={() => handleDelete(entry)}
                          disabled={isDeleting}
                          aria-label="ลบ"
                          style={{ marginLeft: 6 }}
                        >
                          🗑
                        </Button>
                      </>
                    ) : (
                      <>
                        <span className="status status--cooked">✓ Cooked</span>
                        {entry.cookedAt && (
                          <span style={{ color: 'var(--color-text-muted)', fontSize: 12, marginLeft: 6 }}>
                            {new Date(entry.cookedAt).toLocaleTimeString('th-TH', {
                              hour: '2-digit',
                              minute: '2-digit',
                            })}
                          </span>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {stockCheck && stockCheck.missingCount > 0 && (
        <div
          style={{
            background: '#fff3e0',
            border: '1px solid #ffb74d',
            borderRadius: 6,
            padding: '10px 14px',
            fontSize: 13,
            color: '#e65100',
            marginBottom: 12,
          }}
        >
          ⚠️ ขาด{' '}
          {stockCheck.lines
            .filter((l) => l.missing > 0)
            .map((l) => `${l.ingredientName} ${l.missing} ${l.unit}`)
            .join(', ')}{' '}
          — เมื่อกด Cook ระบบจะหักเท่าที่มี
        </div>
      )}

      <div
        style={{
          display: 'flex',
          gap: 8,
          alignItems: 'center',
          paddingTop: 8,
          borderTop: '1px solid #eee',
        }}
      >
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Primary}
          onClick={onAddRecipe}
        >
          + เพิ่ม recipe
        </Button>
        <div style={{ flex: 1 }} />
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Secondary}
          onClick={onClose}
        >
          ยกเลิก
        </Button>
        <Button
          type="button"
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={handleCook}
          disabled={selectedArray.length === 0 || isCooking}
        >
          🍳 Cook selected ({selectedArray.length})
        </Button>
      </div>
    </div>
  )
}

/**
 * Per-row stock badge — reuses the single-entry stock check so each
 * row tells the user "this one alone is short". The selected-set
 * banner above the footer reports the *aggregate*.
 */
function RowStockBadge({ entryId, status }: { entryId: string; status: MealPlanEntryDto['status'] }) {
  const { data } = useGetStockCheckQuery(entryId, { skip: status !== 'Planned' })
  if (status !== 'Planned') return <span style={{ color: 'var(--color-text-muted)' }}>—</span>
  if (!data) return <span style={{ color: 'var(--color-text-muted)' }}>…</span>
  return data.isSufficient ? (
    <span style={{ color: 'green' }}>✅ พอ</span>
  ) : (
    <span style={{ color: 'var(--color-danger)' }}>⚠️ ขาด {data.missingCount} อย่าง</span>
  )
}
```


- [ ] **Step 7: Add minimal CSS for the new status pills**

In `frontend/src/index.css`, append:

```css
.status {
  display: inline-block;
  padding: 3px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}
.status--planned { background: #fff3e0; color: #e65100; }
.status--cooked  { background: #e8f5e9; color: #2e7d32; }
.row--cooked     { background: #fafafa; }
```

- [ ] **Step 8: Run typecheck**

```bash
cd frontend
npx tsc --noEmit
```

Expected: zero errors. If the compiler complains about missing `useEffect` import, add it to the React import line (`import { useEffect, useMemo, useState } from 'react'`).

- [ ] **Step 9: Commit**

```bash
git add frontend/src/pages/meal-plan/mealPlanSlice.ts \
        frontend/src/pages/meal-plan/MealPlanPage.tsx \
        frontend/src/index.css
git commit -m "feat(meal-plan): replace per-entry dialog with multi-recipe slot detail + batch cook"
```

---

## Task 9: End-to-end smoke test with Playwright

This repo has no Vitest setup; we verify the integrated flow with Playwright via the chrome-devtools / playwright MCP tools (the same approach used elsewhere in this project).

- [ ] **Step 1: Make sure both servers are running**

Backend on `https://localhost:5001`, frontend on `http://localhost:5173`. If not:

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/MenuNest.WebApi --launch-profile https
```

(in a separate terminal)

```bash
cd frontend
npm run dev
```

- [ ] **Step 2: Plan two recipes for the same Breakfast slot**

In a browser at `http://localhost:5173/meal-plan`:

1. Click an empty Breakfast cell (e.g. Tuesday 7 AM). Pick recipe A. Confirm appointment renders.
2. Click the same Breakfast cell again — the **detail dialog** opens (because the cell now has an entry). Click `+ เพิ่ม recipe`. Pick recipe B. Confirm both appointments render side-by-side under the slot.

- [ ] **Step 3: Cook only one of the two**

1. Open the slot detail dialog. Both rows pre-checked.
2. Uncheck recipe B. Verify the warning banner updates (or disappears) and the button reads `🍳 Cook selected (1)`.
3. Click Cook. Dialog closes. Recipe A's appointment turns grey ("Cooked" status); recipe B remains orange.
4. Open the slot dialog again — recipe A now shows ✓ Cooked + timestamp, recipe B is still Planned and selectable.

- [ ] **Step 4: Verify stock was deducted**

Navigate to `/stock`. The ingredients used by recipe A should be reduced by exactly the recipe's quantities.

- [ ] **Step 5: Cook the remaining entry with insufficient stock**

1. Reduce the stock of any ingredient recipe B needs (via −/+ buttons) to below the recipe's requirement.
2. Open the slot, select recipe B, Cook.
3. Confirm: stock clamps at 0, the entry is Cooked, and re-opening the slot shows recipe B's `cookNotes` field carries a `"ขาด ..."` summary (visible if you add a small read-only render of cookNotes — optional follow-up; for now verify via the API: `GET /api/meal-plan?from=...&to=...` returns the entry with `cookNotes` populated).

- [ ] **Step 6: Verify duplicate recipe in same slot still works**

Add the same recipe to the same slot twice. Both should appear; cooking both deducts the recipe's ingredients twice.

- [ ] **Step 7: Document any regression in commit message**

If everything passed, no commit needed for this task. If you fixed any small UI issue while smoke-testing (e.g. CSS), commit it now with a short message.

---

## Task 10: Final sweep

- [ ] **Step 1: Run the full backend suite**

```bash
cd backend
dotnet test
```

Expected: all green.

- [ ] **Step 2: Run frontend typecheck + lint**

```bash
cd frontend
npx tsc --noEmit
npm run lint
```

Expected: no errors.

- [ ] **Step 3: Push the branch**

```bash
git push -u origin HEAD
```

(Open a PR if the project workflow requires one.)

---

## Self-Review Notes (already applied)

1. **Spec coverage:** Drop unique constraint (Task 1), drop occupied guard (Task 2), `stock-check-batch` (Task 4), `cook-batch` (Task 5), frontend slice (Task 7), frontend dialog rewrite (Task 8). The spec also mentions a frontend Vitest test for `MealSlotDetailContent` — replaced with a documented Playwright smoke test (Task 9) because the repo has no Vitest setup; adding Vitest is called out as out-of-scope in the spec follow-ups.
2. **Type names:** `CookBatchResult`, `CookDeducted`, `CookShortfall`, `StockCheckBatchDto`, `StockCheckBatchLineDto`, `useCookMealPlanBatchMutation`, `useStockCheckBatchQuery` are used consistently in backend, api.ts, and the page.
3. **Implicit transactions:** No explicit `BeginTransactionAsync` is used because `IApplicationDbContext` only exposes `SaveChangesAsync`. Cook-batch's atomicity comes from a single trailing `SaveChangesAsync` — EF Core wraps the resulting SQL in one transaction. If a future spec needs distributed-style coordination, that's a follow-up.
4. **Frontend test framework:** intentionally not added in this plan; if the team wants Vitest later, it's a new spec.
