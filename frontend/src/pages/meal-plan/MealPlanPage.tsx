import { useListMealPlanQuery } from '../../shared/api/api'
import { useAppSelector } from '../../store'

export function MealPlanPage() {
  const viewStartDate = useAppSelector((s) => s.mealPlan.viewStartDate)
  const to = new Date(viewStartDate)
  to.setDate(to.getDate() + 6)
  const toIso = to.toISOString().slice(0, 10)

  const { data, isLoading, error } = useListMealPlanQuery({ from: viewStartDate, to: toIso })

  return (
    <section className="page page--meal-plan">
      <header className="page__header">
        <h1>Meal Plan</h1>
        <p>
          Week of <strong>{viewStartDate}</strong>
        </p>
      </header>

      {isLoading && <p>Loading…</p>}
      {error && <p>Failed to load meal plan.</p>}
      {data && <p>{data.length} entries (grid view coming soon).</p>}
    </section>
  )
}
