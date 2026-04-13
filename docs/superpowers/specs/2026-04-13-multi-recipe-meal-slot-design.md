# Multi-Recipe Meal Slot — Design

**Status:** Approved — ready for implementation planning
**Date:** 2026-04-13
**Author:** Brainstormed with Claude (MenuNest project)

## Problem

Today the meal plan enforces `UNIQUE (FamilyId, Date, MealSlot)` — each slot
(Breakfast / Lunch / Dinner) holds exactly one recipe. Real meals are usually
courses eaten together — rice + curry + soup + dessert. Users want to plan
those as a set and cook them together.

The current backend rejects the second `POST /api/meal-plan` for a slot with
`400 "This meal slot is already occupied"`, and the UI has no path to add or
view multiple recipes per slot.

## Goals

- Multiple recipes per (date, slot) — no upper limit.
- Each recipe in a slot is a distinct entry that may be cooked independently
  or as part of a batch.
- Single user gesture to cook several entries at once: deduct all ingredients
  in one transaction and mark them all `Cooked`.
- Stock check works against the *selected* subset, so the user sees aggregated
  required vs. on-hand for whatever they're about to cook.

## Non-goals

- No "course order" / drag-and-drop ordering inside a slot — entries are an
  unordered set; we render them in creation order.
- No per-portion serving counts (use the recipe's ingredient quantities — if
  you need 2 servings, plan two entries of the same recipe).
- No "cook session" parent record. Audit lives in `StockTransaction` rows.
- No realtime collaboration on selection state — selections are local to the
  client until the user clicks Cook.

## Data Model

Single change: **drop the unique constraint** on the slot tuple and keep the
column as a non-unique index (still useful for the date-range query).

```
MealPlanEntry
  Id, FamilyId, Date, MealSlot, RecipeId, Notes,
  Status, CookedAt, CookedByUserId, CookNotes,
  CreatedByUserId, CreatedAt

  -- BEFORE: UNIQUE (FamilyId, Date, MealSlot)
  -- AFTER:  INDEX  (FamilyId, Date, MealSlot)   -- non-unique
```

Duplicate recipes in the same slot are allowed (e.g. "ข้าวสวย" twice); we
trust the user. If this turns into a UX problem, a follow-up can introduce
either a `UNIQUE (FamilyId, Date, MealSlot, RecipeId)` constraint or a
quantity field on the entry.

`StockTransaction` keeps its existing shape:

```
StockTransaction
  Id, FamilyId, IngredientId, Delta, Source, SourceRefId, CreatedAt, ...
```

For a batch cook, we write **one row per ingredient deducted** with
`Source = Cook` and `SourceRefId = <first entry id of the batch>`. We
intentionally do not introduce a `CookSession` parent — MVP audit only needs
to know "this delta came from cooking", and the entry id is enough to walk
back to the meal. If a later feature needs grouped audit, a non-breaking
`CookSessionId` column can be added.

## Migration

One EF Core migration:

1. Drop existing index `IX_MealPlanEntries_FamilyId_Date_MealSlot`.
2. Recreate it without `IsUnique`.

No data migration — existing rows continue to satisfy the relaxed index.

## Backend API

| Endpoint | Change |
|---|---|
| `POST /api/meal-plan` | Remove the "slot already occupied" guard in `CreateMealPlanEntryHandler`. Validation otherwise unchanged. |
| `GET /api/meal-plan?from=&to=` | No change. Returns the flat list; the client groups by `(date, mealSlot)`. |
| `GET /api/meal-plan/{id}/stock-check` | Keep as-is for parity with single-entry flows. |
| **NEW** `POST /api/meal-plan/stock-check-batch` | Body: `{ entryIds: Guid[] }`. Aggregates required ingredients across the listed entries and reports per-ingredient totals + missing. |
| **NEW** `POST /api/meal-plan/cook-batch` | Body: `{ entryIds: Guid[] }`. Aggregates ingredients across the listed entries, deducts in one transaction, marks every entry `Cooked`. Returns `{ deducted, partial, cookedEntryIds }`. |
| `POST /api/meal-plan/{id}/uncook` | Unchanged — undo is per-entry. |

`POST /api/meal-plan/{id}/cook` (the single-entry cook from the M4 plan) is
**not** built separately — `cook-batch` with a one-element list is the
canonical cook entry point. Single-entry undo (`/{id}/uncook`) stays as-is
because users may want to undo one course of a meal without uncooking the
whole set.

### `stock-check-batch` response shape

```jsonc
{
  "lines": [
    { "ingredientId": "…", "ingredientName": "ไข่ไก่", "unit": "ฟอง",
      "required": 4, "available": 26, "missing": 0 },
    { "ingredientId": "…", "ingredientName": "ข้าวสาร", "unit": "ถ้วย",
      "required": 2, "available": 1, "missing": 1 }
  ],
  "isSufficient": false,
  "missingCount": 1
}
```

Same shape as the single-entry stock-check, just aggregated across the input
entries. The endpoint validates that every entry belongs to the caller's
family.

### `cook-batch` semantics

```
CookBatchCommand(IReadOnlyList<Guid> EntryIds)
  → CookBatchResult(
      IReadOnlyList<CookDeducted>     deducted,    // ingredients we removed
      IReadOnlyList<CookShortfall>    partial,     // ingredients we couldn't fully cover
      IReadOnlyList<Guid>             cookedEntryIds)

CookDeducted(Guid IngredientId, string IngredientName, string Unit, decimal Amount)
CookShortfall(Guid IngredientId, string IngredientName, string Unit,
              decimal Required, decimal Deducted, decimal Missing)
```

Handler outline:

1. Load entries; reject (`400`) if any is not in the caller's family or has
   `Status != Planned`. The error detail names the offending entry id so the
   UI can refresh.
2. Load `RecipeIngredient` rows for the recipes referenced by the batch.
3. Aggregate `required[ingredientId]` across the batch.
4. Load `StockItem` rows for those ingredients (single query).
5. For each ingredient: `deduction = min(required, available)`,
   `shortfall = required - deduction`. Clamp `StockItem.Quantity` at 0 (no
   negatives), per existing single-cook behaviour.
6. Inside one EF transaction:
   - Update `StockItem.Quantity`.
   - Insert one `StockTransaction` per ingredient **with non-zero
     deduction** (`Source = Cook`, `SourceRefId = entryIds[0]`,
     `Notes = "Batch cook of N entries"`). Ingredients with zero on-hand
     are skipped — there's nothing to record.
   - Update each entry: `Status = Cooked`, `CookedAt = utcNow`,
     `CookedByUserId = currentUser`, and if the batch had any shortfall set
     `CookNotes` on every entry to a Thai summary like
     `"ขาด ข้าวสาร 1 ถ้วย — ใช้เท่าที่มี"`.
7. Return the deducted/partial breakdown plus the entry ids that flipped to
   Cooked, so the UI can update without a refetch.

The handler is wrapped by the existing `TransactionBehavior` pipeline.

## Frontend Changes

### Routing & state

`mealPlanSlice.focusedEntryId: string | null`
→ `focusedSlot: { date: string; slot: MealSlot } | null`

The detail dialog is now opened against a slot, not a single entry. Picking
an existing event on the Scheduler resolves it back to its `(date, slot)`
and opens the same dialog.

### Components

- **`MealSlotDetailContent`** (replaces `EntryDetailContent`) — receives
  `entries: MealPlanEntryDto[]` for the focused slot.
  - Renders a Syncfusion `Grid` (or a plain table; Grid gives us the
    checkbox column for free) with columns:
    `[ ☐ select | Recipe | Stock | Status / actions ]`.
  - Selection state is `useState<Set<string>>` of entry ids; only `Planned`
    rows are selectable, `Cooked` rows show the timestamp + Undo button.
  - Footer: `+ เพิ่ม recipe`, `Cancel`, **`Cook selected (n)`**. Cook is
    disabled when 0 selected.
  - Calls `useStockCheckBatchQuery({ entryIds: Array.from(selected) })`
    (skip when set is empty) and renders the warning banner from the
    response.
  - Handler for Cook calls the new mutation, then `closeDetail()` on
    success; the cache invalidation tag refreshes the Scheduler.
- **Picker dialog (`RecipePickerForm`)** — unchanged. The *open trigger*
  changes: an empty cell still opens the picker for a *new* slot, and the
  detail dialog's `+ เพิ่ม recipe` button opens the same picker preloaded
  with the slot's `(date, slot)`.

### RTK Query

Single `createApi` instance — add two endpoints next to the existing
meal-plan ones:

```
stockCheckBatch: build.query<StockCheckDto, { entryIds: string[] }>
cookMealPlanBatch: build.mutation<CookBatchResult, { entryIds: string[] }>
```

`cookMealPlanBatch` invalidates `[{ type: 'MealPlan', id: 'LIST' }, { type: 'Stock', id: 'LIST' }]`.

`stockCheckBatch` is per-set (cache key derived from sorted entry ids), so a
toggle cycle that produces the same set hits the cache.

### Scheduler

No code change — Syncfusion Scheduler already stacks overlapping events in
the same time band. With the unique constraint dropped, a slot with three
recipes naturally renders as three side-by-side appointments under the
Breakfast row.

## Validation & Errors

- `cook-batch` rejects empty `entryIds`, mixed-family ids, and
  non-`Planned` statuses with a `400` ProblemDetails carrying the offending
  id list. The UI surfaces this in the standard error banner and the user
  can refetch.
- `stock-check-batch` accepts empty `entryIds` but returns
  `{ lines: [], isSufficient: true, missingCount: 0 }` so the UI can call
  it unconditionally.
- The dropped unique constraint means duplicate `POST` is no longer caught
  by the DB. There is no app-side replacement — duplicates are intentional.

## Tests

- **Application unit tests**
  - `CookBatchHandler` — happy path (sufficient stock), short stock
    (clamps + writes CookNotes), mixed statuses (rejects), cross-family
    entry id (rejects).
  - `StockCheckBatchHandler` — aggregates across entries, returns 0 missing
    when stock matches, returns missing breakdown when short.
- **Infrastructure integration test** — drop-and-recreate index migration
  applies cleanly against an empty DB and against a DB that already has
  one entry per slot.
- **Frontend** — Vitest on `MealSlotDetailContent` covering: select +
  cook flow updates table state, partial stock surfaces banner, Undo on a
  cooked row calls the existing single-entry uncook endpoint.

## Out of scope (follow-ups, if needed)

- `CookSession` table for grouped audit (only if shopping-list / reporting
  needs it).
- `UNIQUE (FamilyId, Date, MealSlot, RecipeId)` if duplicate-recipe-in-slot
  becomes a real UX issue.
- Drag-to-reorder courses inside a slot.
- Per-entry "skip without cooking" status (today: delete the entry).
