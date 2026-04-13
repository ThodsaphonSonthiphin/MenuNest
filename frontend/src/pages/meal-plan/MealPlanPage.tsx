import { useMemo, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import {
  Scheduler,
  DayView,
  WeekView,
} from '@syncfusion/react-scheduler'
import type {
  SchedulerCellClickEvent,
  SchedulerDateChangeEvent,
  SchedulerEventClickEvent,
} from '@syncfusion/react-scheduler'
import { Dialog } from '@syncfusion/react-popups'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'

import {
  useCreateMealPlanEntryMutation,
  useDeleteMealPlanEntryMutation,
  useGetStockCheckQuery,
  useListMealPlanQuery,
  useListRecipesQuery,
} from '../../shared/api/api'
import type { MealPlanEntryDto, MealSlot } from '../../shared/api/api'
import { useBreakpoint } from '../../shared/hooks/useBreakpoint'
import { useAppDispatch, useAppSelector } from '../../store'
import {
  closeRecipePicker,
  openRecipePicker,
  selectEntry,
  setViewStartDate,
} from './mealPlanSlice'

// ----------------------------------------------------------------------
// Slot ↔ time-of-day mapping
// ----------------------------------------------------------------------

const SLOT_LABELS: Record<MealSlot, string> = {
  Breakfast: '🌅 Breakfast',
  Lunch: '🌞 Lunch',
  Dinner: '🌙 Dinner',
}

/** Visual time bands so the Scheduler renders three distinct rows per day. */
const SLOT_HOURS: Record<MealSlot, { start: number; end: number }> = {
  Breakfast: { start: 7, end: 9 },
  Lunch: { start: 12, end: 14 },
  Dinner: { start: 18, end: 20 },
}

function slotFromHour(hour: number): MealSlot | null {
  if (hour < 11) return 'Breakfast'
  if (hour < 16) return 'Lunch'
  if (hour < 22) return 'Dinner'
  return null
}

// ----------------------------------------------------------------------
// Date helpers (ISO yyyy-mm-dd is the canonical wire format)
// ----------------------------------------------------------------------

function formatIso(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function addDays(iso: string, days: number): string {
  const d = new Date(iso)
  d.setDate(d.getDate() + days)
  return formatIso(d)
}

function mondayOf(date: Date): Date {
  const d = new Date(date)
  const dow = d.getDay() // 0 = Sunday
  d.setDate(d.getDate() - ((dow + 6) % 7))
  d.setHours(0, 0, 0, 0)
  return d
}

function dayLabel(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleDateString('th-TH', { weekday: 'short', day: '2-digit', month: 'short' })
}

// ----------------------------------------------------------------------
// MealPlanEntryDto → Scheduler EventModel
// ----------------------------------------------------------------------

interface SchedulerEvent extends Record<string, unknown> {
  Id: string
  Subject: string
  StartTime: Date
  EndTime: Date
  IsReadonly: boolean
  CategoryColor: string
  MealSlot: MealSlot
  Status: MealPlanEntryDto['status']
}

function entryToEvent(entry: MealPlanEntryDto): SchedulerEvent {
  const { start, end } = SLOT_HOURS[entry.mealSlot]
  const startTime = new Date(entry.date + 'T00:00:00')
  startTime.setHours(start, 0, 0, 0)
  const endTime = new Date(entry.date + 'T00:00:00')
  endTime.setHours(end, 0, 0, 0)
  return {
    Id: entry.id,
    Subject: entry.recipeName,
    StartTime: startTime,
    EndTime: endTime,
    IsReadonly: entry.status === 'Cooked',
    CategoryColor: entry.status === 'Cooked' ? '#9e9e9e' : '#f57c00',
    MealSlot: entry.mealSlot,
    Status: entry.status,
  }
}

// ----------------------------------------------------------------------
// MealPlanPage
// ----------------------------------------------------------------------

export function MealPlanPage() {
  const dispatch = useAppDispatch()
  const viewStartDate = useAppSelector((s) => s.mealPlan.viewStartDate)
  const focusedEntryId = useAppSelector((s) => s.mealPlan.focusedEntryId)
  const recipePickerOpen = useAppSelector((s) => s.mealPlan.recipePickerOpen)

  const breakpoint = useBreakpoint()
  // Phones can't fit a 7-column week view comfortably — use the
  // single-day view there; tablets and up keep the week.
  const isMobile = breakpoint === 'mobile'

  const fromIso = viewStartDate
  const toIso = addDays(viewStartDate, 6)

  const { data: entries } = useListMealPlanQuery({ from: fromIso, to: toIso })
  const events = useMemo(() => (entries ?? []).map(entryToEvent), [entries])
  const focusedEntry = entries?.find((e) => e.id === focusedEntryId) ?? null

  const [pickerSlot, setPickerSlot] = useState<{ date: string; slot: MealSlot } | null>(null)

  const handleCellClick = (args: SchedulerCellClickEvent) => {
    // Always cancel default — Syncfusion would otherwise pop the
    // built-in event editor that we've replaced with our own dialogs.
    args.cancel = true

    const slot = slotFromHour(args.startTime.getHours())
    if (!slot) return

    const date = formatIso(args.startTime)
    const existing = entries?.find((e) => e.date === date && e.mealSlot === slot)
    if (existing) {
      dispatch(selectEntry(existing.id))
    } else {
      setPickerSlot({ date, slot })
      dispatch(openRecipePicker())
    }
  }

  const handleEventClick = (args: SchedulerEventClickEvent) => {
    args.cancel = true
    const id = (args.data?.Id ?? args.data?.id) as string | undefined
    if (id) dispatch(selectEntry(id))
  }

  const handleSelectedDateChange = (args: SchedulerDateChangeEvent) => {
    // Sync the redux week-anchor whenever the user navigates the
    // built-in Prev/Next/Today toolbar — that drives the API fetch.
    dispatch(setViewStartDate(formatIso(mondayOf(args.value))))
  }

  const closePicker = () => {
    setPickerSlot(null)
    dispatch(closeRecipePicker())
  }
  const closeDetail = () => dispatch(selectEntry(null))

  return (
    <section className="page page--meal-plan">
      <header className="page__header">
        <h1>Meal Plan</h1>
      </header>

      <Scheduler
        height={isMobile ? '70vh' : '650px'}
        selectedDate={new Date(viewStartDate + 'T00:00:00')}
        view={isMobile ? 'Day' : 'Week'}
        eventSettings={{ dataSource: events }}
        showQuickInfoPopup={false}
        eventDrag={false}
        eventResize={false}
        onCellClick={handleCellClick}
        onEventClick={handleEventClick}
        onSelectedDateChange={handleSelectedDateChange}
      >
        <DayView />
        <WeekView />
      </Scheduler>

      <Dialog
        open={recipePickerOpen && !!pickerSlot}
        onClose={closePicker}
        modal
        header={
          pickerSlot
            ? `เลือก recipe — ${dayLabel(pickerSlot.date)} · ${SLOT_LABELS[pickerSlot.slot]}`
            : ''
        }
        style={{ width: '560px' }}
      >
        {pickerSlot && (
          <RecipePickerForm date={pickerSlot.date} slot={pickerSlot.slot} onDone={closePicker} />
        )}
      </Dialog>

      <Dialog
        open={!!focusedEntry}
        onClose={closeDetail}
        modal
        header={focusedEntry?.recipeName ?? ''}
        style={{ width: '560px' }}
      >
        {focusedEntry && <EntryDetailContent entry={focusedEntry} onClose={closeDetail} />}
      </Dialog>
    </section>
  )
}

// ----------------------------------------------------------------------
// Recipe picker (form inside Dialog)
// ----------------------------------------------------------------------

interface RecipePickerFormProps {
  date: string
  slot: MealSlot
  onDone: () => void
}

interface PickerFormValues {
  search: string
}

function RecipePickerForm({ date, slot, onDone }: RecipePickerFormProps) {
  const { data: recipes, isLoading } = useListRecipesQuery()
  const [createEntry, { isLoading: isCreating }] = useCreateMealPlanEntryMutation()
  const { control, watch } = useForm<PickerFormValues>({ defaultValues: { search: '' } })
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const search = watch('search').trim().toLowerCase()
  const filtered = (recipes ?? []).filter((r) => r.name.toLowerCase().includes(search))

  const pick = async (recipeId: string) => {
    setErrorMessage(null)
    try {
      await createEntry({ date, mealSlot: slot, recipeId, notes: null }).unwrap()
      onDone()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <div>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}
      <div style={{ marginBottom: 12 }}>
        <Controller
          control={control}
          name="search"
          render={({ field }) => (
            <TextBox
              placeholder="🔍 ค้นหา recipe..."
              autoFocus
              value={field.value}
              onChange={(e) => field.onChange(e.value ?? '')}
            />
          )}
        />
      </div>
      {isLoading && <p>Loading…</p>}
      {recipes && recipes.length === 0 && (
        <p style={{ color: 'var(--color-text-muted)' }}>
          ยังไม่มี recipe — ไปสร้างที่หน้า Recipes ก่อน
        </p>
      )}
      <ul className="recipe-pick-list">
        {filtered.map((recipe) => (
          <li key={recipe.id}>
            <Button
              type="button"
              variant={Variant.Outlined}
              color={Color.Secondary}
              onClick={() => pick(recipe.id)}
              disabled={isCreating}
              style={{
                width: '100%',
                justifyContent: 'space-between',
                textAlign: 'left',
              }}
            >
              <span style={{ flex: 1, fontWeight: 600 }}>{recipe.name}</span>
              <span style={{ color: 'var(--color-text-muted)', fontSize: 12 }}>
                {recipe.ingredientCount} ingredients
              </span>
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}

// ----------------------------------------------------------------------
// Entry detail (stock check) inside Dialog
// ----------------------------------------------------------------------

interface EntryDetailProps {
  entry: MealPlanEntryDto
  onClose: () => void
}

function EntryDetailContent({ entry, onClose }: EntryDetailProps) {
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
    <div>
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
          <div className="table-scroll" style={{ marginBottom: 12 }}>
          <table className="data-table">
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
          </div>
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
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Error}
          onClick={handleDelete}
          disabled={isDeleting}
        >
          🗑️ Remove from plan
        </Button>
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Secondary}
          onClick={onClose}
        >
          Close
        </Button>
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
