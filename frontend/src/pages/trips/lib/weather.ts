import type {WeatherPointDto, WeatherReadingDto} from '../../../shared/api/api'

// Google's daily/hourly forecast horizon is 10 days = 240 hours (verified: hours=241 -> HTTP 400).
const HORIZON_MS = 240 * 60 * 60 * 1000
export type WeatherWindow = 'past' | 'ok' | 'beyond'

/** Classify an arrival instant against the live clock; 'ok' is the only fetchable window. */
export function weatherWindow(arrivalMs: number, nowMs: number): WeatherWindow {
  if (arrivalMs < nowMs) return 'past'
  if (arrivalMs > nowMs + HORIZON_MS) return 'beyond'
  return 'ok'
}

/** Google weather condition icon URL: light `.svg`, dark `_dark.svg` (ADR-031). */
export function iconUrl(iconBaseUri: string, isDark: boolean): string {
  return `${iconBaseUri}${isDark ? '_dark' : ''}.svg`
}

export const RAIN_TINT_THRESHOLD = 60
/** The On-arrival chip gets the deeper "rainy" tint at/above this rain probability. */
export function isRainy(rainPct: number | null | undefined): boolean {
  return (rainPct ?? 0) >= RAIN_TINT_THRESHOLD
}

export type ChipState = 'loading' | 'nodata' | 'data'
/** Which visual state a chip renders, given its query loading flag and (maybe) a reading. */
export function weatherChipState(isLoading: boolean, reading: WeatherReadingDto | undefined): ChipState {
  if (isLoading && !reading) return 'loading'
  if (!reading || !reading.hasData) return 'nodata'
  return 'data'
}

/** Local wall-clock ISO for a stop's arrival: day date (yyyy-MM-dd) + schedule HH:MM. */
export function arrivalIso(dayDate: string, hhmm: string): string {
  return `${dayDate.slice(0, 10)}T${hhmm.slice(0, 5)}:00`
}

interface WeatherStop {
  stopId: string
  lat: number
  lng: number
  arrivalIso: string
}

/** Split scheduled stops into the two batches the endpoint needs. Now = every finite-coord stop;
 *  On-arrival = only stops whose arrival is within [now, now+240h] (past/beyond are gated out and
 *  render No-data client-side without a request). */
export function buildWeatherBatches(
  stops: WeatherStop[],
  nowMs: number,
): {now: WeatherPointDto[]; arrival: WeatherPointDto[]} {
  const now: WeatherPointDto[] = []
  const arrival: WeatherPointDto[] = []
  for (const s of stops) {
    if (!Number.isFinite(s.lat) || !Number.isFinite(s.lng)) continue
    now.push({stopId: s.stopId, lat: s.lat, lng: s.lng})
    if (weatherWindow(Date.parse(s.arrivalIso), nowMs) === 'ok') {
      arrival.push({stopId: s.stopId, lat: s.lat, lng: s.lng, arrivalIso: s.arrivalIso})
    }
  }
  return {now, arrival}
}


export const UV_WARN_DEFAULT = 6
export const FEELS_WARN_DEFAULT = 40

export type UvBandKey = 'low' | 'mod' | 'high' | 'vhigh' | 'ext'
/** WHO UV band -> key + canonical Thai word (CONTEXT.md). */
export function uvBand(uv: number): {key: UvBandKey; word: string} {
  if (uv <= 2) return {key: 'low', word: 'ต่ำ'}
  if (uv <= 5) return {key: 'mod', word: 'ปานกลาง'}
  if (uv <= 7) return {key: 'high', word: 'สูง'}
  if (uv <= 10) return {key: 'vhigh', word: 'สูงมาก'}
  return {key: 'ext', word: 'อันตราย'}
}

/** Tri-state stored threshold -> effective value. null/undefined -> default; 0 -> null (off); N -> N. */
export function effectiveThreshold(stored: number | null | undefined, dflt: number): number | null {
  if (stored == null) return dflt
  if (stored === 0) return null
  return stored
}

/** Compact-card alert (On-arrival only, ADR-092): which threshold-crossing badges to show. */
export function weatherAlertBadges(
  arrival: WeatherReadingDto | undefined,
  uvStored: number | null,
  feelsStored: number | null,
): {uv?: number; feels?: number} {
  if (!arrival || !arrival.hasData) return {}
  const out: {uv?: number; feels?: number} = {}
  const uvT = effectiveThreshold(uvStored, UV_WARN_DEFAULT)
  if (uvT != null && arrival.uvIndex != null && arrival.uvIndex >= uvT) out.uv = arrival.uvIndex
  const feelsT = effectiveThreshold(feelsStored, FEELS_WARN_DEFAULT)
  if (feelsT != null && arrival.feelsLikeC != null && Math.round(arrival.feelsLikeC) >= feelsT) {
    out.feels = Math.round(arrival.feelsLikeC)
  }
  return out
}

/** Rollover-divider label for an hourly-strip cell whose date differs from the previous cell's.
 *  `dateStr` / `anchorDateStr` are 'YYYY-MM-DD'. One day after the anchor -> 'พรุ่งนี้'; otherwise a
 *  short Thai weekday+date label. Shared by the trips planner (#46) and the Discover strip (#47). */
export function hourlyRolloverLabel(dateStr: string, anchorDateStr: string): string {
  const deltaDays = Math.round((Date.parse(dateStr) - Date.parse(anchorDateStr)) / 86_400_000)
  return deltaDays === 1
    ? 'พรุ่งนี้'
    : new Date(`${dateStr}T00:00:00`).toLocaleDateString('th-TH', {weekday: 'short', day: 'numeric', month: 'short'})
}
