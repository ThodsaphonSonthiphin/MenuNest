// frontend/src/pages/trips/components/StopEditorDialog.tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {DropDownList} from '@syncfusion/react-dropdowns'
import type {ChangeEvent as DDLChangeEvent} from '@syncfusion/react-dropdowns'
import {Button, Color} from '@syncfusion/react-buttons'
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
  stopId,
  placesById,
  onClose,
}: {
  tripId: string
  day: ItineraryDayDto
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

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      header={place?.name ?? 'แก้ไขจุดแวะ'}
      style={{width: '440px'}}
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

        <label className="stop-editor-label">จะอยู่ที่นี่กี่นาที</label>
        <DwellStepper value={dwell} onChange={setDwell} />

        <label className="stop-editor-label">การเดินทางมาที่นี่</label>
        <DropDownList
          dataSource={MODES}
          fields={{text: 'label', value: 'value'}}
          value={mode}
          onChange={handleModeChange}
        />

        {preview && (
          <div className="computed-box">
            ถึง {preview.arrival} → ออก (อัตโนมัติ) {preview.depart}
          </div>
        )}

        {saveError && <p className="trips-field-error">{saveError}</p>}

        <div className="trip-form-actions">
          <Button type="button" onClick={handleDelete}>
            ลบจุดนี้
          </Button>
          <Button color={Color.Primary} disabled={s1 || s2} onClick={save}>
            บันทึก
          </Button>
        </div>
      </div>
    </Dialog>
  )
}
