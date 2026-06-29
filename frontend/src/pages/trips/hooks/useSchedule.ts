// frontend/src/pages/trips/hooks/useSchedule.ts
import {useMemo} from 'react'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'

export interface ScheduledStop { stop: StopDto; arrival: string; depart: string }
export type StopFlag = 'green' | 'amber'

const toMin = (hhmm: string) => { const [h, m] = hhmm.slice(0, 5).split(':').map(Number); return h * 60 + m }
const fromMin = (min: number) => `${String(Math.floor((min % 1440) / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`

/** Forward cascade: arrival[0] = dayStart; depart = arrival + dwell; arrival[i+1] = depart + leg (ADR-008). */
export function computeSchedule(day: ItineraryDayDto): ScheduledStop[] {
  const result: ScheduledStop[] = []
  let cursor = toMin(day.dayStartTime)
  for (const stop of [...day.stops].sort((a, b) => a.sequence - b.sequence)) {
    const arrival = cursor + (stop.legToReach ? Math.round(stop.legToReach.seconds / 60) : 0)
    const depart = arrival + stop.dwellMinutes
    result.push({stop, arrival: fromMin(arrival), depart: fromMin(depart)})
    cursor = depart
  }
  return result
}

/** Green when the arrival falls inside the place's best-time window (when one is set); amber otherwise. */
export function flagStop(place: TripPlaceDto, arrival: string, _depart: string): StopFlag {
  if (!place.bestTimeStart || !place.bestTimeEnd) return 'green'
  const a = toMin(arrival)
  return a >= toMin(place.bestTimeStart) && a <= toMin(place.bestTimeEnd) ? 'green' : 'amber'
}

export function useSchedule(day: ItineraryDayDto, placesById: Record<string, TripPlaceDto>) {
  return useMemo(() => {
    const scheduled = computeSchedule(day).map(s => ({
      ...s,
      flag: placesById[s.stop.tripPlaceId] ? flagStop(placesById[s.stop.tripPlaceId], s.arrival, s.depart) : 'green' as StopFlag,
    }))
    const totalTravelSeconds = day.stops.reduce((sum, st) => sum + (st.legToReach?.seconds ?? 0), 0)
    const dayEnd = scheduled.length ? scheduled[scheduled.length - 1].depart : day.dayStartTime.slice(0, 5)
    return {scheduled, dayEnd, totalTravelSeconds}
  }, [day, placesById])
}
