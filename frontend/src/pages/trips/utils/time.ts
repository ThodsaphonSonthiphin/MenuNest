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

/**
 * Format a duration given in minutes as "X ชม. Y น." once it reaches an hour,
 * otherwise plain "Y น." Raw double/triple-digit minute counts (e.g. "133 น.")
 * are hard to read at a glance.
 */
export function formatDurationMinutes(totalMinutes: number): string {
  const m = Math.max(0, Math.round(totalMinutes))
  const hours = Math.floor(m / 60)
  const minutes = m % 60
  return hours > 0 ? `${hours} ชม. ${minutes} น.` : `${minutes} น.`
}
