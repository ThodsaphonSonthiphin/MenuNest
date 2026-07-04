// frontend/src/pages/trips/hooks/useSchedule.ts
import {useMemo} from 'react'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'

export interface ScheduledStop {
  stop: StopDto; arrival: string; depart: string; overnight: boolean
  arrivedAfterMidnight: boolean // raw arrival >= 1440 (this stop is reached after midnight)
}
export type StopFlag = 'green' | 'amber'

export type FlagReason = 'overflow' | 'closed' | 'off-window'
export type FlagSeverity = 'problem' | 'suggestion'
export type ClosedKind = 'before-open' | 'on-break' | 'after-close' | 'all-day'

export interface TimingFlag {
  reason: FlagReason
  severity: FlagSeverity
  closedKind?: ClosedKind
  reopenAt?: string             // 'HH:MM' — before-open | on-break
  bestStart?: string            // 'HH:MM' — off-window
  bestEnd?: string              // 'HH:MM' — off-window
  windowDir?: 'before' | 'after'
  arrival?: string              // 'HH:MM' (post-midnight) — overflow
}
export type ScheduledStopWithFlag = ScheduledStop & {flag: TimingFlag | null}

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

const pointMin = (p: OhPoint) => p.hour * 60 + (p.minute ?? 0)

function parsePeriods(json: string | null | undefined): OhPeriod[] | null {
  if (!json) return null
  try {
    const periods = (JSON.parse(json) as {periods?: OhPeriod[]}).periods
    return periods && periods.length ? periods : null
  } catch { return null }
}

/** Classify why a place is closed at `minutes` on weekday `dow` — call only when isOpenAt(...) === false. Null when hours unknown. */
export function closedFlag(openingHoursJson: string | null | undefined, dow: number, minutes: number): TimingFlag | null {
  const periods = parsePeriods(openingHoursJson)
  if (!periods) return null
  let reopen: number | null = null // earliest open later today
  let openedEarlier = false        // opened OR closed earlier today (incl. overnight close)
  for (const p of periods) {
    if (p.open && p.open.day === dow) {
      const om = pointMin(p.open)
      if (om > minutes) reopen = reopen === null ? om : Math.min(reopen, om)
      else openedEarlier = true
    }
    if (p.close && p.close.day === dow && pointMin(p.close) <= minutes) openedEarlier = true
  }
  const closedKind: ClosedKind =
    reopen !== null ? (openedEarlier ? 'on-break' : 'before-open')
                    : (openedEarlier ? 'after-close' : 'all-day')
  return {reason: 'closed', severity: 'problem', closedKind, reopenAt: reopen !== null ? fromMin(reopen) : undefined}
}

/** Flag when arrival is outside the place's best-time window; null when inside (bounds inclusive) or no window set. */
export function offWindowFlag(place: TripPlaceDto, arrival: string): TimingFlag | null {
  if (!place.bestTimeStart || !place.bestTimeEnd) return null
  const a = toMin(arrival)
  const start = toMin(place.bestTimeStart)
  const end = toMin(place.bestTimeEnd)
  if (a >= start && a <= end) return null
  return {
    reason: 'off-window',
    severity: 'suggestion',
    bestStart: place.bestTimeStart.slice(0, 5),
    bestEnd: place.bestTimeEnd.slice(0, 5),
    windowDir: a < start ? 'before' : 'after',
  }
}

/** Select one most-severe flag per stop: overflow (once, first stop reached after midnight) > closed > off-window; null when none. */
export function composeFlags(
  scheduled: ScheduledStop[],
  placesById: Record<string, TripPlaceDto>,
  dow: number,
): ScheduledStopWithFlag[] {
  let overflowShown = false
  return scheduled.map(s => {
    let flag: TimingFlag | null = null
    if (!overflowShown && s.arrivedAfterMidnight) {
      flag = {reason: 'overflow', severity: 'problem', arrival: s.arrival}
      overflowShown = true
    } else {
      const place = placesById[s.stop.tripPlaceId]
      if (place) {
        const arr = toMin(s.arrival)
        if (isOpenAt(place.openingHoursJson, dow, arr) === false) flag = closedFlag(place.openingHoursJson, dow, arr)
        if (!flag) flag = offWindowFlag(place, s.arrival)
      }
    }
    return {...s, flag}
  })
}

/** Forward cascade: arrival[0] = dayStart; depart = arrival + dwell; arrival[i+1] = depart + leg (ADR-008). */
export function computeSchedule(day: ItineraryDayDto): ScheduledStop[] {
  const result: ScheduledStop[] = []
  let cursor = toMin(day.dayStartTime)
  for (const stop of [...day.stops].sort((a, b) => a.sequence - b.sequence)) {
    const arrival = cursor + (stop.legToReach ? Math.round(stop.legToReach.seconds / 60) : 0)
    const depart = arrival + stop.dwellMinutes
    result.push({stop, arrival: fromMin(arrival), depart: fromMin(depart), overnight: arrival >= 1440 || depart >= 1440, arrivedAfterMidnight: arrival >= 1440})
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
