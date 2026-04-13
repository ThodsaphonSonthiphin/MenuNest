import { useMemo, useState } from 'react'
import {
  useCreateMealPlanEntryMutation,
  useDeleteMealPlanEntryMutation,
  useGetStockCheckQuery,
  useListMealPlanQuery,
  useListRecipesQuery,
  MEAL_SLOTS,
} from '../../shared/api/api'
import type { MealPlanEntryDto, MealSlot } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store'
import {
  closeRecipePicker,
  openRecipePicker,
  selectEntry,
  setViewStartDate,
} from './mealPlanSlice'

const SLOT_LABELS: Record<MealSlot, string> = {
  Breakfast: '🌅 Breakfast',
  Lunch: '🌞 Lunch',
  Dinner: '🌙 Dinner',
}

function formatIso(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function addDays(iso: string, days: number): string {
  const d = new Date(iso)
  d.setDate(d.getDate() + days)
  return formatIso(d)
}

function dayLabel(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleDateString('th-TH', { weekday: 'short', day: '2-digit', month: 'short' })
}

export function MealPlanPage() {
  const dispatch = useAppDispatch()
  const viewStartDate = useAppSelector((s) => s.mealPlan.viewStartDate)
  const focusedEntryId = useAppSelector((s) => s.mealPlan.focusedEntryId)
  const recipePickerOpen = useAppSelector((s) => s.mealPlan.recipePickerOpen)

  const days = useMemo(() => {
    return Array.from({ length: 7 }, (_, i) => addDays(viewStartDate, i))
  }, [viewStartDate])

  const fromIso = days[0]
  const toIso = days[days.length - 1]

  const { data: entries, isLoading } = useListMealPlanQuery({ from: fromIso, to: toIso })
  const [pickerSlot, setPickerSlot] = useState<{ date: string; slot: MealSlot } | null>(null)

  const grid = useMemo(() => {
    const map = new Map<string, MealPlanEntryDto>()
    for (const e of entries ?? []) {
      map.set(`${e.date}|${e.mealSlot}`, e)
    }
    return map
  }, [entries])

  const focusedEntry = entries?.find((e) => e.id === focusedEntryId) ?? null

  const handleSlotClick = (date: string, slot: MealSlot) => {
    const existing = grid.get(`${date}|${slot}`)
    if (existing) {
      dispatch(selectEntry(existing.id))
    } else {
      setPickerSlot({ date, slot })
      dispatch(openRecipePicker())
    }
  }

  const handlePickerClose = () => {
    setPickerSlot(null)
    dispatch(closeRecipePicker())
  }

  const goPrevWeek = () => dispatch(setViewStartDate(addDays(viewStartDate, -7)))
  const goNextWeek = () => dispatch(setViewStartDate(addDays(viewStartDate, 7)))
  const goToday = () => {
    const today = new Date()
    const dow = today.getDay()
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((dow + 6) % 7))
    dispatch(setViewStartDate(formatIso(monday)))
  }

  return (
    <section className="page page--meal-plan">
      <header className="page__header">
        <h1>Meal Plan</h1>
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="button" className="btn btn--outline btn--sm" onClick={goPrevWeek}>
            ◀ Prev
          </button>
          <button type="button" className="btn btn--outline btn--sm" onClick={goToday}>
            Today
          </button>
          <button type="button" className="btn btn--outline btn--sm" onClick={goNextWeek}>
            Next ▶
          </button>
        </div>
      </header>

      <p style={{ color: 'var(--color-text-muted)', fontSize: 13, marginBottom: 12 }}>
        Week of <strong>{dayLabel(fromIso)}</strong> – <strong>{dayLabel(toIso)}</strong>
      </p>

      {isLoading && <p>Loading…</p>}

      <table className="meal-plan-grid">
        <thead>
          <tr>
            <th></th>
            {MEAL_SLOTS.map((slot) => (
              <th key={slot}>{SLOT_LABELS[slot]}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {days.map((date) => (
            <tr key={date}>
              <th scope="row">{dayLabel(date)}</th>
              {MEAL_SLOTS.map((slot) => {
                const entry = grid.get(`${date}|${slot}`)
                return (
                  <td
                    key={slot}
                    className={`meal-cell ${entry ? 'meal-cell--filled' : ''} ${
                      entry?.status === 'Cooked' ? 'meal-cell--cooked' : ''
                    }`}
                    onClick={() => handleSlotClick(date, slot)}
                  >
                    {entry ? (
                      <>
                        <div className="meal-cell__name">{entry.recipeName}</div>
                        {entry.status === 'Cooked' && (
                          <div className="meal-cell__badge">✓ cooked</div>
                        )}
                      </>
                    ) : (
                      <div className="meal-cell__empty">+</div>
                    )}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>

      {recipePickerOpen && pickerSlot && (
        <RecipePickerDialog
          date={pickerSlot.date}
          slot={pickerSlot.slot}
          onClose={handlePickerClose}
        />
      )}

      {focusedEntry && (
        <EntryDetailDialog
          entry={focusedEntry}
          onClose={() => dispatch(selectEntry(null))}
        />
      )}
    </section>
  )
}

// ----------------------------------------------------------------------

interface RecipePickerProps {
  date: string
  slot: MealSlot
  onClose: () => void
}

function RecipePickerDialog({ date, slot, onClose }: RecipePickerProps) {
  const { data: recipes, isLoading } = useListRecipesQuery()
  const [createEntry, { isLoading: isCreating }] = useCreateMealPlanEntryMutation()
  const [search, setSearch] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const filtered = (recipes ?? []).filter((r) =>
    r.name.toLowerCase().includes(search.trim().toLowerCase()),
  )

  const pick = async (recipeId: string) => {
    setErrorMessage(null)
    try {
      await createEntry({ date, mealSlot: slot, recipeId, notes: null }).unwrap()
      onClose()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <DialogShell onClose={onClose} title={`เลือก recipe — ${dayLabel(date)} · ${SLOT_LABELS[slot]}`}>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}
      <input
        type="search"
        placeholder="🔍 ค้นหา recipe..."
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        autoFocus
        style={{
          width: '100%',
          padding: 10,
          marginBottom: 12,
          border: '1px solid var(--color-border)',
          borderRadius: 6,
          font: 'inherit',
        }}
      />
      {isLoading && <p>Loading…</p>}
      {recipes && recipes.length === 0 && (
        <p style={{ color: 'var(--color-text-muted)' }}>
          ยังไม่มี recipe — ไปสร้างที่หน้า Recipes ก่อน
        </p>
      )}
      <ul className="recipe-pick-list">
        {filtered.map((recipe) => (
          <li key={recipe.id}>
            <button
              type="button"
              className="recipe-pick-item"
              onClick={() => pick(recipe.id)}
              disabled={isCreating}
            >
              <strong>{recipe.name}</strong>
              <span>{recipe.ingredientCount} ingredients</span>
            </button>
          </li>
        ))}
      </ul>
    </DialogShell>
  )
}

// ----------------------------------------------------------------------

interface EntryDetailProps {
  entry: MealPlanEntryDto
  onClose: () => void
}

function EntryDetailDialog({ entry, onClose }: EntryDetailProps) {
  const { data: stockCheck, isLoading } = useGetStockCheckQuery(entry.id)
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteMealPlanEntryMutation()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleDelete = async () => {
    if (!confirm(`ลบ "${entry.recipeName}" ออกจาก meal plan?`)) return
    setErrorMessage(null)
    try {
      await deleteEntry(entry.id).unwrap()
      onClose()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <DialogShell onClose={onClose} title={entry.recipeName}>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}
      <p style={{ color: 'var(--color-text-muted)', marginBottom: 12 }}>
        {dayLabel(entry.date)} · {SLOT_LABELS[entry.mealSlot]}
        {entry.status === 'Cooked' && entry.cookedAt && (
          <> · ✓ cooked {new Date(entry.cookedAt).toLocaleString('th-TH')}</>
        )}
      </p>

      {entry.cookNotes && (
        <p
          style={{
            background: '#fff8e1',
            padding: 8,
            borderRadius: 6,
            fontSize: 13,
            marginBottom: 12,
          }}
        >
          📝 {entry.cookNotes}
        </p>
      )}

      <h3 style={{ marginTop: 0 }}>Stock check</h3>
      {isLoading && <p>Checking stock…</p>}
      {stockCheck && stockCheck.lines.length === 0 && (
        <p style={{ color: 'var(--color-text-muted)' }}>Recipe นี้ไม่มี ingredient list</p>
      )}
      {stockCheck && stockCheck.lines.length > 0 && (
        <>
          <table className="data-table" style={{ marginBottom: 12 }}>
            <thead>
              <tr>
                <th>Ingredient</th>
                <th style={{ width: 90 }}>Required</th>
                <th style={{ width: 90 }}>On hand</th>
                <th style={{ width: 90 }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {stockCheck.lines.map((line) => (
                <tr
                  key={line.ingredientId}
                  className={line.missing > 0 ? 'row--empty' : undefined}
                >
                  <td>{line.ingredientName}</td>
                  <td>
                    {line.required} {line.unit}
                  </td>
                  <td>
                    {line.available} {line.unit}
                  </td>
                  <td>
                    {line.missing === 0 ? (
                      <span style={{ color: 'green' }}>✅ enough</span>
                    ) : (
                      <span style={{ color: 'var(--color-danger)' }}>
                        ❌ short {line.missing} {line.unit}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <p
            style={{
              fontWeight: 600,
              color: stockCheck.isSufficient ? 'green' : 'var(--color-danger)',
            }}
          >
            {stockCheck.isSufficient
              ? '✅ Stock พอสำหรับ recipe นี้'
              : `⚠️ ขาด ${stockCheck.missingCount} อย่าง`}
          </p>
        </>
      )}

      <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
        <button
          type="button"
          className="btn btn--outline"
          onClick={handleDelete}
          disabled={isDeleting}
        >
          🗑️ Remove from plan
        </button>
        <button type="button" className="btn btn--outline" onClick={onClose}>
          Close
        </button>
      </div>
    </DialogShell>
  )
}

// ----------------------------------------------------------------------

interface DialogShellProps {
  title: string
  onClose: () => void
  children: React.ReactNode
}

function DialogShell({ title, onClose, children }: DialogShellProps) {
  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div className="dialog" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="dialog__header">
          <h2>{title}</h2>
          <button
            type="button"
            className="dialog__close"
            onClick={onClose}
            aria-label="Close"
          >
            ✕
          </button>
        </div>
        <div className="dialog__body">{children}</div>
      </div>
    </div>
  )
}

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string; errors?: Record<string, string[]> } }).data
    if (data?.errors) {
      const first = Object.values(data.errors)[0]?.[0]
      if (first) return first
    }
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'Something went wrong. Please try again.'
}
