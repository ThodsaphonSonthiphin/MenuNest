export interface AlertOption {
  label: string
  value: number
}

/** UV-index alert thresholds a user can preselect on /settings (ADR-092 follow-up, #40). */
export const UV_ALERT_OPTIONS: AlertOption[] = [
  { label: 'ปานกลางขึ้นไป (≥ 3)', value: 3 },
  { label: 'สูงขึ้นไป (≥ 6)', value: 6 },
  { label: 'สูงมากขึ้นไป (≥ 8)', value: 8 },
  { label: 'ปิดการเตือน', value: 0 },
]

/** Feels-like (°C) alert thresholds a user can preselect on /settings. */
export const FEELS_ALERT_OPTIONS: AlertOption[] = [
  { label: '≥ 38°', value: 38 },
  { label: '≥ 40°', value: 40 },
  { label: '≥ 42°', value: 42 },
  { label: 'ปิดการเตือน', value: 0 },
]

/**
 * Dropdown value to preselect for a stored setting: null/undefined (never set) falls
 * back to the built-in default; any stored number — including 0, meaning "off" — wins
 * verbatim so the "ปิดการเตือน" choice round-trips correctly.
 */
export function selectedAlertValue(stored: number | null | undefined, dflt: number): number {
  return stored == null ? dflt : stored
}
