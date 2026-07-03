/**
 * Convert a stored "HH:mm:ss" string to a Date (local-time, today's date as base).
 * Avoids TZ-shift issues by using setHours/setMinutes/setSeconds.
 */
export function hmsToDate(hms: string | null): Date | null {
  if (!hms) return null
  const [h, m, s] = hms.slice(0, 8).split(':').map(Number)
  const d = new Date()
  d.setHours(h ?? 0, m ?? 0, s ?? 0, 0)
  return d
}

/** Convert a Date back to "HH:mm:ss" using local-time getters. */
export function dateToHms(date: Date | null): string | null {
  if (!date) return null
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const ss = String(date.getSeconds()).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}
