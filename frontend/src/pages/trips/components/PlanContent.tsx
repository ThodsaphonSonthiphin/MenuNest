// frontend/src/pages/trips/components/PlanContent.tsx
//
// The scrolling half of the plan — the `แผนวัน` / `คลัง` segment, the Stop list with its
// Legs, `+ เพิ่มจุดแวะ` and the `มาแล้ว` drawer. This is `ItineraryTab`'s rendering (spec §7);
// its data now comes from `usePlan`, hoisted to the page, and the overlays it used to own
// (StopDetailSheet, StopEditorDialog) are rendered by the page so the **Compact stop card**
// can open them too.
//
// It is container-agnostic: the same element tree goes inside the **Plan sheet** on mobile
// and the **Plan panel** on desktop (menunest-236).
import {Fragment, useState} from 'react'
import {
  DndContext,
  closestCenter,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
  type DragStartEvent,
  type DragEndEvent,
} from '@dnd-kit/core'
import {SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy} from '@dnd-kit/sortable'
import {restrictToVerticalAxis} from '@dnd-kit/modifiers'
import {
  useAddStopMutation,
  useReorderStopsMutation,
  useSetStopVisitedMutation,
} from '../../../shared/api/api'
import type {TripPlaceDto} from '../../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../../store/index'
import {setAddMode, setPlaceEditor, setSelectedStop} from '../tripsSlice'
import {useCurrentUser} from '../../../shared/hooks/useCurrentUser'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {reorderKeepingVisited} from '../lib/reorder'
import type {Plan} from '../hooks/usePlan'
import {SegmentedTabs} from './SegmentedTabs'
import {ItineraryStopCard} from './ItineraryStopCard'
import {VisitedStopRow} from './VisitedStopRow'
import {PlaceCard} from './PlaceCard'
import {TravelLeg} from './TravelLeg'
import {CheckIcon} from './FlagIcons'
import {ChevronDownIcon} from './TripFormIcons'

type PlanView = 'day' | 'library'

/** Inline add-stop picker shown below the stop list. */
function AddStopPicker({
  tripId,
  dayId,
  places,
  defaultTravelMode,
  onClose,
  onAddNew,
}: {
  tripId: string
  dayId: string
  places: TripPlaceDto[]
  defaultTravelMode: string
  onClose: () => void
  onAddNew: () => void
}) {
  const [addStop] = useAddStopMutation()
  const [addError, setAddError] = useState<string | null>(null)

  return (
    <div className="add-stop-picker">
      <div className="add-stop-header">
        <span>เลือกจุดแวะ</span>
        <button className="btn-text" onClick={onClose}>✕</button>
      </div>

      <button type="button" className="add-stop-new" onClick={onAddNew}>
        <span className="add-stop-new-plus">+</span>
        <span className="add-stop-new-txt">
          เพิ่มสถานที่ใหม่
          <span className="add-stop-new-sub">ค้นหา / แตะหมุดบนแผนที่ / วางลิงก์</span>
        </span>
      </button>

      {places.length === 0 ? (
        <p className="trips-muted">คุณยังไม่มีสถานที่ในคลัง</p>
      ) : (
        <>
          <div className="add-stop-divider">หรือเลือกจากคลังสถานที่</div>
          <ul className="add-stop-list">
            {places.map((p) => (
              <li key={p.id}>
                <button
                  className="add-stop-item"
                  onClick={async () => {
                    try {
                      await addStop({
                        tripId,
                        dayId,
                        tripPlaceId: p.id,
                        dwellMinutes: 60,
                        travelModeToReach: (defaultTravelMode as 'Drive' | 'Walk' | 'Transit') ?? 'Drive',
                      }).unwrap()
                      onClose()
                    } catch (err) {
                      setAddError(getErrorMessage(err))
                    }
                  }}
                >
                  <span className="add-stop-name">{p.name}</span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      {addError && <p className="trips-field-error">{addError}</p>}
    </div>
  )
}

export function PlanContent({
  tripId,
  plan,
  onActivateStop,
}: {
  tripId: string
  plan: Plan
  /**
   * A Stop card was tapped. The PAGE decides what that means per surface: on a sheet it
   * selects, and `ดูทั้งหมด` on the Compact stop card opens the detail; on the panel, where
   * there is no Compact stop card, it selects AND opens the detail (spec §6.3).
   */
  onActivateStop(stopId: string): void
}) {
  const dispatch = useAppDispatch()
  const {uvWarnThreshold, feelsLikeWarnThreshold} = useCurrentUser()
  const selectedStopId = useAppSelector((s) => s.trips.selectedStopId)
  const [view, setView] = useState<PlanView>('day')
  const [pickerOpen, setPickerOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [activeDragId, setActiveDragId] = useState<string | null>(null)
  const [isReordering, setIsReordering] = useState(false)
  const [doneOpen, setDoneOpen] = useState(false)
  const [reorderMode, setReorderMode] = useState(false)

  const [reorder] = useReorderStopsMutation()
  const [setStopVisited] = useSetStopVisitedMutation()

  const sensors = useSensors(
    // A few px of movement before a drag starts, so a tap/scroll is not misread.
    useSensor(PointerSensor, {activationConstraint: {distance: 6}}),
    useSensor(KeyboardSensor, {coordinateGetter: sortableKeyboardCoordinates}),
  )

  const {dayId, remaining, done, scheduled, placesById, places, stopWeather, tripMonth, dayNav, mixedMode} = plan

  // Clear a stale action error when the Day changes (render-time reset — avoids
  // set-state-in-effect; React re-renders immediately, no extra paint).
  const [lastDayId, setLastDayId] = useState(dayId)
  if (dayId !== lastDayId) {
    setLastDayId(dayId)
    setActionError(null)
    setReorderMode(false)
  }

  if (plan.error) return <p className="trips-field-error">{getErrorMessage(plan.error)}</p>
  if (plan.isLoading || !dayId) return <p className="trips-muted">กำลังโหลดแผน…</p>

  const toggleReorder = () => {
    setReorderMode((v) => !v)
    dispatch(setSelectedStop(null)) // entering/leaving reorder drops the selection (design §2)
  }

  const handleDragStart = (e: DragStartEvent) => setActiveDragId(String(e.active.id))

  const handleDragEnd = async (e: DragEndEvent) => {
    setActiveDragId(null)
    const {active, over} = e
    if (!over) return
    const visitedIds = new Set(scheduled.filter((s) => s.stop.isVisited).map((s) => s.stop.id))
    const orderedStopIds = reorderKeepingVisited(
      scheduled.map((s) => s.stop.id),
      visitedIds,
      String(active.id),
      String(over.id),
    )
    if (!orderedStopIds) return
    setIsReordering(true)
    try {
      await reorder({tripId, dayId, orderedStopIds}).unwrap()
      await plan.refetch().unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
    } finally {
      setIsReordering(false)
    }
  }

  const allVisited = scheduled.length > 0 && remaining.length === 0
  // Lead Leg = the drive INTO the first remaining Stop, shown only when a visited Stop
  // precedes it (i.e. it is not the day's very first Stop). Skipped at zero-visited. ADR-047 §4.
  const leadLeg =
    remaining.length > 0 && scheduled.indexOf(remaining[0]) > 0 ? remaining[0].stop.legToReach : null

  return (
    <div className="plan-content">
      {/* The library is for EDITING a Place's profile now — finding one to schedule is the
          map's job, where it is a Ghost pin (menunest-235). */}
      <SegmentedTabs
        value={view}
        onChange={setView}
        options={[
          {label: 'แผนวัน', value: 'day' as PlanView},
          {label: `คลังสถานที่ · ${places.length}`, value: 'library' as PlanView},
        ]}
      />

      {view === 'library' ? (
        places.length ? (
          <div className="place-list">
            {places.map((p) => (
              <PlaceCard key={p.id} place={p} onClick={() => dispatch(setPlaceEditor(p.id))} />
            ))}
          </div>
        ) : (
          <p className="trips-empty">ยังไม่มีสถานที่ — แตะ + แล้ววางลิงก์ หรือแตะหมุดบนแผนที่</p>
        )
      ) : (
        <>
          {dayNav?.overflow && (
            <p className="nav-note">
              นำทางครอบคลุม {dayNav.coveredCount} จุดแรก — จุดที่เหลือใช้ปุ่มนำทางรายจุด
            </p>
          )}
          {dayNav && mixedMode && (
            <p className="nav-note">
              วันนี้มีหลายโหมดเดินทาง — เส้นทางทั้งวันใช้โหมดเดียว ใช้ปุ่มรายจุดเพื่อโหมดที่ถูก
            </p>
          )}

          {actionError && <p className="trips-field-error">{actionError}</p>}

          {/* Toolbar appears with >=2 stops (reordering needs two) — and stays visible while
              reorder mode is on even if stops drop below 2, so the way back out is always
              reachable (design §2). Gating the whole bar avoids a lone count with no action. */}
          {(remaining.length >= 2 || reorderMode) && (
            <div className="stop-toolbar">
              <span className="stop-count">จุดแวะ · {remaining.length} จุด</span>
              <button
                type="button"
                className={`reorder-toggle${reorderMode ? ' on' : ''}`}
                aria-pressed={reorderMode}
                onClick={toggleReorder}
              >
                {reorderMode ? 'เสร็จ' : 'จัดลำดับ'}
              </button>
            </div>
          )}
          {reorderMode && (
            <p className="reorder-hint">โหมดจัดลำดับ — ลากที่จับด้านขวาเพื่อย้ายจุดแวะ</p>
          )}

          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            modifiers={[restrictToVerticalAxis]}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
            accessibility={{
              announcements: {
                onDragStart: () => 'เริ่มลากจุดแวะ ใช้ลูกศรขึ้น–ลงเพื่อย้าย',
                onDragOver: () => 'กำลังย้ายจุดแวะ',
                onDragEnd: () => 'วางจุดแวะแล้ว กำลังคำนวณเวลาใหม่',
                onDragCancel: () => 'ยกเลิกการย้ายจุดแวะ',
              },
              screenReaderInstructions: {
                draggable: 'กดเว้นวรรคเพื่อยกจุดแวะ ใช้ลูกศรขึ้น–ลงเพื่อย้าย แล้วกดเว้นวรรคอีกครั้งเพื่อวาง หรือ Escape เพื่อยกเลิก',
              },
            }}
          >
            <SortableContext items={remaining.map((s) => s.stop.id)} strategy={verticalListSortingStrategy}>
              <div className={`stop-list${activeDragId ? ' dragging' : ''}`}>
                {leadLeg && (
                  <TravelLeg leg={leadLeg} mode={remaining[0].stop.travelModeToReach} note="จากจุดที่เพิ่งไป" />
                )}
                {remaining.map((s, i) => {
                  const place = placesById[s.stop.tripPlaceId]
                  return (
                    <Fragment key={s.stop.id}>
                      {i > 0 && s.stop.legToReach && (
                        <TravelLeg leg={s.stop.legToReach} mode={s.stop.travelModeToReach} />
                      )}
                      {place && (
                        <ItineraryStopCard
                          id={s.stop.id}
                          place={place}
                          arrival={s.arrival}
                          dwell={s.stop.dwellMinutes}
                          flag={s.flag}
                          arrivalReading={stopWeather[s.stop.id]?.arrival}
                          tripMonth={tripMonth}
                          reorderMode={reorderMode}
                          selected={s.stop.id === selectedStopId}
                          // Tapping a card is the SAME selection as tapping its pin
                          // (menunest-238): the map enlarges and pans to it either way.
                          onOpenDetail={() => onActivateStop(s.stop.id)}
                          uvWarn={uvWarnThreshold}
                          feelsWarn={feelsLikeWarnThreshold}
                          order={i + 1}
                        />
                      )}
                    </Fragment>
                  )
                })}
                {scheduled.length === 0 && (
                  <p className="trips-empty">ยังไม่มีจุดแวะ — แตะหมุดจางบนแผนที่ หรือปุ่ม + เพื่อเพิ่ม</p>
                )}
                {allVisited && (
                  <p className="trips-empty"><CheckIcon /> เที่ยวครบทุกจุดแล้ว</p>
                )}
              </div>
            </SortableContext>
          </DndContext>

          {pickerOpen ? (
            <AddStopPicker
              tripId={tripId}
              dayId={dayId}
              places={places}
              defaultTravelMode={plan.dayMode}
              onClose={() => setPickerOpen(false)}
              onAddNew={() => {
                // menunest-240: arms the map ALREADY on screen. There is no second map to open.
                dispatch(setAddMode(true))
                setPickerOpen(false)
              }}
            />
          ) : (
            <button className="btn-add-stop" onClick={() => setPickerOpen(true)}>
              + เพิ่มจุดแวะ
            </button>
          )}

          {done.length > 0 && (
            <div className="done-drawer">
              <button
                type="button"
                className="done-toggle"
                aria-expanded={doneOpen}
                onClick={() => setDoneOpen((v) => !v)}
              >
                <ChevronDownIcon className="chev" />
                <span className="badge"><CheckIcon /> มาแล้ว {done.length}</span>
              </button>
              {doneOpen && (
                <div className="done-body">
                  {done.map((s) => {
                    const place = placesById[s.stop.tripPlaceId]
                    return place ? (
                      <VisitedStopRow
                        key={s.stop.id}
                        place={place}
                        arrival={s.arrival}
                        onUnvisit={async () => {
                          try {
                            await setStopVisited({tripId, stopId: s.stop.id, isVisited: false}).unwrap()
                          } catch (err) {
                            setActionError(getErrorMessage(err))
                          }
                        }}
                      />
                    ) : null
                  })}
                </div>
              )}
            </div>
          )}
        </>
      )}

      {isReordering && (
        <div className="itin-reorder-overlay" role="status" aria-live="polite">
          <span className="itin-reorder-spinner" aria-hidden="true" />
          <span>กำลังจัดลำดับใหม่…</span>
        </div>
      )}
    </div>
  )
}
