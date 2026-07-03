// frontend/src/pages/trips/components/StopEditorDialog.tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {DropDownList} from '@syncfusion/react-dropdowns'
import type {ChangeEvent as DDLChangeEvent} from '@syncfusion/react-dropdowns'
import {
  useUpdateStopMutation,
  useUpdateTripPlaceMutation,
  useRemoveStopMutation,
  type ItineraryDayDto,
  type TripPlaceDto,
  type TravelMode,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {computeSchedule} from '../hooks/useSchedule'
import {catColor, catLabel} from '../placeCategory'
import {DwellStepper} from './DwellStepper'
import {BestTimeBar} from './BestTimeBar'

const MODES: {label: string; value: TravelMode}[] = [
  {label: '🚗 รถยนต์', value: 'Drive'},
  {label: '🚶 เดิน', value: 'Walk'},
  {label: '🚃 ขนส่ง', value: 'Transit'},
]

export function StopEditorDialog({
  tripId,
  day,
  dayNumber,
  stopId,
  placesById,
  onClose,
}: {
  tripId: string
  day: ItineraryDayDto
  dayNumber: number
  stopId: string
  placesById: Record<string, TripPlaceDto>
  onClose: () => void
}) {
  const stop = day.stops.find((s) => s.id === stopId)!
  const place = placesById[stop.tripPlaceId]

  const [dwell, setDwell] = useState(stop.dwellMinutes)
  const [mode, setMode] = useState<TravelMode>(stop.travelModeToReach)
  const [bestStart, setBestStart] = useState<string | null>(place?.bestTimeStart ?? null)
  const [bestEnd, setBestEnd] = useState<string | null>(place?.bestTimeEnd ?? null)
  const [saveError, setSaveError] = useState<string | null>(null)

  const [updateStop, {isLoading: s1}] = useUpdateStopMutation()
  const [updatePlace, {isLoading: s2}] = useUpdateTripPlaceMutation()
  const [removeStop] = useRemoveStopMutation()

  // 1-based position of this stop within the day (for the "จุดที่ N" breadcrumb).
  const ordinal =
    [...day.stops].sort((a, b) => a.sequence - b.sequence).findIndex((s) => s.id === stopId) + 1

  // Travel leg from the previous stop — stored on the stop, computed server-side.
  const leg = stop.legToReach
  const legMinutes = leg ? Math.round(leg.seconds / 60) : null
  const legKm = leg ? (leg.meters / 1000).toFixed(1) : null

  // Local computed preview: clone the day with the edited dwell/mode for this stop
  const preview = computeSchedule({
    ...day,
    stops: day.stops.map((s) =>
      s.id === stopId ? {...s, dwellMinutes: dwell, travelModeToReach: mode} : s,
    ),
  }).find((p) => p.stop.id === stopId)

  const save = async () => {
    setSaveError(null)
    try {
      await updateStop({tripId, stopId, dwellMinutes: dwell, travelModeToReach: mode}).unwrap()
      if (
        place &&
        (bestStart !== place.bestTimeStart || bestEnd !== place.bestTimeEnd)
      ) {
        await updatePlace({
          tripId,
          placeId: place.id,
          name: place.name,
          category: place.category,
          address: place.address,
          feeNote: place.feeNote,
          notes: place.notes,
          bestTimeStart: bestStart,
          bestTimeEnd: bestEnd,
        }).unwrap()
      }
      onClose()
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }

  const handleDelete = async () => {
    setSaveError(null)
    try {
      await removeStop({tripId, stopId}).unwrap()
      onClose()
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }

  const handleModeChange = (e: DDLChangeEvent) => {
    setMode(e.value as TravelMode)
  }

  const header = (
    <div className="se-head">
      <div className="se-title">{place?.name ?? 'แก้ไขจุดแวะ'}</div>
      {place && (
        <div className="se-meta">
          <span className="se-cat">
            <span className="se-cat-dot" style={{background: catColor(place.category)}} />
            {catLabel(place.category)}
          </span>
          <span className="se-crumb">
            วัน {dayNumber} · จุดที่ {ordinal}
          </span>
        </div>
      )}
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="stop-editor-dialog"
      header={header}
      style={{width: 'min(480px, calc(100vw - 24px))'}}
    >
      <div className="stop-editor">
        <BestTimeBar
          start={bestStart}
          end={bestEnd}
          onChange={(s, e) => {
            setBestStart(s)
            setBestEnd(e)
          }}
        />

        <section className="se-sec">
          <div className="se-sec-head">
            <span className="se-ico">⏱️</span>จะอยู่ที่นี่กี่นาที
          </div>
          <DwellStepper value={dwell} onChange={setDwell} />
        </section>

        <section className="se-sec">
          <div className="se-sec-head">
            <span className="se-ico">🚋</span>การเดินทางมาที่นี่
          </div>
          <DropDownList
            className="se-mode"
            dataSource={MODES}
            fields={{text: 'label', value: 'value'}}
            value={mode}
            onChange={handleModeChange}
          />
          {leg && (
            <p className="se-leg">
              {legMinutes} นาที · {legKm} กม จากจุดก่อนหน้า · คำนวณอัตโนมัติ
            </p>
          )}
        </section>

        {preview && (
          <div className="se-sched">
            <div className="se-sched-col">
              <div className="se-sched-lab">ถึง</div>
              <div className="se-sched-val">{preview.arrival}</div>
            </div>
            <div className="se-sched-arrow">→</div>
            <div className="se-sched-col out">
              <div className="se-sched-lab">ออก · อัตโนมัติ</div>
              <div className="se-sched-val">{preview.depart}</div>
            </div>
          </div>
        )}

        {saveError && <p className="trips-field-error">{saveError}</p>}

        <div className="se-foot">
          <button type="button" className="se-delete" onClick={handleDelete}>
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" />
            </svg>
            ลบจุดนี้
          </button>
          <button
            type="button"
            className="se-save"
            disabled={s1 || s2}
            onClick={save}
          >
            บันทึก
          </button>
        </div>
      </div>
    </Dialog>
  )
}
