// frontend/src/pages/trips/lib/stopSummary.ts
// Pure builder for the compact itinerary card's one-line summary (issue #34).
// Weather is forecast-forward: the condition description leads and rain% is appended
// only when significant; temperature is intentionally omitted from the card (it lives,
// muted, in the detail sheet). Extracted to lib/ so it gets real vitest coverage — the
// SPA has no component test harness (see CLAUDE.md).
import type {WeatherReadingDto} from '../../../shared/api/api'
import type {FlagSeverity, StopFlag} from '../hooks/useSchedule'
import {flagText} from '../timingFlag'
import {isRainy} from './weather'
import {formatDurationMinutes} from '../utils/time'

export interface StopSummary {
  /** Arrival-forecast weather, or null when there is no usable reading. */
  weather: {iconBaseUri: string | null; label: string} | null
  /** e.g. "อยู่ 1 ชม. 30 น." */
  dwellText: string
  /** Timing-flag severity (drives the dot colour) + short reason label, or null. */
  flag: {severity: FlagSeverity; label: string} | null
}

export function buildStopSummary({
  arrivalReading,
  dwellMinutes,
  flag,
}: {
  arrivalReading?: WeatherReadingDto
  dwellMinutes: number
  flag: StopFlag
}): StopSummary {
  let weather: StopSummary['weather'] = null
  if (arrivalReading?.hasData && (arrivalReading.description || arrivalReading.iconBaseUri)) {
    const desc = arrivalReading.description ?? ''
    const rainy = isRainy(arrivalReading.rainPct) && arrivalReading.rainPct != null
    const label = rainy ? (desc ? `${desc} ${arrivalReading.rainPct}%` : `ฝน ${arrivalReading.rainPct}%`) : desc
    weather = {iconBaseUri: arrivalReading.iconBaseUri ?? null, label}
  }
  return {
    weather,
    dwellText: `อยู่ ${formatDurationMinutes(dwellMinutes)}`,
    flag: flag ? {severity: flag.severity, label: flagText(flag).reasonLine} : null,
  }
}
