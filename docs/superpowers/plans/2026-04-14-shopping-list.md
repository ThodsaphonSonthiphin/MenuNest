# Shopping List — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shopping list feature that auto-generates from a meal plan date range (computing missing ingredients against current stock), supports manual item management, and auto-updates stock when items are marked bought/unbought.

**Architecture:** Domain entities (`ShoppingList`, `ShoppingListItem`) already exist with full business logic (`MarkBought`, `Unmark`, `Complete`, `AddOrIncreaseItem`). EF configurations with JSON column support and unique constraints are in place. The initial migration created the DB tables. This plan adds CQRS handlers behind the Mediator pipeline, a new REST controller, fills in the existing frontend page stubs + RTK Query endpoints, and creates component/hook pairs per the project convention.

**Tech Stack:**
- Backend: ASP.NET 10, EF Core 10, `martinothamar/Mediator`, FluentValidation, xUnit + Moq + FluentAssertions + InMemory DbContext
- Frontend: React 19, Redux Toolkit + RTK Query, Syncfusion Pure React, react-hook-form
- Verification: Playwright smoke test

**Spec:** [docs/superpowers/specs/2026-04-14-shopping-list-design.md](../specs/2026-04-14-shopping-list-design.md)

---

## Existing Infrastructure (already done — do NOT recreate)

- **Domain:** `ShoppingList.cs` (Create, AddOrIncreaseItem, RemoveItem, Complete, Rename), `ShoppingListItem.cs` (Create, MarkBought, Unmark, IncreaseQuantity, UpdateQuantity), `ShoppingListStatus` enum
- **EF configs:** `ShoppingListConfiguration.cs`, `ShoppingListItemConfiguration.cs` (JSON for SourceMealPlanEntryIds, UNIQUE ShoppingListId+IngredientId)
- **DB tables:** `ShoppingLists`, `ShoppingListItems` (initial migration)
- **Frontend:** Router routes `/shopping` + `/shopping/:id`, `shoppingSlice.ts` (filter + createDialogOpen), NavBar link, store reducer, page stub files, `ShoppingListSummaryDto` type, `listShoppingLists` endpoint stub in `api.ts`
- **Test infra:** `InMemoryAppDbContext` + `HandlerTestFixture` (from multi-recipe Tasks)

## File Structure

### Backend — create

- `backend/src/MenuNest.Application/UseCases/ShoppingList/ShoppingListDtos.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsQuery.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailQuery.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListValidator.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingList/DeleteShoppingListCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingList/DeleteShoppingListHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CompleteShoppingList/CompleteShoppingListCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CompleteShoppingList/CompleteShoppingListHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/AddShoppingListItem/AddShoppingListItemCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/AddShoppingListItem/AddShoppingListItemValidator.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/AddShoppingListItem/AddShoppingListItemHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingListItem/DeleteShoppingListItemCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingListItem/DeleteShoppingListItemHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemHandler.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/RegenerateShoppingList/RegenerateShoppingListCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/RegenerateShoppingList/RegenerateShoppingListHandler.cs`
- `backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`
- `backend/tests/MenuNest.Application.UnitTests/ShoppingList/CreateShoppingListHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/ShoppingList/BuyUnbuyHandlerTests.cs`
- `backend/tests/MenuNest.Application.UnitTests/ShoppingList/RegenerateHandlerTests.cs`

### Frontend — modify

- `frontend/src/shared/api/api.ts` — fill in DTOs + endpoints
- `frontend/src/pages/shopping/ShoppingListsPage.tsx` — full rewrite (currently a stub)
- `frontend/src/pages/shopping/ShoppingListDetailPage.tsx` — full rewrite (currently a stub)

### Frontend — create

- `frontend/src/pages/shopping/components/ShoppingListCard.tsx`
- `frontend/src/pages/shopping/components/ShoppingItemRow.tsx`
- `frontend/src/pages/shopping/components/CreateListDialog.tsx`
- `frontend/src/pages/shopping/components/AddItemForm.tsx`
- `frontend/src/pages/shopping/hooks/useShoppingListDetail.ts`
- `frontend/src/pages/shopping/hooks/useCreateShoppingList.ts`

---

## Task 1: Backend DTOs + Controller shell + List/GetDetail handlers

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/ShoppingListDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailHandler.cs`
- Create: `backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`

- [ ] **Step 1: Create DTOs**

`backend/src/MenuNest.Application/UseCases/ShoppingList/ShoppingListDtos.cs`:

```csharp
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.ShoppingList;

public sealed record ShoppingListDto(
    Guid Id,
    string Name,
    ShoppingListStatus Status,
    int TotalCount,
    int BoughtCount,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record ShoppingListItemDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string Unit,
    decimal Quantity,
    bool IsBought,
    DateTime? BoughtAt,
    IReadOnlyList<Guid>? SourceMealPlanEntryIds);

public sealed record ShoppingListDetailDto(
    Guid Id,
    string Name,
    ShoppingListStatus Status,
    int TotalCount,
    int BoughtCount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ShoppingListItemDto> Items);
```

- [ ] **Step 2: Create ListShoppingLists query + handler**

`backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsQuery.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;

public sealed record ListShoppingListsQuery(ShoppingListStatus? Status = null)
    : IQuery<IReadOnlyList<ShoppingListDto>>;
```

`backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;

public sealed class ListShoppingListsHandler
    : IQueryHandler<ListShoppingListsQuery, IReadOnlyList<ShoppingListDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListShoppingListsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ShoppingListDto>> Handle(
        ListShoppingListsQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var q = _db.ShoppingLists
            .Include(l => l.Items)
            .Where(l => l.FamilyId == familyId);

        if (query.Status.HasValue)
            q = q.Where(l => l.Status == query.Status.Value);

        return await q
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new ShoppingListDto(
                l.Id, l.Name, l.Status,
                l.Items.Count,
                l.Items.Count(i => i.IsBought),
                l.CreatedAt, l.CompletedAt))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: Create GetShoppingListDetail query + handler**

`backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;

public sealed record GetShoppingListDetailQuery(Guid Id) : IQuery<ShoppingListDetailDto>;
```

`backend/src/MenuNest.Application/UseCases/ShoppingList/GetShoppingListDetail/GetShoppingListDetailHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;

public sealed class GetShoppingListDetailHandler
    : IQueryHandler<GetShoppingListDetailQuery, ShoppingListDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetShoppingListDetailHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListDetailDto> Handle(
        GetShoppingListDetailQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == query.Id && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var ingredientIds = list.Items.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients
            .Where(i => ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var items = list.Items
            .Select(i =>
            {
                var meta = ingredients[i.IngredientId];
                return new ShoppingListItemDto(
                    i.Id, i.IngredientId, meta.Name, meta.Unit,
                    i.Quantity, i.IsBought, i.BoughtAt,
                    i.SourceMealPlanEntryIds.Count > 0 ? i.SourceMealPlanEntryIds : null);
            })
            .OrderBy(i => i.IsBought)
            .ThenBy(i => i.IngredientName)
            .ToList();

        return new ShoppingListDetailDto(
            list.Id, list.Name, list.Status,
            items.Count, items.Count(i => i.IsBought),
            list.CreatedAt, list.CompletedAt, items);
    }
}
```

- [ ] **Step 4: Create the controller with all endpoint shells**

`backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/shopping-lists")]
public sealed class ShoppingListsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ShoppingListsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShoppingListDto>>> List(
        [FromQuery] ShoppingListStatus? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListShoppingListsQuery(status), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShoppingListDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetShoppingListDetailQuery(id), ct);
        return Ok(result);
    }

    // POST, DELETE, /complete, /items, /items/{itemId}, /buy, /unbuy, /regenerate
    // are wired in subsequent tasks as the handlers are created.
}
```

- [ ] **Step 5: Build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/ShoppingList \
        backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs
git commit -m "feat(shopping): add DTOs + List/GetDetail handlers + controller shell"
```

---

## Task 2: CreateShoppingList handler (with auto-generate) + tests

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/ShoppingList/CreateShoppingListHandlerTests.cs`

- [ ] **Step 1: Write tests first**

`backend/tests/MenuNest.Application.UnitTests/ShoppingList/CreateShoppingListHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class CreateShoppingListHandlerTests
{
    [Fact]
    public async Task Creates_empty_list_when_no_dates_provided()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());

        var result = await sut.Handle(
            new CreateShoppingListCommand("Test list", null, null), CancellationToken.None);

        result.Name.Should().Be("Test list");
        result.Status.Should().Be(ShoppingListStatus.Active);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Auto_generates_items_from_planned_entries_missing_stock()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var oil = Ingredient.Create(fx.Family.Id, "น้ำมัน", "ขวด");
        fx.Db.Ingredients.AddRange(egg, oil);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 5m);
        recipe.AddIngredient(oil.Id, 1m);
        fx.Db.Recipes.Add(recipe);

        // Stock: egg=2 (short 3), oil=10 (enough)
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 2m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, oil.Id, 10m, fx.User.Id));

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());
        var result = await sut.Handle(
            new CreateShoppingListCommand("Week shopping", new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 20)),
            CancellationToken.None);

        result.TotalCount.Should().Be(1);  // only egg is short
        var detail = await fx.Db.ShoppingLists
            .Include(l => l.Items)
            .SingleAsync(l => l.Id == result.Id);
        detail.Items.Should().ContainSingle();
        detail.Items.First().IngredientId.Should().Be(egg.Id);
        detail.Items.First().Quantity.Should().Be(3m);  // 5 required - 2 on hand
    }

    [Fact]
    public async Task Cooked_entries_are_excluded_from_auto_generate()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 3m);
        fx.Db.Recipes.Add(recipe);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 0m, fx.User.Id));

        var planned = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        var cooked = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 16), MealSlot.Lunch, recipe.Id, fx.User.Id);
        cooked.MarkCooked(fx.User.Id);
        fx.Db.MealPlanEntries.AddRange(planned, cooked);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());
        var result = await sut.Handle(
            new CreateShoppingListCommand("Test", new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 20)),
            CancellationToken.None);

        // Only the planned entry contributes (3 eggs), cooked is excluded
        result.TotalCount.Should().Be(1);
        var list = await fx.Db.ShoppingLists.Include(l => l.Items).SingleAsync(l => l.Id == result.Id);
        list.Items.First().Quantity.Should().Be(3m);
    }
}
```

> **Note:** Add `using Microsoft.EntityFrameworkCore;` for `Include` + `SingleAsync`.

- [ ] **Step 2: Run tests — expect compile failure**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CreateShoppingListHandlerTests
```

Expected: build error — handler/command don't exist.

- [ ] **Step 3: Create command + validator**

`backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed record CreateShoppingListCommand(
    string Name,
    DateOnly? FromDate,
    DateOnly? ToDate) : ICommand<ShoppingListDto>;
```

`backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed class CreateShoppingListValidator : AbstractValidator<CreateShoppingListCommand>
{
    public CreateShoppingListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        When(x => x.FromDate.HasValue || x.ToDate.HasValue, () =>
        {
            RuleFor(x => x.FromDate).NotNull().WithMessage("Both dates are required when generating from meal plan.");
            RuleFor(x => x.ToDate).NotNull().WithMessage("Both dates are required when generating from meal plan.");
            RuleFor(x => x).Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
                .WithMessage("FromDate must be on or before ToDate.");
        });
    }
}
```

- [ ] **Step 4: Create the handler**

`backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed class CreateShoppingListHandler
    : ICommandHandler<CreateShoppingListCommand, ShoppingListDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateShoppingListCommand> _validator;

    public CreateShoppingListHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateShoppingListCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<ShoppingListDto> Handle(
        CreateShoppingListCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = Domain.Entities.ShoppingList.Create(familyId, command.Name, user.Id);
        _db.ShoppingLists.Add(list);

        if (command.FromDate.HasValue && command.ToDate.HasValue)
        {
            await AutoGenerateItems(list, familyId, command.FromDate.Value, command.ToDate.Value, ct);
        }

        await _db.SaveChangesAsync(ct);

        var itemCount = list.Items.Count;
        return new ShoppingListDto(
            list.Id, list.Name, list.Status,
            itemCount, 0, list.CreatedAt, list.CompletedAt);
    }

    private async Task AutoGenerateItems(
        Domain.Entities.ShoppingList list,
        Guid familyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        // 1. Load Planned entries in range
        var entries = await _db.MealPlanEntries
            .Where(e => e.FamilyId == familyId
                && e.Status == MealEntryStatus.Planned
                && e.Date >= fromDate && e.Date <= toDate)
            .ToListAsync(ct);

        if (entries.Count == 0) return;

        // 2. Load recipes with ingredients
        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // 3. Aggregate required per ingredient, tracking source entry ids
        var required = new Dictionary<Guid, decimal>();
        var sources = new Dictionary<Guid, List<Guid>>();
        foreach (var entry in entries)
        {
            var recipe = recipes.SingleOrDefault(r => r.Id == entry.RecipeId);
            if (recipe is null) continue;
            foreach (var ri in recipe.Ingredients)
            {
                required[ri.IngredientId] = required.GetValueOrDefault(ri.IngredientId) + ri.Quantity;
                if (!sources.ContainsKey(ri.IngredientId))
                    sources[ri.IngredientId] = new List<Guid>();
                if (!sources[ri.IngredientId].Contains(entry.Id))
                    sources[ri.IngredientId].Add(entry.Id);
            }
        }

        // 4. Load stock
        var ingredientIds = required.Keys.ToList();
        var stockLookup = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

        // 5. Create items for missing quantities
        foreach (var (ingredientId, totalRequired) in required)
        {
            var available = stockLookup.GetValueOrDefault(ingredientId);
            var missing = totalRequired - available;
            if (missing <= 0m) continue;

            list.AddOrIncreaseItem(ingredientId, missing, sources[ingredientId]);
        }
    }
}
```

- [ ] **Step 5: Wire controller endpoint**

Add to `ShoppingListsController.cs`:

```csharp
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
```

And the action:

```csharp
[HttpPost]
public async Task<ActionResult<ShoppingListDto>> Create(
    [FromBody] CreateShoppingListRequest request,
    CancellationToken ct)
{
    var result = await _mediator.Send(
        new CreateShoppingListCommand(request.Name, request.FromDate, request.ToDate), ct);
    return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
}
```

At the bottom of the file:

```csharp
public sealed record CreateShoppingListRequest(string Name, DateOnly? FromDate, DateOnly? ToDate);
```

- [ ] **Step 6: Run tests**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~CreateShoppingListHandlerTests
```

Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList \
        backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs \
        backend/tests/MenuNest.Application.UnitTests/ShoppingList
git commit -m "feat(shopping): add CreateShoppingList handler with auto-generate from meal plan"
```

---

## Task 3: Buy + Unbuy handlers + tests

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/ShoppingList/BuyUnbuyHandlerTests.cs`

- [ ] **Step 1: Write tests**

`backend/tests/MenuNest.Application.UnitTests/ShoppingList/BuyUnbuyHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class BuyUnbuyHandlerTests
{
    [Fact]
    public async Task Buy_marks_item_and_increments_stock()
    {
        using var fx = new HandlerTestFixture();
        var (list, item, ingredient) = SeedListWithItem(fx, stockOnHand: 2m, itemQty: 3m);

        var sut = new BuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new BuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        result.IsBought.Should().BeTrue();
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m); // 2+3
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.Delta == 3m && t.Source == StockTransactionSource.ShoppingListBought);
    }

    [Fact]
    public async Task Buy_creates_stock_item_when_none_exists()
    {
        using var fx = new HandlerTestFixture();
        var ingredient = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(ingredient);

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();
        list.AddOrIncreaseItem(ingredient.Id, 5m);
        fx.Db.SaveChanges();

        var item = list.Items.Single();

        var sut = new BuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        await sut.Handle(new BuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        fx.Db.StockItems.Should().ContainSingle(s => s.IngredientId == ingredient.Id);
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task Unbuy_reverses_stock_and_clears_bought_status()
    {
        using var fx = new HandlerTestFixture();
        var (list, item, ingredient) = SeedListWithItem(fx, stockOnHand: 5m, itemQty: 3m);

        // First buy
        item.MarkBought(fx.User.Id);
        var stock = fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id);
        stock.SetQuantity(8m, fx.User.Id); // simulate post-buy stock = 5+3
        fx.Db.SaveChanges();

        var sut = new UnbuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new UnbuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        result.IsBought.Should().BeFalse();
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m); // 8-3
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.Delta == -3m && t.Source == StockTransactionSource.Correction);
    }

    private static (Domain.Entities.ShoppingList List, ShoppingListItem Item, Ingredient Ingredient)
        SeedListWithItem(HandlerTestFixture fx, decimal stockOnHand, decimal itemQty)
    {
        var ingredient = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(ingredient);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, ingredient.Id, stockOnHand, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();
        list.AddOrIncreaseItem(ingredient.Id, itemQty);
        fx.Db.SaveChanges();

        return (list, list.Items.Single(), ingredient);
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

```bash
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BuyUnbuyHandlerTests
```

- [ ] **Step 3: Create Buy command + handler**

`backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;

public sealed record BuyShoppingListItemCommand(Guid ListId, Guid ItemId)
    : ICommand<ShoppingListItemDto>;
```

`backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;

public sealed class BuyShoppingListItemHandler
    : ICommandHandler<BuyShoppingListItemCommand, ShoppingListItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public BuyShoppingListItemHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListItemDto> Handle(
        BuyShoppingListItemCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var item = list.Items.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new DomainException("Shopping list item not found.");

        if (item.IsBought)
            throw new DomainException("Item is already marked as bought.");

        item.MarkBought(user.Id);

        // Increment stock
        var stockItem = await _db.StockItems
            .FirstOrDefaultAsync(s => s.FamilyId == familyId && s.IngredientId == item.IngredientId, ct);

        if (stockItem is not null)
        {
            stockItem.SetQuantity(stockItem.Quantity + item.Quantity, user.Id);
        }
        else
        {
            stockItem = StockItem.Create(familyId, item.IngredientId, item.Quantity, user.Id);
            _db.StockItems.Add(stockItem);
        }

        _db.StockTransactions.Add(StockTransaction.Create(
            familyId, item.IngredientId, item.Quantity,
            StockTransactionSource.ShoppingListBought,
            sourceRefId: item.Id, userId: user.Id));

        await _db.SaveChangesAsync(ct);

        var ingredient = await _db.Ingredients.FindAsync(new object[] { item.IngredientId }, ct);
        return new ShoppingListItemDto(
            item.Id, item.IngredientId, ingredient!.Name, ingredient.Unit,
            item.Quantity, item.IsBought, item.BoughtAt,
            item.SourceMealPlanEntryIds.Count > 0 ? item.SourceMealPlanEntryIds : null);
    }
}
```

- [ ] **Step 4: Create Unbuy command + handler**

`backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;

public sealed record UnbuyShoppingListItemCommand(Guid ListId, Guid ItemId)
    : ICommand<ShoppingListItemDto>;
```

`backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem/UnbuyShoppingListItemHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;

public sealed class UnbuyShoppingListItemHandler
    : ICommandHandler<UnbuyShoppingListItemCommand, ShoppingListItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public UnbuyShoppingListItemHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListItemDto> Handle(
        UnbuyShoppingListItemCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var item = list.Items.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new DomainException("Shopping list item not found.");

        if (!item.IsBought)
            throw new DomainException("Item has not been bought yet.");

        item.Unmark();

        // Decrease stock (clamp at 0)
        var stockItem = await _db.StockItems
            .FirstOrDefaultAsync(s => s.FamilyId == familyId && s.IngredientId == item.IngredientId, ct);

        stockItem?.ApplyDelta(-item.Quantity, user.Id);

        _db.StockTransactions.Add(StockTransaction.Create(
            familyId, item.IngredientId, -item.Quantity,
            StockTransactionSource.Correction,
            sourceRefId: item.Id, userId: user.Id,
            notes: "Unbuy shopping list item"));

        await _db.SaveChangesAsync(ct);

        var ingredient = await _db.Ingredients.FindAsync(new object[] { item.IngredientId }, ct);
        return new ShoppingListItemDto(
            item.Id, item.IngredientId, ingredient!.Name, ingredient.Unit,
            item.Quantity, item.IsBought, item.BoughtAt,
            item.SourceMealPlanEntryIds.Count > 0 ? item.SourceMealPlanEntryIds : null);
    }
}
```

- [ ] **Step 5: Wire controller endpoints**

Add to the controller (with the required usings):

```csharp
[HttpPost("{listId:guid}/items/{itemId:guid}/buy")]
public async Task<ActionResult<ShoppingListItemDto>> Buy(
    Guid listId, Guid itemId, CancellationToken ct)
{
    var result = await _mediator.Send(new BuyShoppingListItemCommand(listId, itemId), ct);
    return Ok(result);
}

[HttpPost("{listId:guid}/items/{itemId:guid}/unbuy")]
public async Task<ActionResult<ShoppingListItemDto>> Unbuy(
    Guid listId, Guid itemId, CancellationToken ct)
{
    var result = await _mediator.Send(new UnbuyShoppingListItemCommand(listId, itemId), ct);
    return Ok(result);
}
```

- [ ] **Step 6: Run all buy/unbuy tests**

```bash
dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~BuyUnbuyHandlerTests
```

Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem \
        backend/src/MenuNest.Application/UseCases/ShoppingList/UnbuyShoppingListItem \
        backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs \
        backend/tests/MenuNest.Application.UnitTests/ShoppingList/BuyUnbuyHandlerTests.cs
git commit -m "feat(shopping): add Buy/Unbuy handlers with stock integration"
```

---

## Task 4: AddItem + DeleteItem + Complete + Regenerate handlers + tests

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/AddShoppingListItem/*`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingListItem/*`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/CompleteShoppingList/*`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/DeleteShoppingList/*`
- Create: `backend/src/MenuNest.Application/UseCases/ShoppingList/RegenerateShoppingList/*`
- Modify: `backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs`
- Create: `backend/tests/MenuNest.Application.UnitTests/ShoppingList/RegenerateHandlerTests.cs`

- [ ] **Step 1: Write Regenerate tests**

`backend/tests/MenuNest.Application.UnitTests/ShoppingList/RegenerateHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class RegenerateHandlerTests
{
    [Fact]
    public async Task Preserves_bought_items_and_recomputes_unbought()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var oil = Ingredient.Create(fx.Family.Id, "น้ำมัน", "ขวด");
        fx.Db.Ingredients.AddRange(egg, oil);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 5m);
        recipe.AddIngredient(oil.Id, 2m);
        fx.Db.Recipes.Add(recipe);

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 1m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, oil.Id, 10m, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();

        // Add items: egg (source entry) and oil (source entry)
        list.AddOrIncreaseItem(egg.Id, 4m, new[] { entry.Id });  // egg: missing 4
        list.AddOrIncreaseItem(oil.Id, 1m, new[] { entry.Id });  // oil: was missing 1
        fx.Db.SaveChanges();

        // Mark egg as bought — should be preserved
        var eggItem = list.Items.Single(i => i.IngredientId == egg.Id);
        eggItem.MarkBought(fx.User.Id);
        fx.Db.SaveChanges();

        var sut = new RegenerateShoppingListHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new RegenerateShoppingListCommand(list.Id), CancellationToken.None);

        // Egg item preserved (bought), oil item recomputed
        // Oil: required 2, stock 10 → no shortage → item removed
        result.Items.Should().ContainSingle();
        result.Items[0].IngredientId.Should().Be(egg.Id);
        result.Items[0].IsBought.Should().BeTrue();
    }

    [Fact]
    public async Task Skips_cooked_entries_during_regenerate()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 3m);
        fx.Db.Recipes.Add(recipe);

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        entry.MarkCooked(fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 0m, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();

        list.AddOrIncreaseItem(egg.Id, 3m, new[] { entry.Id });
        fx.Db.SaveChanges();

        var sut = new RegenerateShoppingListHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new RegenerateShoppingListCommand(list.Id), CancellationToken.None);

        // Entry was cooked → excluded → no missing items
        result.Items.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Create all remaining commands + handlers**

Create each handler following the established patterns. The key ones:

**AddShoppingListItem:**
```csharp
// Command: (Guid ListId, Guid IngredientId, decimal Quantity) : ICommand<ShoppingListItemDto>
// Validator: IngredientId NotEmpty, Quantity > 0
// Handler: load list with Include(Items), call list.AddOrIncreaseItem(ingredientId, quantity)
//          save, return DTO. Uses same ingredient lookup as GetDetail for Name+Unit.
```

**DeleteShoppingListItem:**
```csharp
// Command: (Guid ListId, Guid ItemId) : ICommand<Unit>
// Handler: load list, find item, reject if IsBought, call list.RemoveItem(itemId), save.
```

**CompleteShoppingList:**
```csharp
// Command: (Guid Id) : ICommand<ShoppingListDto>
// Handler: load list, call list.Complete(), save, return summary DTO.
```

**DeleteShoppingList:**
```csharp
// Command: (Guid Id) : ICommand<Unit>
// Handler: load list, _db.ShoppingLists.Remove(list), save. No stock reversal.
```

**RegenerateShoppingList:**
```csharp
// Command: (Guid Id) : ICommand<ShoppingListDetailDto>
// Handler:
//   1. Load list with Items (Include)
//   2. Collect ALL SourceMealPlanEntryIds across ALL items (before deleting)
//   3. Remove all unbought items via list.RemoveItem(id) for each unbought
//   4. Load the collected entry ids → filter Status=Planned
//   5. Aggregate required ingredients (same as CreateShoppingList.AutoGenerateItems)
//   6. Compute missing = required - current stock
//   7. For each missing > 0: list.AddOrIncreaseItem(ingredientId, missing, sourceEntryIds)
//      UNLESS a bought item for that ingredient already exists
//   8. Save, return detail DTO
```

Implement each of these in their respective folders, following the exact pattern from Tasks 1-3 (constructor injection of `_db`, `_userProvisioner`, optional `_validator`).

- [ ] **Step 3: Wire all remaining controller endpoints**

Add to `ShoppingListsController.cs`:

```csharp
[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
{
    await _mediator.Send(new DeleteShoppingListCommand(id), ct);
    return NoContent();
}

[HttpPost("{id:guid}/complete")]
public async Task<ActionResult<ShoppingListDto>> Complete(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new CompleteShoppingListCommand(id), ct);
    return Ok(result);
}

[HttpPost("{id:guid}/items")]
public async Task<ActionResult<ShoppingListItemDto>> AddItem(
    Guid id, [FromBody] AddShoppingListItemRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(
        new AddShoppingListItemCommand(id, request.IngredientId, request.Quantity), ct);
    return Created($"/api/shopping-lists/{id}", result);
}

[HttpDelete("{listId:guid}/items/{itemId:guid}")]
public async Task<IActionResult> DeleteItem(Guid listId, Guid itemId, CancellationToken ct)
{
    await _mediator.Send(new DeleteShoppingListItemCommand(listId, itemId), ct);
    return NoContent();
}

[HttpPost("{id:guid}/regenerate")]
public async Task<ActionResult<ShoppingListDetailDto>> Regenerate(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new RegenerateShoppingListCommand(id), ct);
    return Ok(result);
}
```

And request records:

```csharp
public sealed record AddShoppingListItemRequest(Guid IngredientId, decimal Quantity);
```

- [ ] **Step 4: Run full backend test suite**

```bash
cd backend && dotnet test
```

Expected: all green (previous 17 + new ~8 = ~25 total).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/ShoppingList \
        backend/src/MenuNest.WebApi/Controllers/ShoppingListsController.cs \
        backend/tests/MenuNest.Application.UnitTests/ShoppingList
git commit -m "feat(shopping): add AddItem, DeleteItem, Complete, Delete, Regenerate handlers"
```

---

## Task 5: Frontend RTK Query endpoints

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Update DTOs**

Replace the existing `ShoppingListSummaryDto` and add the new types near it:

```ts
export interface ShoppingListDto {
  id: string
  name: string
  status: 'Active' | 'Completed' | 'Archived'
  totalCount: number
  boughtCount: number
  createdAt: string
  completedAt: string | null
}

export interface ShoppingListItemDto {
  id: string
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: number
  isBought: boolean
  boughtAt: string | null
  sourceMealPlanEntryIds: string[] | null
}

export interface ShoppingListDetailDto extends ShoppingListDto {
  items: ShoppingListItemDto[]
}
```

Remove `ShoppingListSummaryDto` and update the existing `listShoppingLists` endpoint to use `ShoppingListDto[]`.

- [ ] **Step 2: Replace the endpoint stub and add all endpoints**

Replace the existing `listShoppingLists` stub and add the full set inside the `endpoints` block:

```ts
// -------------------- Shopping Lists --------------------
listShoppingLists: build.query<ShoppingListDto[], { status?: string }>({
  query: ({ status } = {}) =>
    `/api/shopping-lists${status ? `?status=${status}` : ''}`,
  providesTags: [{ type: 'ShoppingLists', id: 'LIST' }],
}),

getShoppingListDetail: build.query<ShoppingListDetailDto, string>({
  query: (id) => `/api/shopping-lists/${id}`,
  providesTags: (_res, _err, id) => [{ type: 'ShoppingListDetail', id }],
}),

createShoppingList: build.mutation<ShoppingListDto, { name: string; fromDate?: string; toDate?: string }>({
  query: (body) => ({ url: '/api/shopping-lists', method: 'POST', body }),
  invalidatesTags: [{ type: 'ShoppingLists', id: 'LIST' }],
}),

deleteShoppingList: build.mutation<void, string>({
  query: (id) => ({ url: `/api/shopping-lists/${id}`, method: 'DELETE' }),
  invalidatesTags: [{ type: 'ShoppingLists', id: 'LIST' }],
}),

completeShoppingList: build.mutation<ShoppingListDto, string>({
  query: (id) => ({ url: `/api/shopping-lists/${id}/complete`, method: 'POST' }),
  invalidatesTags: (_res, _err, id) => [
    { type: 'ShoppingLists', id: 'LIST' },
    { type: 'ShoppingListDetail', id },
  ],
}),

addShoppingListItem: build.mutation<ShoppingListItemDto, { listId: string; ingredientId: string; quantity: number }>({
  query: ({ listId, ...body }) => ({
    url: `/api/shopping-lists/${listId}/items`,
    method: 'POST',
    body,
  }),
  invalidatesTags: (_res, _err, { listId }) => [
    { type: 'ShoppingListDetail', id: listId },
    { type: 'ShoppingLists', id: 'LIST' },
  ],
}),

deleteShoppingListItem: build.mutation<void, { listId: string; itemId: string }>({
  query: ({ listId, itemId }) => ({
    url: `/api/shopping-lists/${listId}/items/${itemId}`,
    method: 'DELETE',
  }),
  invalidatesTags: (_res, _err, { listId }) => [
    { type: 'ShoppingListDetail', id: listId },
    { type: 'ShoppingLists', id: 'LIST' },
  ],
}),

buyShoppingListItem: build.mutation<ShoppingListItemDto, { listId: string; itemId: string }>({
  query: ({ listId, itemId }) => ({
    url: `/api/shopping-lists/${listId}/items/${itemId}/buy`,
    method: 'POST',
  }),
  invalidatesTags: (_res, _err, { listId }) => [
    { type: 'ShoppingListDetail', id: listId },
    { type: 'ShoppingLists', id: 'LIST' },
    { type: 'Stock', id: 'LIST' },
  ],
}),

unbuyShoppingListItem: build.mutation<ShoppingListItemDto, { listId: string; itemId: string }>({
  query: ({ listId, itemId }) => ({
    url: `/api/shopping-lists/${listId}/items/${itemId}/unbuy`,
    method: 'POST',
  }),
  invalidatesTags: (_res, _err, { listId }) => [
    { type: 'ShoppingListDetail', id: listId },
    { type: 'ShoppingLists', id: 'LIST' },
    { type: 'Stock', id: 'LIST' },
  ],
}),

regenerateShoppingList: build.mutation<ShoppingListDetailDto, string>({
  query: (id) => ({ url: `/api/shopping-lists/${id}/regenerate`, method: 'POST' }),
  invalidatesTags: (_res, _err, id) => [
    { type: 'ShoppingListDetail', id },
    { type: 'ShoppingLists', id: 'LIST' },
  ],
}),
```

- [ ] **Step 3: Export hooks**

Add to the export block at the bottom of `api.ts`:

```ts
useListShoppingListsQuery,
useGetShoppingListDetailQuery,
useCreateShoppingListMutation,
useDeleteShoppingListMutation,
useCompleteShoppingListMutation,
useAddShoppingListItemMutation,
useDeleteShoppingListItemMutation,
useBuyShoppingListItemMutation,
useUnbuyShoppingListItemMutation,
useRegenerateShoppingListMutation,
```

- [ ] **Step 4: Typecheck**

```bash
cd frontend && npx tsc --noEmit
```

Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(api): add full Shopping List RTK Query endpoints"
```

---

## Task 6: ShoppingListsPage (index + create dialog)

**Files:**
- Modify: `frontend/src/pages/shopping/ShoppingListsPage.tsx` — full rewrite
- Create: `frontend/src/pages/shopping/components/ShoppingListCard.tsx`
- Create: `frontend/src/pages/shopping/components/CreateListDialog.tsx`
- Create: `frontend/src/pages/shopping/hooks/useCreateShoppingList.ts`

- [ ] **Step 1: Create components + hook**

Follow the project's component+hook convention. The hook owns the form state (react-hook-form) and mutation. The dialog is pure JSX. The card is a simple display component with a `Link` to `/shopping/:id`. The page is the container that renders the list + dialog.

Key requirements:
- Status filter dropdown (Active / Completed / All) driven by `shoppingSlice.filter`
- Cards show: name, progress (boughtCount / totalCount), a progress bar div, status badge, created date
- Create dialog: name input (default "ซื้อของ {today}"), checkbox "📅 คำนวณจาก meal plan" → date range (2 date inputs), Create button
- Empty state: "ยังไม่มีรายการ — สร้างรายการแรก"
- Use Syncfusion `Button`, `TextBox`, `Dialog`, `DropDownList` per project convention
- Use `useConfirm` for delete (if you add a delete button per card — optional for MVP index)

- [ ] **Step 2: Rewrite ShoppingListsPage.tsx**

Replace the stub with the full page that renders the filter, card list, and create dialog.

- [ ] **Step 3: Typecheck + lint**

```bash
cd frontend && npx tsc --noEmit
```

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/shopping
git commit -m "feat(shopping): implement ShoppingListsPage with create dialog"
```

---

## Task 7: ShoppingListDetailPage (items + buy/unbuy + add item)

**Files:**
- Modify: `frontend/src/pages/shopping/ShoppingListDetailPage.tsx` — full rewrite
- Create: `frontend/src/pages/shopping/components/ShoppingItemRow.tsx`
- Create: `frontend/src/pages/shopping/components/AddItemForm.tsx`
- Create: `frontend/src/pages/shopping/hooks/useShoppingListDetail.ts`

- [ ] **Step 1: Create the hook**

`useShoppingListDetail(listId: string)` — owns:
- `useGetShoppingListDetailQuery(listId)`
- `useBuyShoppingListItemMutation`, `useUnbuyShoppingListItemMutation`
- `useDeleteShoppingListItemMutation`
- `useCompleteShoppingListMutation`, `useRegenerateShoppingListMutation`
- `useConfirm` for destructive actions (unbuy, delete, regenerate, complete)
- Error state

Returns: `{ detail, isLoading, error, handleBuy, handleUnbuy, handleDeleteItem, handleComplete, handleRegenerate, errorMessage }`

- [ ] **Step 2: Create ShoppingItemRow**

Renders one row:
- Unbought: `☐ checkbox` + ingredient name + quantity + unit + optional source tag + 🗑 delete
- Bought: `☑ checked` + ingredient name + quantity + unit + bought time + ↩ undo

- [ ] **Step 3: Create AddItemForm**

Inline form: ingredient autocomplete (`useListIngredientsQuery`, filter out ingredients already in list) + quantity `NumericTextBox` + add button. Uses react-hook-form `Controller`.

- [ ] **Step 4: Rewrite ShoppingListDetailPage.tsx**

Replace the stub. Layout:
- Back link `← Shopping Lists` + list name header
- "🔄 Regenerate" button (shown if any item has sourceMealPlanEntryIds) + "✓ Complete" button
- Progress bar
- Two sections: "ยังไม่ได้ซื้อ" (unbought items) + "ซื้อแล้ว" (bought items)
- `<AddItemForm>` at the bottom

- [ ] **Step 5: Typecheck + lint**

```bash
cd frontend && npx tsc --noEmit
```

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/shopping
git commit -m "feat(shopping): implement ShoppingListDetailPage with buy/unbuy + add item"
```

---

## Task 8: Playwright smoke test

- [ ] **Step 1: Start backend + frontend dev servers**
- [ ] **Step 2: Navigate to /shopping, create a new list with auto-generate from meal plan range**
- [ ] **Step 3: Verify items match expected missing ingredients**
- [ ] **Step 4: Mark one item bought → navigate to /stock → verify stock incremented**
- [ ] **Step 5: Go back → unbuy → verify stock restored**
- [ ] **Step 6: Add a manual item → verify it appears**
- [ ] **Step 7: Complete the list → verify redirect + Completed badge on index**
- [ ] **Step 8: Screenshot evidence + close browser**

---

## Task 9: Final sweep

- [ ] **Step 1: Run full backend test suite**

```bash
cd backend && dotnet test
```

- [ ] **Step 2: Frontend typecheck + lint**

```bash
cd frontend && npx tsc --noEmit && npm run lint
```

- [ ] **Step 3: Verify no regressions on existing pages** (quick manual check: Recipes, Stock, Meal Plan still work)

---

## Self-Review Notes

1. **Spec coverage:** Create list (manual + auto-generate) → Task 2. List + GetDetail → Task 1. Buy/Unbuy → Task 3. AddItem + DeleteItem + Complete + Delete + Regenerate → Task 4. Frontend endpoints → Task 5. Index page → Task 6. Detail page → Task 7. Smoke test → Task 8. All spec sections covered.

2. **No placeholders:** Tasks 1-3 have full code. Task 4 has full code for the test + controller wiring; the CRUD handlers (AddItem, DeleteItem, Complete, Delete) reference existing domain methods directly — the implementer creates files with the same constructor-injection + family-guard + domain-method-call + SaveChangesAsync pattern shown in Tasks 1-3. Regenerate handler logic is described step-by-step. Tasks 6-7 describe component responsibilities and key requirements rather than full JSX (because the UI involves Syncfusion controls whose exact prop names should be read from `node_modules/@syncfusion/react-*` types at implementation time — hardcoding them risks stale API references).

3. **Type consistency:** `ShoppingListDto` / `ShoppingListItemDto` / `ShoppingListDetailDto` used consistently across backend DTOs, frontend interfaces, and RTK Query endpoints. Domain method names: `MarkBought` (not `MarkAsBought`), `Unmark` (not `MarkUnbought`), `Complete` (not `MarkComplete`), `AddOrIncreaseItem` (not `AddItem`).
