// frontend/src/pages/trips/utils/date.ts
//
// Small TZ-stable converters between the API's "yyyy-MM-dd" DateOnly strings and
// local-midnight `Date` objects, plus the inclusive end-date derivation. Shared by
// TripDateEditor (and safe to reuse anywhere a trip's start/end is computed). Kept
// separate + unit-tested so the round-trip is locked against UTC-shift regressions,
// mirroring utils/time.ts for the day-start editor.

/** "yyyy-MM-dd" (an API DateOnly; may carry a time/zone suffix) → local-midnight Date. */
export function ymdToDate(ymd: string | null | undefined): Date | null {
  if (!ymd) return null
  const [y, m, d] = ymd.slice(0, 10).split('-').map(Number)
  if (!y || !m || !d) return null
  const dt = new Date(y, m - 1, d)
  return Number.isNaN(dt.getTime()) ? null : dt
}

/** Date → "yyyy-MM-dd" using local fields (no `toISOString` UTC shift). */
export function dateToYmd(d: Date | null): string | null {
  if (!d || Number.isNaN(d.getTime())) return null
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/** Inclusive end date = start + (dayCount − 1) days. `null` when start is unusable. */
export function endDate(start: Date | null, dayCount: number): Date | null {
  if (!start) return null
  const days = Math.max(1, dayCount)
  const e = new Date(start)
  e.setDate(e.getDate() + (days - 1))
  return e
}
