# Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder Dashboard page with a meal grid (today/tomorrow × 3 slots), stock badges, and quick stats.

**Architecture:** Single page component + one hook. No new backend endpoints — reuses existing RTK Query hooks (`useListMealPlanQuery`, `useStockCheckBatchQuery`, `useListRecipesQuery`, `useListIngredientsQuery`). Syncfusion Grid for the meal table.

**Tech Stack:** React 19, RTK Query, Syncfusion Pure React Grid + Buttons, TypeScript.

**Spec:** `docs/superpowers/specs/2026-04-14-dashboard-design.md`

---

## File Map

### New Files

| File | Purpose |
|------|---------|
| `frontend/src/pages/dashboard/hooks/useDashboard.ts` | Fetches meals, stock checks, stats; transforms to grid rows |

### Modified Files

| File | Change |
|------|--------|
| `frontend/src/pages/dashboard/DashboardPage.tsx` | Full implementation replacing stub |

---

## Task 1: Create useDashboard Hook

**Files:**
- Create: `frontend/src/pages/dashboard/hooks/useDashboard.ts`

- [ ] **Step 1: Create the hooks directory**

Run: `mkdir -p c:/Repo2/t/menunest/frontend/src/pages/dashboard/hooks`

- [ ] **Step 2: Create useDashboard.ts**

This hook:
1. Computes today/tomorrow ISO dates
2. Fetches meal plan for today+tomorrow
3. Fetches stock check batch for all entries
4. Fetches recipes + ingredients for counts
5. Fetches meal plan for the current week (Mon-Sun) for "meals this week" stat
6. Transforms entries into 2 grid rows

```typescript
import { useMemo } from 'react'
import {
  useListMealPlanQuery,
  useStockCheckBatchQuery,
  useListRecipesQuery,
  useListIngredientsQuery,
} from '../../../shared/api/api'
import type { MealPlanEntryDto, StockCheckDto } from '../../../shared/api/api'

export interface MealGridRow {
  label: string
  date: string
  breakfast: MealPlanEntryDto | null
  lunch: MealPlanEntryDto | null
  dinner: MealPlanEntryDto | null
}

function toISODate(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function getMonday(d: Date): Date {
  const date = new Date(d)
  const day = date.getDay()
  const diff = day === 0 ? -6 : 1 - day
  date.setDate(date.getDate() + diff)
  return date
}

function addDays(d: Date, n: number): Date {
  const date = new Date(d)
  date.setDate(date.getDate() + n)
  return date
}

export function useDashboard() {
  const today = new Date()
  const tomorrow = addDays(today, 1)
  const todayISO = toISODate(today)
  const tomorrowISO = toISODate(tomorrow)

  const monday = getMonday(today)
  const sunday = addDays(monday, 6)
  const mondayISO = toISODate(monday)
  const sundayISO = toISODate(sunday)

  // Meal plan: today + tomorrow
  const { data: meals, isLoading: isLoadingMeals } = useListMealPlanQuery({
    from: todayISO,
    to: tomorrowISO,
  })

  // Meal plan: full week (for stats)
  const { data: weekMeals } = useListMealPlanQuery({
    from: mondayISO,
    to: sundayISO,
  })

  // Stock check batch for all entries
  const entryIds = useMemo(() => meals?.map((m) => m.id) ?? [], [meals])
  const { data: stockChecks } = useStockCheckBatchQuery(
    { entryIds },
    { skip: entryIds.length === 0 },
  )

  // Stats counts
  const { data: recipes } = useListRecipesQuery()
  const { data: ingredients } = useListIngredientsQuery()

  // Transform meals into grid rows
  const gridRows = useMemo<MealGridRow[]>(() => {
    const findEntry = (date: string, slot: string) =>
      meals?.find((m) => m.date === date && m.mealSlot === slot) ?? null

    return [
      {
        label: 'วันนี้',
        date: todayISO,
        breakfast: findEntry(todayISO, 'Breakfast'),
        lunch: findEntry(todayISO, 'Lunch'),
        dinner: findEntry(todayISO, 'Dinner'),
      },
      {
        label: 'พรุ่งนี้',
        date: tomorrowISO,
        breakfast: findEntry(tomorrowISO, 'Breakfast'),
        lunch: findEntry(tomorrowISO, 'Lunch'),
        dinner: findEntry(tomorrowISO, 'Dinner'),
      },
    ]
  }, [meals, todayISO, tomorrowISO])

  // Build a map of entryId → stock check result
  const stockCheckMap = useMemo<Record<string, { isSufficient: boolean; missingCount: number }>>(() => {
    if (!stockChecks || !meals) return {}
    const map: Record<string, { isSufficient: boolean; missingCount: number }> = {}
    for (const entry of meals) {
      const check = (stockChecks as { checks?: StockCheckDto[] })?.checks?.find(
        (c: StockCheckDto) => c.mealPlanEntryId === entry.id,
      )
      if (check) {
        map[entry.id] = { isSufficient: check.isSufficient, missingCount: check.missingCount }
      }
    }
    return map
  }, [stockChecks, meals])

  const thaiDate = today.toLocaleDateString('th-TH', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  })

  return {
    gridRows,
    stockCheckMap,
    isLoadingMeals,
    thaiDate,
    recipeCount: recipes?.length ?? 0,
    ingredientCount: ingredients?.length ?? 0,
    mealsThisWeek: weekMeals?.length ?? 0,
    hasMeals: (meals?.length ?? 0) > 0,
  }
}
```

- [ ] **Step 3: Build to verify**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/dashboard/hooks/
git commit -m "feat(dashboard): add useDashboard hook with meal grid + stats"
```

---

## Task 2: Implement DashboardPage

**Files:**
- Modify: `frontend/src/pages/dashboard/DashboardPage.tsx`

- [ ] **Step 1: Replace DashboardPage with full implementation**

```tsx
import { useNavigate } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import type { MealPlanEntryDto } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useDashboard } from './hooks/useDashboard'
import type { MealGridRow } from './hooks/useDashboard'

export function DashboardPage() {
  const { familyName } = useCurrentUser()
  const navigate = useNavigate()
  const {
    gridRows,
    stockCheckMap,
    isLoadingMeals,
    thaiDate,
    recipeCount,
    ingredientCount,
    mealsThisWeek,
    hasMeals,
  } = useDashboard()

  /* ---------- Column templates ---------- */

  const LabelTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) => (
    <span
      style={{
        fontWeight: 600,
        color: row.label === 'วันนี้' ? 'var(--color-primary-dark)' : 'var(--color-text-muted)',
      }}
    >
      {row.label}
    </span>
  )

  const MealCellTemplate = (entry: MealPlanEntryDto | null) => {
    if (!entry) {
      return (
        <span style={{ color: 'var(--color-text-muted)', fontSize: 13 }}>—</span>
      )
    }

    const isCooked = entry.status === 'Cooked'
    const stock = stockCheckMap[entry.id]

    return (
      <div
        style={{ cursor: 'pointer' }}
        onClick={() => navigate('/meal-plan')}
      >
        <span
          style={{
            fontWeight: 500,
            fontSize: 13,
            textDecoration: isCooked ? 'line-through' : undefined,
            color: isCooked ? 'var(--color-text-muted)' : undefined,
          }}
        >
          {entry.recipeName}
        </span>
        {isCooked ? (
          <span style={{ marginLeft: 6, fontSize: 11, color: 'var(--color-text-muted)' }}>
            ✓ ทำแล้ว
          </span>
        ) : stock ? (
          <span
            style={{
              marginLeft: 6,
              fontSize: 11,
              color: stock.isSufficient ? '#2E7D32' : '#E65100',
            }}
          >
            {stock.isSufficient ? '✅' : `⚠️ ขาด ${stock.missingCount}`}
          </span>
        ) : null}
      </div>
    )
  }

  const BreakfastTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.breakfast)
  const LunchTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.lunch)
  const DinnerTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.dinner)

  return (
    <section className="page page--dashboard">
      {/* Header */}
      <header className="page__header" style={{ flexDirection: 'column', alignItems: 'flex-start', gap: 2 }}>
        <h1>สวัสดี, {familyName ?? 'ครอบครัว'}</h1>
        <p style={{ color: 'var(--color-text-muted)', fontSize: 14, margin: 0 }}>
          {thaiDate}
        </p>
      </header>

      {/* Meal Grid */}
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>มื้ออาหารวันนี้และพรุ่งนี้</h2>
        {isLoadingMeals ? (
          <p style={{ color: 'var(--color-text-muted)' }}>กำลังโหลด...</p>
        ) : hasMeals ? (
          <Grid dataSource={gridRows as MealGridRow[]} height="auto">
            <Columns>
              <Column field="label" headerText="วัน" width={100} template={LabelTemplate} />
              <Column field="breakfast" headerText="🌅 เช้า" template={BreakfastTemplate} />
              <Column field="lunch" headerText="☀️ กลางวัน" template={LunchTemplate} />
              <Column field="dinner" headerText="🌙 เย็น" template={DinnerTemplate} />
            </Columns>
          </Grid>
        ) : (
          <p
            style={{
              textAlign: 'center',
              padding: 32,
              color: 'var(--color-text-muted)',
            }}
          >
            ยังไม่มีแผนมื้ออาหาร — กดปุ่มด้านล่างเพื่อเริ่มวางแผน
          </p>
        )}
      </div>

      {/* Stats Row */}
      <div style={{ display: 'flex', gap: 12, marginBottom: 16, flexWrap: 'wrap' }}>
        <div
          className="card"
          style={{
            flex: 1,
            minWidth: 140,
            textAlign: 'center',
            padding: 20,
            background: '#FFF3E0',
          }}
        >
          <div style={{ fontSize: 28, fontWeight: 700, color: '#F57C00' }}>
            {recipeCount}
          </div>
          <div style={{ fontSize: 13, color: '#E65100', marginTop: 4 }}>
            📖 Recipes
          </div>
        </div>
        <div
          className="card"
          style={{
            flex: 1,
            minWidth: 140,
            textAlign: 'center',
            padding: 20,
            background: '#E3F2FD',
          }}
        >
          <div style={{ fontSize: 28, fontWeight: 700, color: '#1565C0' }}>
            {ingredientCount}
          </div>
          <div style={{ fontSize: 13, color: '#0D47A1', marginTop: 4 }}>
            🥬 Ingredients
          </div>
        </div>
        <div
          className="card"
          style={{
            flex: 1,
            minWidth: 140,
            textAlign: 'center',
            padding: 20,
            background: '#E8F5E9',
          }}
        >
          <div style={{ fontSize: 28, fontWeight: 700, color: '#2E7D32' }}>
            {mealsThisWeek}
          </div>
          <div style={{ fontSize: 13, color: '#1B5E20', marginTop: 4 }}>
            📅 Meals This Week
          </div>
        </div>
      </div>

      {/* Action */}
      <div style={{ textAlign: 'center' }}>
        <Button
          type="button"
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={() => navigate('/meal-plan')}
        >
          ดูแผนทั้งหมด →
        </Button>
      </div>
    </section>
  )
}
```

- [ ] **Step 2: Build to verify**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/dashboard/
git commit -m "feat(dashboard): implement Dashboard with meal grid, stock badges, and stats"
```

---

## Task 3: Verification

- [ ] **Step 1: Full build**

Run: `cd c:/Repo2/t/menunest/frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 2: Manual test**

1. Open `http://localhost:5173/`
2. Dashboard shows greeting with family name + Thai date
3. Meal grid shows today/tomorrow × 3 slots
4. Entries with recipes show name + stock badge (✅ or ⚠️)
5. Empty slots show "—"
6. Stats row shows recipe count, ingredient count, meals this week
7. "ดูแผนทั้งหมด →" navigates to `/meal-plan`
8. If no meals planned → empty state message shows

---

## Summary

| Task | Description | Files |
|------|------------|-------|
| 1 | useDashboard hook | 1 new |
| 2 | DashboardPage implementation | 1 modified |
| 3 | Verification | — |

**Total: 1 new file, 1 modified file, 3 tasks**
