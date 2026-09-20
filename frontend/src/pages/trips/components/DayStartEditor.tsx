// frontend/src/pages/trips/components/DayStartEditor.tsx
import {useEffect, useRef, useState} from 'react'
import type {ChangeEvent} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import {useSetDayStartTimeMutation, useSetDayUseCurrentTimeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {hmsToDate, dateToHms} from '../utils/time'

/**
 * True while mounted — guards an async resolution from touching parent state after a Day
 * switch unmounts the instance (callers pass key={dayId}). Set in the effect BODY, not just
 * via useRef's initial value, so it is restored on StrictMode's dev mount→unmount→remount;
 * otherwise the cleanup leaves it false and the post-await onError/revert never run.
 */
function useMountedRef() {
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  return mounted
}

/**
 * The active Day's start time as an editable VALUE — nothing else. It renders the bare
 * `HH:mm`, which is the `เริ่ม–เสร็จ` figure the **Plan summary** shows (spec §4.1); the
 * surrounding `.k` label already names it, so this carries no label of its own.
 *
 * Tapping the value opens a Syncfusion TimePicker (editable=false + openOnFocus make it read
 * as a label that opens on tap); picking a time commits immediately (ADR-013) and the
 * schedule re-cascades via TripItinerary invalidation. The picked value shows optimistically
 * and reverts on failure.
 *
 * The `ตอนนี้` button and the `ใช้เวลาปัจจุบันเสมอ` toggle deliberately live in
 * `DayTimeControls`, NOT here: the summary row is only 346px wide on the Plan panel and those
 * two controls cost 153px of it, which crushed the `เดินทางรวม` stat to 22px and wrapped its
 * text one syllable per line (#154). They belong below the fold, with the Day's other
 * controls.
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
  const mounted = useMountedRef()

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
      aria-label="เวลาเริ่มของวัน"
    />
  )
}

/**
 * The Day's start-time CONTROLS — `ตอนนี้` (set the start to the clock now) and the
 * `ใช้เวลาปัจจุบันเสมอ` toggle. Split out of `DayStartEditor` so the Plan summary can show
 * the `เริ่ม–เสร็จ` figure alone and still fit one line (#154); these render in the Plan
 * body, which is reachable from the `half` detent up.
 *
 * The `ใช้เวลาปัจจุบันเสมอ` checkbox persists a per-Day flag (UseCurrentTimeAsStart): while
 * set, the backend re-seeds dayStartTime from the real clock on every itinerary fetch, so the
 * schedule is always current whenever the trip is opened. Manual editing is disabled while
 * the flag is on, since any pick would just be overwritten by the next fetch — which is why
 * `ตอนนี้` hides then too.
 */
export function DayTimeControls({
  tripId,
  dayId,
  dayStartTime,
  useCurrentTimeAsStart,
  locked = false,
  onError,
}: {
  tripId: string
  dayId: string
  dayStartTime: string // "HH:mm:ss"
  useCurrentTimeAsStart: boolean
  locked?: boolean
  onError: (msg: string | null) => void
}) {
  const [setDayStart] = useSetDayStartTimeMutation()
  const [setUseCurrentTime] = useSetDayUseCurrentTimeMutation()
  const mounted = useMountedRef()

  const handleNow = async () => {
    const hms = dateToHms(new Date())
    if (!hms || hms === dayStartTime) return
    try {
      await setDayStart({tripId, dayId, startTime: hms}).unwrap()
      if (mounted.current) onError(null)
    } catch (err) {
      if (mounted.current) onError(getErrorMessage(err))
    }
  }

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
    <div className="day-time-controls">
      <span className="dtc-label">เวลาเริ่มวัน</span>
      {!useCurrentTimeAsStart && !locked && (
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
          checked={locked ? true : useCurrentTimeAsStart}
          disabled={locked}
          onChange={locked ? undefined : handleToggleUseCurrentTime}
        />
        ใช้เวลาปัจจุบันเสมอ{locked && ' (โหมดประจำวัน)'}
      </label>
    </div>
  )
}
