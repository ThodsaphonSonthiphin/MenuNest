import { useEffect, useMemo, useState } from 'react'
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
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'

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
import { useBreakpoint } from '../../shared/hooks/useBreakpoint'
import { useConfirm } from '../../shared/hooks/useConfirm'
import { useAppDispatch, useAppSelector } from '../../store'
import {
  closeRecipePicker,
  openRecipePicker,
  selectSlot,
  setViewStartDate,
} from './mealPlanSlice'
import type { FocusedSlot } from './mealPlanSlice'

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
  // Use LOCAL Y/M/D — `toISOString()` converts to UTC, which shifts
  // the date by a day when the user's zone is east of Greenwich
  // (e.g. clicking Wed 07:00 in TH-UTC+7 would serialize as Tue).
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
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
  const focusedSlot = useAppSelector((s) => s.mealPlan.focusedSlot)
  const recipePickerOpen = useAppSelector((s) => s.mealPlan.recipePickerOpen)

  const breakpoint = useBreakpoint()
  // Phones can't fit a 7-column week view comfortably — use the
  // single-day view there; tablets and up keep the week.
  const isMobile = breakpoint === 'mobile'

  const fromIso = viewStartDate
  const toIso = addDays(viewStartDate, 6)

  const { data: entries } = useListMealPlanQuery({ from: fromIso, to: toIso })
  const events = useMemo(() => (entries ?? []).map(entryToEvent), [entries])

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

  // Auto-clear the focused-slot pointer when its entries have all been
  // deleted — without this the dialog appears closed but Redux still
  // holds a stale (date, slot) reference. Subsequent user actions stay
  // correct, but the state is misleading and complicates debugging.
  useEffect(() => {
    if (focusedSlot && focusedEntries.length === 0) {
      dispatch(selectSlot(null))
    }
  }, [focusedSlot, focusedEntries.length, dispatch])

  const [pickerSlot, setPickerSlot] = useState<{ date: string; slot: MealSlot } | null>(null)

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

  const handleSelectedDateChange = (args: SchedulerDateChangeEvent) => {
    // Sync the redux week-anchor whenever the user navigates the
    // built-in Prev/Next/Today toolbar — that drives the API fetch.
    dispatch(setViewStartDate(formatIso(mondayOf(args.value))))
  }

  const closePicker = () => {
    setPickerSlot(null)
    dispatch(closeRecipePicker())
  }
  const closeDetail = () => dispatch(selectSlot(null))

  return (
    <section className="page page--meal-plan">
      <header className="page__header">
        <h1>Meal Plan</h1>
      </header>

      <Scheduler
        height={isMobile ? '70vh' : '650px'}
        selectedDate={new Date(viewStartDate + 'T00:00:00')}
        view={isMobile ? 'Day' : 'Week'}
        // The redux week-anchor is computed via mondayOf(), so the
        // grid must also start on Monday — otherwise Sunday cells
        // render in the visible week but fall outside the fetched
        // range, producing "save succeeded but row never appears".
        firstDayOfWeek={1}
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
            entries={focusedEntries}
            onAddRecipe={() => {
              setPickerSlot(focusedSlot)
              dispatch(openRecipePicker())
            }}
            onClose={closeDetail}
          />
        )}
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
// Meal slot detail (multi-recipe table with batch cook)
// ----------------------------------------------------------------------

interface MealSlotDetailProps {
  entries: MealPlanEntryDto[]
  onAddRecipe: () => void
  onClose: () => void
}

function MealSlotDetailContent({ entries, onAddRecipe, onClose }: MealSlotDetailProps) {
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
 *
 * TODO: consolidate into the batch endpoint if slot sizes regularly exceed 4.
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
