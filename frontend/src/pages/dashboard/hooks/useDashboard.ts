import { useMemo } from 'react'
import {
  useListMealPlanQuery,
  useStockCheckBatchQuery,
  useListRecipesQuery,
  useListIngredientsQuery,
} from '../../../shared/api/api'
import type { MealPlanEntryDto } from '../../../shared/api/api'

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

  const { data: meals, isLoading: isLoadingMeals } = useListMealPlanQuery({
    from: todayISO,
    to: tomorrowISO,
  })

  const { data: weekMeals } = useListMealPlanQuery({
    from: toISODate(monday),
    to: toISODate(sunday),
  })

  const entryIds = useMemo(() => meals?.map((m) => m.id) ?? [], [meals])
  const { data: stockChecks } = useStockCheckBatchQuery(
    { entryIds },
    { skip: entryIds.length === 0 },
  )

  const { data: recipes } = useListRecipesQuery()
  const { data: ingredients } = useListIngredientsQuery()

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

  // Build stock check map keyed by mealPlanEntryId.
  // The StockCheckBatchDto is a flat aggregate (lines + isSufficient + missingCount)
  // without per-entry breakdowns.  If the backend ever returns a `checks` array
  // of StockCheckDto[], this will populate per-entry badges; until then the map
  // stays empty and the UI gracefully omits stock indicators.
  const stockCheckMap = useMemo<Record<string, { isSufficient: boolean; missingCount: number }>>(() => {
    if (!stockChecks || !meals) return {}
    const map: Record<string, { isSufficient: boolean; missingCount: number }> = {}
    const checks = (stockChecks as Record<string, unknown>)?.checks
    if (Array.isArray(checks)) {
      for (const check of checks as Array<{ mealPlanEntryId: string; isSufficient: boolean; missingCount: number }>) {
        map[check.mealPlanEntryId] = {
          isSufficient: check.isSufficient,
          missingCount: check.missingCount,
        }
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
