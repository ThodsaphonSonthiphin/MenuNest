# Dashboard Page — Design Spec

## Goal

Replace the placeholder Dashboard (`/`) with a useful landing page showing
today's and tomorrow's meals in a grid, stock status badges, and quick stats.
No new backend endpoints — uses existing RTK Query hooks only.

## Layout

Stacked sections, single page:

1. **Header** — greeting + current date
2. **Meal Grid** — Syncfusion Grid, 2 rows (today/tomorrow) × 3 columns (breakfast/lunch/dinner)
3. **Stats Row** — 3 stat cards (recipe count, ingredient count, meals this week)
4. **Action** — "ดูแผนทั้งหมด" button → navigates to `/meal-plan`

## Data Sources

All existing — no new endpoints needed:

| Hook | Purpose | Params |
|------|---------|--------|
| `useListMealPlanQuery` | Today + tomorrow meals | `{ from: todayISO, to: tomorrowISO }` |
| `useStockCheckBatchQuery` | Stock status per meal entry | `{ entryIds: [...] }`, skip if no entries |
| `useListRecipesQuery` | Recipe count for stats | void |
| `useListIngredientsQuery` | Ingredient count for stats | void |
| `useCurrentUser` | Family name, display name | — |

## Section 1 — Header

```
สวัสดี, ครอบครัว {familyName}
วันจันทร์ที่ 14 เมษายน 2569
```

- Family name from `useCurrentUser().familyName`
- Date formatted in Thai locale: `new Date().toLocaleDateString('th-TH', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })`

## Section 2 — Meal Grid

Syncfusion `Grid` (`@syncfusion/react-grid`), read-only, no toolbar.

**Data shape:** Transform `MealPlanEntryDto[]` into 2 rows:

```typescript
interface MealGridRow {
  label: string           // "วันนี้" or "พรุ่งนี้"
  date: string            // ISO date
  breakfast: MealPlanEntryDto | null
  lunch: MealPlanEntryDto | null
  dinner: MealPlanEntryDto | null
}
```

**Columns:**

| Column | Field | Width | Template |
|--------|-------|-------|----------|
| วัน | `label` | 100px | Bold text, orange for today, muted for tomorrow |
| 🌅 เช้า | `breakfast` | auto | MealCellTemplate |
| ☀️ กลางวัน | `lunch` | auto | MealCellTemplate |
| 🌙 เย็น | `dinner` | auto | MealCellTemplate |

**MealCellTemplate** renders:
- If entry exists + status Planned: `{recipeName}` + stock badge (✅ sufficient / ⚠️ short N)
- If entry exists + status Cooked: `{recipeName}` + ✓ cooked (muted, line-through)
- If no entry: "—" in muted text

**Stock badges:** Use `useStockCheckBatchQuery` with all entry IDs. Map
`StockCheckDto.isSufficient` → ✅, `!isSufficient` → ⚠️ with `missingCount`.

**Click behavior:** Click any cell → `navigate('/meal-plan')`. No inline
editing on the Dashboard.

## Section 3 — Stats Row

Three inline stat cards (plain `<div>` with CSS, no Syncfusion component
needed since these are simple display elements):

| Stat | Value | Color | Icon |
|------|-------|-------|------|
| Recipes | `recipes?.length ?? 0` | Orange (`#FFF3E0` / `#F57C00`) | 📖 |
| Ingredients | `ingredients?.length ?? 0` | Blue (`#E3F2FD` / `#1565C0`) | 🥬 |
| Meals This Week | count of entries from a 7-day query | Green (`#E8F5E9` / `#2E7D32`) | 📅 |

For "Meals This Week": reuse the meal plan query but with a 7-day range
(Monday–Sunday of current week). This can be a separate
`useListMealPlanQuery` call with the week range.

## Section 4 — Action Button

```tsx
<Button variant={Variant.Filled} color={Color.Primary} onClick={() => navigate('/meal-plan')}>
  ดูแผนทั้งหมด →
</Button>
```

## Empty State

If no meals planned for today or tomorrow:

```
ยังไม่มีแผนมื้ออาหาร — กดปุ่มด้านล่างเพื่อเริ่มวางแผน
[ดูแผนทั้งหมด →]
```

## Loading State

Show "กำลังโหลด..." while meal plan query is loading. Stats can load
independently (each has its own loading state).

## Files

### New Files

| File | Purpose |
|------|---------|
| `pages/dashboard/hooks/useDashboard.ts` | Hook: fetches meals, stock checks, stats, transforms to grid rows |

### Modified Files

| File | Change |
|------|--------|
| `pages/dashboard/DashboardPage.tsx` | Full implementation replacing stub |

## Syncfusion Components

| Component | Package | Usage |
|-----------|---------|-------|
| `Grid`, `Column`, `Columns` | `@syncfusion/react-grid` | Meal grid (read-only) |
| `Button` | `@syncfusion/react-buttons` | "ดูแผนทั้งหมด →" action |

## Out of Scope

- Click meal cell to open stock check modal (just navigate to `/meal-plan`)
- Drag-drop meals from dashboard
- Notifications or alerts
- Weather or external data
- Customizable widget layout
