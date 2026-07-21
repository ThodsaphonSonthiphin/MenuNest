import type {ItineraryDayDto, HourlyReadingDto} from '../../../shared/api/api'

// Minutes-past-midnight <-> "HH:mm(:ss)". (Matches useSchedule.ts's toMin/fromMin convention;
// this module only needs the minutes->string direction, so only that half is kept here.)
/** Minutes-past-midnight -> "HH:mm:ss" (the wire shape TimeOnly binds). Wraps at 24h. */
export function minutesToHHMMSS(min: number): string {
  const wrapped = ((min % 1440) + 1440) % 1440
  return `${String(Math.floor(wrapped / 60)).padStart(2, '0')}:${String(wrapped % 60).padStart(2, '0')}:00`
}

/** Anchor arrival minus day start, in minutes: Σ legs(rounded) + Σ dwell up to (and incl. legs of) the anchor.
 *  Independent of dayStart — mirrors useSchedule.computeSchedule but unwrapped (overnight-safe). null if not found. */
export function offsetMinutes(day: ItineraryDayDto, stopId: string): number | null {
  const ordered = [...day.stops].sort((a, b) => a.sequence - b.sequence)
  let acc = 0
  for (const s of ordered) {
    acc += s.legToReach ? Math.round(s.legToReach.seconds / 60) : 0
    if (s.id === stopId) return acc
    acc += s.dwellMinutes
  }
  return null
}

/** New day-start (minutes) so the anchor arrives at targetMinuteOfDay. Negative => unreachably early. */
export function suggestedStartMinutes(targetMinuteOfDay: number, offsetMin: number): number {
  return targetMinuteOfDay - offsetMin
}

export interface ShiftKind {sameDay: boolean; deltaDays: number; movesTrip: boolean}
/** dates are 'yyyy-MM-dd'. Any cross-day target shifts the whole trip (ADR-109). */
export function classifyShift(targetDate: string, anchorDayDate: string): ShiftKind {
  const d = Math.round((Date.parse(targetDate.slice(0, 10)) - Date.parse(anchorDayDate.slice(0, 10))) / 86_400_000)
  return {sameDay: d === 0, deltaDays: d, movesTrip: d !== 0}
}

/** Min feels-like hour of the requested half (isDaytime), earliest on ties; null if the half is empty. */
export function coolestHour(hours: HourlyReadingDto[], daytime: boolean): HourlyReadingDto | null {
  const half = hours
    .filter((h) => h.isDaytime === daytime && h.feelsLikeC != null)
    .sort((a, b) => Date.parse(a.displayLocal) - Date.parse(b.displayLocal))
  let best: HourlyReadingDto | null = null
  for (const h of half) if (best == null || (h.feelsLikeC as number) < (best.feelsLikeC as number)) best = h
  return best
}

/** Reuses the 240h forecast horizon check. */
export {weatherWindow as _weatherWindow} from './weather'
export function withinHorizon(targetMs: number, nowMs: number): boolean {
  // duplicate of weather.weatherWindow's 'ok' band, kept explicit for the planner's guard
  const HORIZON_MS = 240 * 60 * 60 * 1000
  return targetMs >= nowMs && targetMs <= nowMs + HORIZON_MS
}
