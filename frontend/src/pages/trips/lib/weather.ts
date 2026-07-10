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
