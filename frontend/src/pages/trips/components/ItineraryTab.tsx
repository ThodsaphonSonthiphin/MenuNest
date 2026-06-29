// frontend/src/pages/trips/components/ItineraryTab.tsx
import {useState} from 'react'
import {
  useGetItineraryQuery,
  useListTripPlacesQuery,
  useListTripsQuery,
  useReorderStopsMutation,
  useAddStopMutation,
} from '../../../shared/api/api'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../../store/index'
import {setActiveDay, setStopEditor} from '../tripsSlice'
import {useSchedule} from '../hooks/useSchedule'
import {SegmentedTabs} from './SegmentedTabs'
import {ItineraryStopCard} from './ItineraryStopCard'
import {TravelLeg} from './TravelLeg'
import {StopEditorDialog} from './StopEditorDialog'

function bestLabel(p: TripPlaceDto): string | null {
  if (!p.bestTimeStart || !p.bestTimeEnd) return null
  return `ช่วงดี ${p.bestTimeStart.slice(0, 5)}–${p.bestTimeEnd.slice(0, 5)}`
}

/** Inline add-stop picker shown below the stop list. */
function AddStopPicker({
  tripId,
  dayId,
  places,
  existingTripPlaceIds,
  defaultTravelMode,
  onClose,
}: {
  tripId: string
  dayId: string
  places: TripPlaceDto[]
  existingTripPlaceIds: Set<string>
  defaultTravelMode: string
  onClose: () => void
}) {
  const [addStop] = useAddStopMutation()

  const available = places.filter((p) => !existingTripPlaceIds.has(p.id))

  if (available.length === 0) {
    return (
      <div className="add-stop-picker">
        <p className="trips-muted">สถานที่ทั้งหมดอยู่ในแผนแล้ว</p>
        <button className="btn-text" onClick={onClose}>ปิด</button>
      </div>
    )
  }

  return (
    <div className="add-stop-picker">
      <div className="add-stop-header">
        <span>เลือกจุดแวะ</span>
        <button className="btn-text" onClick={onClose}>✕</button>
      </div>
      <ul className="add-stop-list">
        {available.map((p) => (
          <li key={p.id}>
            <button
              className="add-stop-item"
              onClick={() => {
                addStop({
                  tripId,
                  dayId,
                  tripPlaceId: p.id,
                  dwellMinutes: 60,
                  travelModeToReach: (defaultTravelMode as 'Drive' | 'Walk' | 'Transit') ?? 'Drive',
                })
                onClose()
              }}
            >
              <span className="add-stop-name">{p.name}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}

export function ItineraryTab({tripId}: {tripId: string}) {
  const dispatch = useAppDispatch()
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  const editorStopId = useAppSelector((s) => s.trips.stopEditorStopId)
  const [pickerOpen, setPickerOpen] = useState(false)

  const {data: days} = useGetItineraryQuery(tripId)
  const {data: places} = useListTripPlacesQuery(tripId)
  const {data: trips} = useListTripsQuery()
  const [reorder] = useReorderStopsMutation()

  // Derive stable values used by useSchedule — must run before any early return.
  const dayList = days ?? []
  const dayId =
    activeDayId && dayList.some((d) => d.id === activeDayId) ? activeDayId : dayList[0]?.id
  const day = dayList.find((d) => d.id === dayId)
  const placesById = Object.fromEntries((places ?? []).map((p) => [p.id, p]))

  // EMPTY_DAY is used as a fallback so useSchedule is ALWAYS called unconditionally
  // (Rules of Hooks: hook count must be identical on every render).
  const EMPTY_DAY: ItineraryDayDto = {id: '', date: '', dayStartTime: '09:00:00', stops: []}
  const {scheduled, dayEnd, totalTravelSeconds} = useSchedule(day ?? EMPTY_DAY, placesById)

  const trip = trips?.find((t) => t.id === tripId)

  // Early return is now safe — all hooks have already been called above.
  if (!dayList.length) return <p className="trips-muted">กำลังโหลดแผน…</p>

  // After the guard above, dayList is non-empty, so dayId and day are defined.
  const resolvedDayId = dayId!
  const resolvedDay = day!

  const move = (index: number, dir: -1 | 1) => {
    const ids = scheduled.map((s) => s.stop.id)
    const j = index + dir
    if (j < 0 || j >= ids.length) return
    ;[ids[index], ids[j]] = [ids[j], ids[index]]
    reorder({tripId, dayId: resolvedDayId, orderedStopIds: ids})
  }

  const existingTripPlaceIds = new Set(scheduled.map((s) => s.stop.tripPlaceId))

  return (
    <div className="itinerary-tab">
      <SegmentedTabs
        value={resolvedDayId}
        onChange={(v) => dispatch(setActiveDay(v))}
        options={dayList.map((d, i) => ({label: `วัน ${i + 1}`, value: d.id}))}
      />

      <div className="day-summary">
        <span>
          เริ่ม <b>{resolvedDay.dayStartTime.slice(0, 5)}</b>
        </span>
        <span>
          เสร็จ <b>{dayEnd}</b>
        </span>
        <span>
          เดินทางรวม <b>{Math.round(totalTravelSeconds / 60)} น.</b>
        </span>
      </div>

      <div className="stop-list">
        {scheduled.map((s, i) => {
          const place = placesById[s.stop.tripPlaceId]
          return (
            <div key={s.stop.id}>
              {i > 0 && s.stop.legToReach && (
                <TravelLeg leg={s.stop.legToReach} mode={s.stop.travelModeToReach} />
              )}
              {place && (
                <ItineraryStopCard
                  place={place}
                  arrival={s.arrival}
                  depart={s.depart}
                  dwell={s.stop.dwellMinutes}
                  flag={s.flag}
                  bestLabel={bestLabel(place)}
                  onEdit={() => dispatch(setStopEditor(s.stop.id))}
                  onUp={() => move(i, -1)}
                  onDown={() => move(i, 1)}
                  canUp={i > 0}
                  canDown={i < scheduled.length - 1}
                />
              )}
            </div>
          )
        })}
        {scheduled.length === 0 && (
          <p className="trips-empty">ยังไม่มีจุดแวะ — เพิ่มจากคลังสถานที่</p>
        )}
      </div>

      {pickerOpen ? (
        <AddStopPicker
          tripId={tripId}
          dayId={resolvedDayId}
          places={places ?? []}
          existingTripPlaceIds={existingTripPlaceIds}
          defaultTravelMode={trip?.defaultTravelMode ?? 'Drive'}
          onClose={() => setPickerOpen(false)}
        />
      ) : (
        <button className="btn-add-stop" onClick={() => setPickerOpen(true)}>
          + เพิ่มจุดแวะ
        </button>
      )}

      {editorStopId && (
        <StopEditorDialog
          tripId={tripId}
          day={resolvedDay}
          stopId={editorStopId}
          placesById={placesById}
          onClose={() => dispatch(setStopEditor(null))}
        />
      )}
    </div>
  )
}
