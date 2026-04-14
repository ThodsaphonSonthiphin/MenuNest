# Shopping List — Design

**Status:** Approved — ready for implementation planning
**Date:** 2026-04-14
**Author:** Brainstormed with Claude (MenuNest project)

## Problem

Users plan multiple meals per week and need to know what ingredients to
buy. Today they must manually compare the meal plan against their stock —
no tooling helps. A shopping list that auto-generates from the plan's
missing ingredients and auto-updates stock when items are marked bought
closes this loop.

## Goals

- Create shopping lists manually (empty) or auto-generated from a meal
  plan date range.
- Auto-generation computes missing ingredients: sum required across all
  **Planned** entries in the range, subtract current stock, list the
  shortfall.
- Mark an item bought → stock automatically increases by that quantity.
- Uncheck a bought item → stock decreases (clamped at zero) to stay
  symmetric.
- Regenerate a list from its original meal plan range: delete all
  unbought items, recompute missing, preserve bought items (their stock
  increment is already committed).
- Complete a list to move it out of the active view.

## Non-goals (MVP)

- Inline quantity editing — delete + re-add is sufficient.
- Archived status — only Active and Completed.
- Real-time collaboration on the same list.
- Drag-to-reorder items.
- Multiple units per ingredient (the app uses one fixed unit per
  ingredient globally).

## Data Model

No new tables or migrations — the initial migration already created
`ShoppingList`, `ShoppingListItem`, and the `StockTransaction` table
with `Source = ShoppingListBought` enum value.

### Existing tables used

```
ShoppingList
  Id, FamilyId, Name,
  Status (Active / Completed / Archived),
  CreatedAt, CreatedByUserId, CompletedAt

ShoppingListItem
  Id, ShoppingListId, IngredientId, Quantity,
  IsBought, BoughtAt, BoughtByUserId,
  SourceMealPlanEntryIds (JSON, nullable)
  UNIQUE (ShoppingListId, IngredientId)

StockItem        — updated on buy/unbuy
StockTransaction — audit row on buy/unbuy
```

### Domain methods to add

`ShoppingList`:
- `Complete(userId)` → sets `Status = Completed`, `CompletedAt = utcNow`
- Guard: only Active lists can be completed.

`ShoppingListItem`:
- `MarkBought(userId)` → sets `IsBought = true`, `BoughtAt = utcNow`,
  `BoughtByUserId = userId`. Guard: must not already be bought.
- `MarkUnbought()` → sets `IsBought = false`, clears `BoughtAt` and
  `BoughtByUserId`. Guard: must be bought.

## Backend API

| Endpoint | Method | Body / Query | Response | Notes |
|---|---|---|---|---|
| `/api/shopping-lists` | GET | `?status=Active\|Completed` | `ShoppingListDto[]` | Each DTO includes `boughtCount` and `totalCount` for progress |
| `/api/shopping-lists` | POST | `{ name, fromDate?, toDate? }` | `ShoppingListDto` | If dates → auto-generate items from Planned entries' missing ingredients |
| `/api/shopping-lists/{id}` | GET | — | `ShoppingListDetailDto` | List metadata + all items |
| `/api/shopping-lists/{id}` | DELETE | — | 204 | Hard delete; any bought items' stock increments are NOT reversed |
| `/api/shopping-lists/{id}/complete` | POST | — | `ShoppingListDto` | Status → Completed |
| `/api/shopping-lists/{id}/items` | POST | `{ ingredientId, quantity }` | `ShoppingListItemDto` | Manual add; rejects if ingredient already in list (unique constraint) |
| `/api/shopping-lists/{id}/items/{itemId}` | DELETE | — | 204 | Only unbought items can be deleted |
| `/api/shopping-lists/{id}/items/{itemId}/buy` | POST | — | `ShoppingListItemDto` | See Buy handler below |
| `/api/shopping-lists/{id}/items/{itemId}/unbuy` | POST | — | `ShoppingListItemDto` | See Unbuy handler below |
| `/api/shopping-lists/{id}/regenerate` | POST | — | `ShoppingListDetailDto` | See Regenerate handler below |

### DTO shapes

```csharp
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

### Auto-generate logic (CreateShoppingList with dates)

```
1. Load all MealPlanEntries with Status = Planned
   in [fromDate, toDate] for this family.
2. Load their recipes + ingredients (Include).
3. Aggregate required[ingredientId] across all entries
   (same pattern as stock-check-batch / cook-batch).
4. Load StockItems for those ingredients.
5. For each ingredient: missing = max(0, required - available).
6. For each ingredient with missing > 0:
   → ShoppingListItem.Create(
       ingredientId, quantity = missing,
       sourceMealPlanEntryIds = [entry ids that reference this ingredient])
7. Save ShoppingList + items in one SaveChangesAsync.
```

The list stores `fromDate` and `toDate` implicitly via
`SourceMealPlanEntryIds` on each item — regenerate walks those entry ids
back to recipes to recompute.

### Buy handler

```
1. Validate: item exists, belongs to caller's family, IsBought = false.
2. item.MarkBought(userId).
3. StockItem for this ingredient:
     exists → stockItem.SetQuantity(stockItem.Quantity + item.Quantity, userId)
     missing → StockItem.Create(familyId, ingredientId, item.Quantity, userId)
                + db.StockItems.Add(...)
4. StockTransaction.Create(
     delta = +item.Quantity,
     Source = ShoppingListBought,
     sourceRefId = item.Id,
     userId, notes = null)
5. SaveChangesAsync
```

### Unbuy handler

```
1. Validate: item.IsBought = true.
2. item.MarkUnbought().
3. StockItem.ApplyDelta(-item.Quantity, userId)  — clamps at 0.
4. StockTransaction.Create(
     delta = -item.Quantity,
     Source = Correction,
     sourceRefId = item.Id,
     userId, notes = "Unbuy shopping list item")
5. SaveChangesAsync
```

### Regenerate handler

```
1. Load list with items.
2. Collect all SourceMealPlanEntryIds across all items (bought + unbought).
3. Delete all unbought items from the DB.
4. Re-run the auto-generate logic using the collected entry ids
   (filter to Status = Planned only — entries that were cooked since
   the list was created are skipped).
5. For each ingredient with missing > 0, create a new ShoppingListItem
   UNLESS a bought item for that ingredient already exists (unique
   constraint: one item per ingredient per list).
6. Return the refreshed list.
```

### Validation (FluentValidation)

- `CreateShoppingList`: name required (max 200), dates optional but if
  one is set both must be, `fromDate <= toDate`.
- `AddShoppingListItem`: ingredientId required, quantity > 0.
- All handlers verify family ownership via `IUserProvisioner.RequireFamilyAsync`.

## Frontend

### Routes

| Path | Component |
|---|---|
| `/shopping` | `ShoppingListsPage` — index with card list + create dialog |
| `/shopping/:id` | `ShoppingListDetailPage` — items, buy/unbuy, add, regenerate, complete |

### RTK Query endpoints (in `api.ts`)

```ts
listShoppingLists:       query   GET  /api/shopping-lists?status=...
getShoppingListDetail:   query   GET  /api/shopping-lists/{id}
createShoppingList:      mutation POST /api/shopping-lists
deleteShoppingList:      mutation DELETE /api/shopping-lists/{id}
completeShoppingList:    mutation POST /api/shopping-lists/{id}/complete
addShoppingListItem:     mutation POST /api/shopping-lists/{id}/items
deleteShoppingListItem:  mutation DELETE /api/shopping-lists/{id}/items/{itemId}
buyShoppingListItem:     mutation POST /api/shopping-lists/{id}/items/{itemId}/buy
unbuyShoppingListItem:   mutation POST /api/shopping-lists/{id}/items/{itemId}/unbuy
regenerateShoppingList:  mutation POST /api/shopping-lists/{id}/regenerate
```

Tags:
- `'ShoppingLists'` id `'LIST'` — invalidated by create, delete, complete.
- `'ShoppingListDetail'` id `{listId}` — invalidated by add/delete/buy/unbuy/regenerate.
- Buy and unbuy also invalidate `{ type: 'Stock', id: 'LIST' }` since stock changes.

### File structure

```
pages/shopping/
  components/
    ShoppingListCard.tsx       -- card on index (name, progress bar, status)
    ShoppingItemRow.tsx        -- row (checkbox + name + qty + source tag + delete/undo)
    CreateListDialog.tsx       -- name + optional date range
    AddItemForm.tsx            -- ingredient autocomplete + quantity
  hooks/
    useShoppingListDetail.ts   -- buy/unbuy/delete item/regenerate/complete logic
    useCreateShoppingList.ts   -- create form logic
  shoppingSlice.ts             -- searchTerm for filtering list names
  ShoppingListsPage.tsx
  ShoppingListDetailPage.tsx
  index.ts
```

### Index page (`/shopping`)

- Header: "Shopping Lists" + "+ สร้างรายการ" button
- Optional status filter dropdown (Active / Completed / All, default Active)
- Card list: each card shows name, progress (e.g. "5 / 12 bought"), a
  progress bar, status badge, created date. Click → navigate to detail.
- Empty state: "ยังไม่มีรายการ — สร้างรายการแรก"
- Create dialog: name input (default "ซื้อของ {today Thai date}") +
  checkbox "📅 คำนวณจาก meal plan" → date range picker. Create → POST
  → redirect to `/shopping/{id}`.

### Detail page (`/shopping/:id`)

- Header: list name + "🔄 Regenerate" button (shown only when any item
  in the list carries `sourceMealPlanEntryIds` — i.e. the list was
  auto-generated from a meal plan range) + "✓ Complete" button
- Progress bar: "5 / 12 bought"
- Two sections:
  - **ยังไม่ได้ซื้อ** — unbought items, each row: ☐ checkbox + ingredient
    name + quantity + unit + optional source tag ("จาก: ไข่ทอด 16 เม.ย.")
    + 🗑 delete
  - **ซื้อแล้ว** — bought items: ☑ checked + ingredient name + quantity +
    unit + bought timestamp + ↩ undo
- "+ เพิ่มรายการ" — inline form with ingredient autocomplete (from
  master, excluding ingredients already in the list) + quantity input +
  add button
- Complete → confirm dialog → POST complete → redirect to index
- Regenerate → confirm dialog ("items ที่ยังไม่ได้ซื้อจะถูกคำนวณใหม่ —
  items ที่ซื้อแล้วจะไม่เปลี่ยน") → POST regenerate → refresh

### Interactions

| Action | API call | Stock effect | UI update |
|---|---|---|---|
| ☐ click (buy) | `POST /buy` | +quantity | Row moves to ซื้อแล้ว section |
| ↩ click (unbuy) | Confirm → `POST /unbuy` | −quantity (clamped 0) | Row moves back to ยังไม่ได้ซื้อ |
| 🗑 click | Confirm → `DELETE /items/{id}` | none | Row removed |
| + เพิ่มรายการ | `POST /items` | none | New row appears in ยังไม่ได้ซื้อ |
| 🔄 Regenerate | Confirm → `POST /regenerate` | none | Unbought items refreshed |
| ✓ Complete | Confirm → `POST /complete` | none | Redirect to index |

## Tests

### Application unit tests (xUnit + Moq + InMemory DbContext)

Using the existing `HandlerTestFixture` from Task 3.

- **CreateShoppingListHandler**
  - Empty list (no dates): creates list with 0 items.
  - With date range: seeds 2 Planned entries with known ingredients +
    stock → items reflect missing quantities only.
  - Cooked entries in range are excluded.
  - Dates provided but no Planned entries → list created with 0 items.

- **BuyShoppingListItemHandler**
  - Happy path: IsBought flips, stock increases, StockTransaction written
    with Source=ShoppingListBought.
  - StockItem doesn't exist yet → created with quantity = item.Quantity.
  - Already bought → DomainException.

- **UnbuyShoppingListItemHandler**
  - Happy path: IsBought flips back, stock decreases (clamped),
    StockTransaction with Source=Correction.
  - Not bought → DomainException.

- **RegenerateShoppingListHandler**
  - Unbought items deleted, new items computed from Planned entries.
  - Bought items preserved.
  - Entries cooked since list creation are skipped.

### Smoke test (Playwright)

- Create a list with auto-generate from meal plan range.
- Verify items match expected missing ingredients.
- Mark one item bought → verify stock page shows increment.
- Unbuy → verify stock restored.
- Add a manual item → verify it appears.
- Regenerate → verify unbought items refreshed.
- Complete → verify redirect to index with Completed badge.

## Out of scope (follow-ups)

- Inline quantity editing on items.
- Archived status + archive/unarchive flow.
- Sharing a list link outside the family.
- Push notifications when someone buys an item.
- Print-friendly view.
