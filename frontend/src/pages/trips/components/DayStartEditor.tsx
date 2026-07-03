// frontend/src/pages/trips/components/DayStartEditor.tsx
import {useEffect, useRef, useState} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import {useSetDayStartTimeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {hmsToDate, dateToHms} from '../utils/time'

/**
 * The active Day's start time, rendered as the `เริ่ม HH:mm` value in the
 * day-summary bar and editable in place. Tapping the value opens a Syncfusion
 * TimePicker (editable=false + openOnFocus make it read as a label that opens on
 * tap); picking a time commits immediately (ADR-013) and the schedule re-cascades
 * via TripItinerary invalidation. The picked value shows optimistically and reverts
 * on failure. Parent passes key={dayId} so each Day gets a fresh instance.
 */
export function DayStartEditor({
  tripId,
  dayId,
  dayStartTime,
  onError,
}: {
  tripId: string
  dayId: string
  dayStartTime: string // "HH:mm:ss"
  onError: (msg: string | null) => void
}) {
  const [value, setValue] = useState<string>(dayStartTime)
  const [setDayStart] = useSetDayStartTimeMutation()

  // True while mounted — guards the async resolution from touching parent state
  // after a day switch unmounts this instance (key={dayId}).
  const mounted = useRef(true)
  useEffect(() => () => {
    mounted.current = false
  }, [])

  // Re-sync the displayed value to the server value after a refetch. Between a
  // pick and the refetch the local value is optimistic.
  useEffect(() => {
    setValue(dayStartTime)
  }, [dayStartTime])

  const handleChange = async (e: TimePickerChangeEvent) => {
    const hms = dateToHms(e.value)
    if (!hms || hms === value) return // ignore a cleared / unchanged pick
    setValue(hms) // optimistic
    try {
      await setDayStart({tripId, dayId, startTime: hms}).unwrap()
      if (mounted.current) onError(null)
    } catch (err) {
      if (mounted.current) {
        setValue(dayStartTime) // revert to server value
        onError(getErrorMessage(err))
      }
    }
  }

  return (
    <span className="day-start-edit">
      เริ่ม{' '}
      <TimePicker
        className="day-start-picker"
        value={hmsToDate(value)}
        onChange={handleChange}
        format="HH:mm"
        step={15}
        editable={false}
        openOnFocus
        clearButton={false}
      />
    </span>
  )
}
