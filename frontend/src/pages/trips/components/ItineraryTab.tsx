// frontend/src/pages/trips/components/ItineraryTab.tsx
import {Fragment, useMemo, useState} from 'react'
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
import {reorderKeepingVisited} from '../lib/reorder'
import {useCurrentUser} from '../../../shared/hooks/useCurrentUser'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {
  useGetItineraryQuery,
  useListTripPlacesQuery,
  useListTripsQuery,
  useReorderStopsMutation,
  useAddStopMutation,
  useSetStopVisitedMutation,
} from '../../../shared/api/api'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../../store/index'
import {setActiveDay, setStopEditor, setItineraryMapCollapsed, startAddStopCapture} from '../tripsSlice'
import {useSchedule} from '../hooks/useSchedule'
import {useStopWeather} from '../hooks/useStopWeather'
import {SegmentedTabs} from './SegmentedTabs'
import {ItineraryStopCard} from './ItineraryStopCard'
import {VisitedStopRow} from './VisitedStopRow'
import {CheckIcon} from './FlagIcons'
import {TravelLeg} from './TravelLeg'
import {StopEditorDialog} from './StopEditorDialog'
import {StopDetailSheet} from './StopDetailSheet'
import {DayStartEditor} from './DayStartEditor'
import {NavIcon} from './NavIcon'
import {TripMap} from './TripMap'
import {ChevronUpIcon, ChevronDownIcon, MapRouteIcon} from './TripFormIcons'
import type {DayRoute} from '../hooks/useDayRoute'
import {buildDayNavUrl, buildStopNavUrl, getWaypointCap} from '../lib/navUrl'
import {monthOfDate} from '../lib/season'
import {appInsights} from '../../../shared/telemetry/appInsights'
import {formatDurationMinutes, getViewerTimeZone} from '../utils/time'

// Frame padding for the 188px itinerary map band. Small and top-weighted (route pins
// hang above their coordinate: callout + numbered dot), so the route fills the short band
// instead of over-zooming-out like the desktop full-height map's default (64). Module-level
// so the reference is stable across renders — FitBounds re-runs its effect when it changes.
const BAND_FIT_PADDING: google.maps.Padding = {top: 48, right: 20, bottom: 16, left: 20}

/** Inline add-stop picker shown below the stop list. */
function AddStopPicker({
  tripId,
  dayId,
  places,
  existingTripPlaceIds,
  defaultTravelMode,
  onClose,
  onAddNew,
}: {
  tripId: string
  dayId: string
  places: TripPlaceDto[]
  existingTripPlaceIds: Set<string>
  defaultTravelMode: string
  onClose: () => void
  onAddNew: () => void
}) {
  const [addStop] = useAddStopMutation()
  const [addError, setAddError] = useState<string | null>(null)

  const available = places.filter((p) => !existingTripPlaceIds.has(p.id))

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

      {available.length === 0 ? (
        <p className="trips-muted">สถานที่ในคลังทั้งหมดอยู่ในแผนแล้ว</p>
      ) : (
        <>
          <div className="add-stop-divider">หรือเลือกจากคลังสถานที่</div>
          <ul className="add-stop-list">
            {available.map((p) => (
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

export function ItineraryTab({tripId, dayRoute}: {tripId: string; dayRoute?: DayRoute}) {
  const dispatch = useAppDispatch()
  const {uvWarnThreshold, feelsLikeWarnThreshold} = useCurrentUser()
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  const editorStopId = useAppSelector((s) => s.trips.stopEditorStopId)
  const mapCollapsed = useAppSelector((s) => s.trips.itineraryMapCollapsed)
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [activeDragId, setActiveDragId] = useState<string | null>(null)
  const [isReordering, setIsReordering] = useState(false)
  const [doneOpen, setDoneOpen] = useState(false)
  const [reorderMode, setReorderMode] = useState(false)
  const [detailStopId, setDetailStopId] = useState<string | null>(null)
  const toggleReorder = () => {
    setReorderMode((v) => !v)
    setDetailStopId(null) // entering/leaving reorder closes any open detail (design §2)
  }

  const {data: days, isLoading: itineraryLoading, error: itineraryError, refetch: refetchItinerary} = useGetItineraryQuery({tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng})
  const {data: places} = useListTripPlacesQuery(tripId)
  const {data: trips} = useListTripsQuery()
  const [reorder] = useReorderStopsMutation()
  const [setStopVisited] = useSetStopVisitedMutation()

  const sensors = useSensors(
    // A few px of movement before a drag starts, so a tap/scroll is not misread.
    useSensor(PointerSensor, {activationConstraint: {distance: 6}}),
    useSensor(KeyboardSensor, {coordinateGetter: sortableKeyboardCoordinates}),
  )

  // Derive stable values used by useSchedule — must run before any early return.
  const dayList = days ?? []
  const dayId =
    activeDayId && dayList.some((d) => d.id === activeDayId) ? activeDayId : dayList[0]?.id
  const day = dayList.find((d) => d.id === dayId)
  const placesById = Object.fromEntries((places ?? []).map((p) => [p.id, p]))

  // EMPTY_DAY is used as a fallback so useSchedule is ALWAYS called unconditionally
  // (Rules of Hooks: hook count must be identical on every render).
  const EMPTY_DAY: ItineraryDayDto = {id: '', date: '', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops: []}
  const {scheduled, dayEnd, totalTravelSeconds, remainingTravelSeconds} = useSchedule(day ?? EMPTY_DAY, placesById)
  const stopWeather = useStopWeather(day ?? EMPTY_DAY, scheduled, placesById)

  // Clear any stale start-time error when the active day changes (render-time
  // reset — avoids set-state-in-effect). React re-renders immediately, no extra paint.
  const [lastDayId, setLastDayId] = useState(dayId)
  if (dayId !== lastDayId) {
    setLastDayId(dayId)
    setActionError(null)
  }

  const trip = trips?.find((t) => t.id === tripId)

  const cap = useMemo(() => getWaypointCap(), [])
  const dayMode = trip?.defaultTravelMode ?? 'Drive'
  const navPoints = scheduled
    .map((s) => placesById[s.stop.tripPlaceId])
    .filter((p): p is TripPlaceDto => !!p)
    .map((p) => ({lat: p.lat, lng: p.lng, placeId: p.googlePlaceId}))
  const dayNav = buildDayNavUrl(navPoints, cap, dayMode)
  const mixedMode = scheduled.slice(1).some((s) => s.stop.travelModeToReach !== dayMode)

  // Early return is now safe — all hooks have already been called above.
  // A failed fetch must render distinctly from "still loading" — otherwise
  // any GetItinerary error (e.g. a schema mismatch) looks identical to an
  // infinite spinner, with no signal that anything went wrong.
  if (itineraryError) return <p className="trips-field-error">{getErrorMessage(itineraryError)}</p>
  if (itineraryLoading || !dayList.length) return <p className="trips-muted">กำลังโหลดแผน…</p>

  // After the guard above, dayList is non-empty, so dayId and day are defined.
  const resolvedDayId = dayId!
  const resolvedDay = day!
  // resolvedDay.date is the server-projected date for this day (ADR-054/056) — always
  // a real 'yyyy-MM-dd' string once resolvedDay is defined, so this is safe unconditionally.
  const tripMonth = monthOfDate(resolvedDay.date)

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
      await reorder({tripId, dayId: resolvedDayId, orderedStopIds}).unwrap()
      await refetchItinerary().unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
    } finally {
      setIsReordering(false)
    }
  }

  const existingTripPlaceIds = new Set(scheduled.map((s) => s.stop.tripPlaceId))
  const remaining = scheduled.filter((s) => !s.stop.isVisited)
  const done = scheduled.filter((s) => s.stop.isVisited)
  const visitedCount = done.length
  const allVisited = scheduled.length > 0 && remaining.length === 0
  // Lead Leg = the drive INTO the first remaining Stop, shown only when a visited Stop
  // precedes it (i.e. it is not the day's very first Stop). Skipped at zero-visited. ADR-047 §4.
  const leadLeg =
    remaining.length > 0 && scheduled.indexOf(remaining[0]) > 0 ? remaining[0].stop.legToReach : null

  const detailStop = detailStopId ? scheduled.find((x) => x.stop.id === detailStopId) ?? null : null
  const detailPlace = detailStop ? placesById[detailStop.stop.tripPlaceId] : undefined

  return (
    <div className="itinerary-tab">
      <SegmentedTabs
        value={resolvedDayId}
        onChange={(v) => dispatch(setActiveDay(v))}
        options={dayList.map((d, i) => ({label: `วัน ${i + 1}`, value: d.id}))}
      />

      {dayRoute && (
        <div className={`itin-map-band${mapCollapsed ? ' collapsed' : ''}`}>
          <TripMap
            places={places ?? []}
            route={dayRoute.route}
            segments={dayRoute.segments}
            viewerLocation={dayRoute.viewerLocation}
            gestureHandling="cooperative"
            fitPadding={BAND_FIT_PADDING}
          />
          {mapCollapsed ? (
            <button
              type="button"
              className="itin-map-strip"
              aria-label="แสดงแผนที่เส้นทาง"
              aria-expanded={false}
              onClick={() => dispatch(setItineraryMapCollapsed(false))}
            >
              <MapRouteIcon className="itin-map-strip-lead" />
              <span>แสดงแผนที่เส้นทาง</span>
              <ChevronDownIcon className="itin-map-strip-chev" />
            </button>
          ) : (
            <button
              type="button"
              className="itin-map-collapse"
              aria-label="ย่อแผนที่"
              aria-expanded={true}
              onClick={() => dispatch(setItineraryMapCollapsed(true))}
            >
              <ChevronUpIcon />
            </button>
          )}
        </div>
      )}

      <div className="day-summary">
        <div className="day-stats">
          <DayStartEditor
            key={resolvedDayId}
            tripId={tripId}
            dayId={resolvedDayId}
            dayStartTime={resolvedDay.dayStartTime}
            useCurrentTimeAsStart={resolvedDay.useCurrentTimeAsStart}
            onError={setActionError}
          />
          <span>
            เสร็จ <b>{dayEnd}</b>
          </span>
          {visitedCount > 0 ? (
            <span className="stat-remain">
              เหลือเดินทาง <b>{formatDurationMinutes(remainingTravelSeconds / 60)}</b>
            </span>
          ) : (
            <span>
              เดินทางรวม <b>{formatDurationMinutes(totalTravelSeconds / 60)}</b>
            </span>
          )}
          {scheduled.length > 0 && (
            <span className="day-visited">
              <span className="dot" />
              {visitedCount}/{scheduled.length} มาแล้ว
            </span>
          )}
        </div>
        {dayNav && (
          <a
            className="btn-day-nav"
            href={dayNav.url}
            target="_blank"
            rel="noopener noreferrer"
            onClick={() =>
              appInsights.trackEvent(
                {name: 'TripNavHandoff'},
                {
                  scope: 'day',
                  travelMode: dayMode,
                  stopCount: navPoints.length,
                  coveredCount: dayNav.coveredCount,
                  overflow: dayNav.overflow,
                  mixedMode,
                },
              )
            }
          >
            <NavIcon /> นำทาง
          </a>
        )}
      </div>

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
                      onOpenDetail={() => setDetailStopId(s.stop.id)}
                      uvWarn={uvWarnThreshold}
                      feelsWarn={feelsLikeWarnThreshold}
                    />
                  )}
                </Fragment>
              )
            })}
            {scheduled.length === 0 && (
              <p className="trips-empty">ยังไม่มีจุดแวะ — เพิ่มจากคลังสถานที่</p>
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
          dayId={resolvedDayId}
          places={places ?? []}
          existingTripPlaceIds={existingTripPlaceIds}
          defaultTravelMode={trip?.defaultTravelMode ?? 'Drive'}
          onClose={() => setPickerOpen(false)}
          onAddNew={() => {
            dispatch(startAddStopCapture(resolvedDayId))
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

      {detailStop && detailPlace && (
        <StopDetailSheet
          place={detailPlace}
          arrival={detailStop.arrival}
          depart={detailStop.depart}
          dwell={detailStop.stop.dwellMinutes}
          flag={detailStop.flag}
          tripMonth={tripMonth}
          dayNumber={dayList.findIndex((d) => d.id === resolvedDayId) + 1}
          ordinal={remaining.indexOf(detailStop) + 1}
          navUrl={buildStopNavUrl(detailPlace, detailStop.stop.travelModeToReach)}
          nowReading={stopWeather[detailStop.stop.id]?.now}
          arrivalReading={stopWeather[detailStop.stop.id]?.arrival}
          weatherLoading={(stopWeather[detailStop.stop.id]?.nowLoading ?? false) || (stopWeather[detailStop.stop.id]?.arrivalLoading ?? false)}
          onEdit={() => {
            setDetailStopId(null)
            dispatch(setStopEditor(detailStop.stop.id))
          }}
          onNavigate={() =>
            appInsights.trackEvent(
              {name: 'TripNavHandoff'},
              {scope: 'stop', travelMode: detailStop.stop.travelModeToReach, hasPlaceId: !!detailPlace.googlePlaceId},
            )
          }
          onToggleVisited={async (next) => {
            try {
              await setStopVisited({tripId, stopId: detailStop.stop.id, isVisited: next}).unwrap()
              setDetailStopId(null)
            } catch (err) {
              setActionError(getErrorMessage(err))
            }
          }}
          onClose={() => setDetailStopId(null)}
        />
      )}

      {editorStopId && (
        <StopEditorDialog
          tripId={tripId}
          day={resolvedDay}
          dayNumber={dayList.findIndex((d) => d.id === resolvedDayId) + 1}
          stopId={editorStopId}
          placesById={placesById}
          onClose={() => dispatch(setStopEditor(null))}
        />
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
