// frontend/src/pages/trips/components/TripDateEditor.tsx
import {useEffect, useRef, useState} from 'react'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {useUpdateTripMutation, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {ymdToDate, dateToYmd, endDate} from '../utils/date'

const DATE_FMT = 'dd MMM yyyy'

/** en date label matching the DatePicker's "dd MMM yyyy" field display. */
function fmt(d: Date | null): string {
  return d
    ? d.toLocaleDateString('en-GB', {day: '2-digit', month: 'short', year: 'numeric'})
    : ''
}

/**
 * The trip's start date, rendered in the detail header and editable in place.
 * Tapping the value opens a Syncfusion DatePicker (editable=false + openOnFocus
 * make it read as a label that opens on tap — the same treatment as the day-start
 * editor, ADR-012); picking a date commits immediately (ADR-013) via updateTrip,
 * which reschedules the trip and realigns the itinerary days server-side. Only the
 * start date changes here — dayCount and the other fields are carried through
 * unchanged, so no itinerary days are dropped (shrinking is out of scope). The
 * picked value shows optimistically and reverts on failure; the derived inclusive
 * end date follows for multi-day trips.
 */
export function TripDateEditor({
  trip,
  onError,
}: {
  trip: TripDto
  onError: (msg: string | null) => void
}) {
  // Bare identifier for the server start date — the effect below re-syncs local
  // state to it verbatim (the recognized "reset state when a prop changes" shape;
  // a `trip.startDate` member expression is not recognized by the lint rule).
  const serverStart = trip.startDate

  // Holds the raw server value (or an optimistic "yyyy-MM-dd").
  const [start, setStart] = useState<string>(serverStart)
  const [updateTrip] = useUpdateTripMutation()

  // True while mounted — guards the async resolution from touching parent state
  // after the component unmounts (e.g. navigating away mid-request). Set in the
  // effect body (not just via useRef's initial value) so it is restored to true on
  // StrictMode's dev mount→unmount→remount; otherwise the cleanup leaves it false
  // and the post-await onError/revert never run.
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // Re-sync to the server value after the mutation's TripDetail refetch. Between a
  // pick and that refetch the local value is optimistic.
  useEffect(() => {
    setStart(serverStart)
  }, [serverStart])

  const startYmd = start.slice(0, 10)

  const handleChange = async (e: DatePickerChangeEvent) => {
    const ymd = dateToYmd(e.value)
    if (!ymd || ymd === startYmd) return // ignore a cleared / unchanged pick
    setStart(ymd) // optimistic
    try {
      await updateTrip({
        id: trip.id,
        name: trip.name,
        destination: trip.destination,
        startDate: ymd,
        dayCount: trip.dayCount,
        defaultTravelMode: trip.defaultTravelMode,
      }).unwrap()
      if (mounted.current) onError(null)
    } catch (err) {
      if (mounted.current) {
        setStart(serverStart) // revert to server value
        onError(getErrorMessage(err))
      }
    }
  }

  const startDt = ymdToDate(startYmd)
  const end = endDate(startDt, trip.dayCount)

  return (
    <span className="trip-date-edit">
      <DatePicker
        className="trip-date-picker"
        value={startDt}
        onChange={handleChange}
        format={DATE_FMT}
        editable={false}
        openOnFocus
        clearButton={false}
      />
      {trip.dayCount > 1 && end && <span className="trip-date-end">– {fmt(end)}</span>}
    </span>
  )
}
