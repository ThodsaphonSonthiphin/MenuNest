// frontend/src/pages/trips/hooks/useSchedule.ts
import {useMemo} from 'react'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'

export interface ScheduledStop { stop: StopDto; arrival: string; depart: string; overnight: boolean }
export type StopFlag = 'green' | 'amber'

const toMin = (hhmm: string) => { const [h, m] = hhmm.slice(0, 5).split(':').map(Number); return h * 60 + m }
const fromMin = (min: number) => `${String(Math.floor((min % 1440) / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`

/** Weekday (0 = Sunday … 6 = Saturday) for a 'yyyy-MM-dd' date — matches Places API day numbering. */
export function dayOfWeek(isoDate: string): number {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay()
}

interface OhPoint { day: number; hour: number; minute?: number }
interface OhPeriod { open?: OhPoint; close?: OhPoint }

/**
 * Is the place open at `minutes` past midnight on weekday `dow`, per the Places API
 * `regularOpeningHours` snapshot? Returns null when hours are unknown (no JSON, no
 * periods, always-open, or malformed) so the caller does not penalise on missing data.
 */
export function isOpenAt(openingHoursJson: string | null | undefined, dow: number, minutes: number): boolean | null {
  if (!openingHoursJson) return null
  let periods: OhPeriod[] | undefined
  try { periods = (JSON.parse(openingHoursJson) as {periods?: OhPeriod[]}).periods } catch { return null }
  if (!periods || periods.length === 0) return null // 24h / always-open / unknown
  for (const p of periods) {
    if (!p.open) continue
    const openMin = p.open.hour * 60 + (p.open.minute ?? 0)
    if (!p.close) { if (p.open.day === dow) return true; continue } // open with no close → open all that day
    const closeMin = p.close.hour * 60 + (p.close.minute ?? 0)
    if (p.open.day === p.close.day) {
      if (p.open.day === dow && minutes >= openMin && minutes < closeMin) return true
    } else { // overnight period (e.g. open Fri 18:00 → close Sat 02:00)
      if (p.open.day === dow && minutes >= openMin) return true
      if (p.close.day === dow && minutes < closeMin) return true
    }
  }
  return false
}

/** Forward cascade: arrival[0] = dayStart; depart = arrival + dwell; arrival[i+1] = depart + leg (ADR-008). */
export function computeSchedule(day: ItineraryDayDto): ScheduledStop[] {
  const result: ScheduledStop[] = []
  let cursor = toMin(day.dayStartTime)
  for (const stop of [...day.stops].sort((a, b) => a.sequence - b.sequence)) {
    const arrival = cursor + (stop.legToReach ? Math.round(stop.legToReach.seconds / 60) : 0)
    const depart = arrival + stop.dwellMinutes
    result.push({stop, arrival: fromMin(arrival), depart: fromMin(depart), overnight: arrival >= 1440 || depart >= 1440})
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
    const dow = dayOfWeek(day.date)
    const scheduled = computeSchedule(day).map(s => ({
      ...s,
      flag: ((): StopFlag => {
        if (s.overnight) return 'amber'
        const place = placesById[s.stop.tripPlaceId]
        if (!place) return 'green'
        // Amber if the place is closed at arrival (ADR-008: flag against opening hours too).
        if (isOpenAt(place.openingHoursJson, dow, toMin(s.arrival)) === false) return 'amber'
        return flagStop(place, s.arrival, s.depart)
      })(),
    }))
    const totalTravelSeconds = day.stops.reduce((sum, st) => sum + (st.legToReach?.seconds ?? 0), 0)
    const dayEnd = scheduled.length ? scheduled[scheduled.length - 1].depart : day.dayStartTime.slice(0, 5)
    return {scheduled, dayEnd, totalTravelSeconds}
  }, [day, placesById])
}
