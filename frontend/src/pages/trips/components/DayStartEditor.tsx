// frontend/src/pages/trips/components/DayStartEditor.tsx
import {useEffect, useRef, useState} from 'react'
import type {ChangeEvent} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import {useSetDayStartTimeMutation, useSetDayUseCurrentTimeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {hmsToDate, dateToHms} from '../utils/time'

/**
 * The active Day's start time, rendered as the `เริ่ม HH:mm` value in the
 * day-summary bar and editable in place. Tapping the value opens a Syncfusion
 * TimePicker (editable=false + openOnFocus make it read as a label that opens on
 * tap); picking a time — or tapping the adjacent "ตอนนี้" (now) button — commits
 * immediately (ADR-013) and the schedule re-cascades via TripItinerary invalidation.
 * The picked value shows optimistically and reverts on failure. Parent passes
 * key={dayId} so each Day gets a fresh instance.
 *
 * The "ใช้เวลาปัจจุบันเสมอ" checkbox persists a per-Day flag (UseCurrentTimeAsStart):
 * while set, the backend re-seeds dayStartTime from the real clock on every
 * itinerary fetch, so the schedule is always current whenever the trip is opened.
 * Manual editing is disabled while the flag is on, since any pick would just be
 * overwritten by the next fetch.
 */
export function DayStartEditor({
  tripId,
  dayId,
  dayStartTime,
  useCurrentTimeAsStart,
  onError,
}: {
  tripId: string
  dayId: string
  dayStartTime: string // "HH:mm:ss"
  useCurrentTimeAsStart: boolean
  onError: (msg: string | null) => void
}) {
  const [value, setValue] = useState<string>(dayStartTime)
  const [setDayStart] = useSetDayStartTimeMutation()
  const [setUseCurrentTime] = useSetDayUseCurrentTimeMutation()

  // True while mounted — guards the async resolution from touching parent state
  // after a day switch unmounts this instance (key={dayId}). Set in the effect body
  // (not just via useRef's initial value) so it is restored to true on StrictMode's
  // dev mount→unmount→remount; otherwise the cleanup leaves it false and the
  // post-await onError/revert never run.
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // Re-sync the displayed value to the server value after a refetch. Between a
  // pick and the refetch the local value is optimistic.
  useEffect(() => {
    setValue(dayStartTime)
  }, [dayStartTime])

  const commit = async (hms: string | null) => {
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

  const handleChange = (e: TimePickerChangeEvent) => commit(dateToHms(e.value))
  const handleNow = () => commit(dateToHms(new Date()))

  const handleToggleUseCurrentTime = async (e: ChangeEvent<HTMLInputElement>) => {
    const next = e.target.checked
    try {
      await setUseCurrentTime({tripId, dayId, useCurrentTime: next}).unwrap()
      if (mounted.current) onError(null)
    } catch (err) {
      if (mounted.current) onError(getErrorMessage(err))
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
        disabled={useCurrentTimeAsStart}
      />
      {!useCurrentTimeAsStart && (
        <button
          type="button"
          className="day-start-now-btn"
          onClick={handleNow}
          aria-label="ตั้งเวลาเริ่มเป็นเวลาปัจจุบัน"
        >
          ตอนนี้
        </button>
      )}
      <label className="day-start-live-toggle">
        <input
          type="checkbox"
          checked={useCurrentTimeAsStart}
          onChange={handleToggleUseCurrentTime}
        />
        ใช้เวลาปัจจุบันเสมอ
      </label>
    </span>
  )
}
