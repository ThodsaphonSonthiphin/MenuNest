// Pure helpers backing the /settings "เตือนอากาศ" numeric threshold controls (ADR-105).
// Storage is the tri-state on UserSettings (ADR-091): null = built-in default, 0 = off,
// N = warn at >= N. The itinerary-card badge (lib/weather.ts) reads that same encoding
// unchanged; these helpers only translate stored values <-> the on/off + number the UI shows.

// Bounds mirror the server validator (UpdateUserSettingsValidator: UV 0..15, feels 0..60).
// Min is 1 (not 0) when a threshold is ON, because 0 is reserved for "off".
export const UV_MIN = 1
export const UV_MAX = 15
export const FEELS_MIN = 1
export const FEELS_MAX = 60

export interface AlertControl {
  on: boolean
  value: number
}

/** Stored tri-state (null=default, 0=off, N=on@N) -> the on/off + value the UI renders. */
export function alertControlFromStored(stored: number | null | undefined, dflt: number): AlertControl {
  if (stored == null) return {on: true, value: dflt}
  if (stored === 0) return {on: false, value: dflt}
  return {on: true, value: stored}
}

/** On/off + value -> the value the full-snapshot PUT persists (off => 0). */
export function storedFromAlertControl(on: boolean, value: number): number {
  return on ? value : 0
}

/** Clamp a typed value to its axis integer range so a stored value can never fail server validation. */
export function clampThreshold(value: number, min: number, max: number): number {
  const n = Math.round(Number.isFinite(value) ? value : min)
  return Math.min(max, Math.max(min, n))
}