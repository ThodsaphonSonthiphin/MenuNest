// frontend/src/pages/trips/lib/tripsGrid.ts
// Pure helpers for the Trips grid. The SPA has no component test harness, so
// anything that can be a function lives here and is covered by vitest.

export type SortDirection = 'Ascending' | 'Descending'

/**
 * Syncfusion's Grid raises the sort through its UrlAdaptor, which lower-cases
 * the direction (`ascending` / `descending`). The Grid's own header renderer,
 * however, compares `sortSettings.columns[].direction` against the exact
 * strings `'Ascending'` / `'Descending'` — so feeding the lower-cased value
 * back in leaves the header with no sort indicator at all.
 *
 * That is not merely cosmetic: `initiateSort` derives the next direction from
 * the presence of the `sf-ascending` class in the header. With no indicator
 * rendered, every click re-emits `Ascending`, the URL never changes, the
 * deferred data request is never resolved, and the grid hangs under its
 * spinner. Normalising here is what makes a second click mean "descending".
 */
export function normalizeSortDirection(direction: unknown): SortDirection {
  return String(direction ?? '').toLowerCase().startsWith('desc') ? 'Descending' : 'Ascending'
}

/**
 * Render a trip's `startDate` as dd/MM/yyyy.
 *
 * The API serialises `DateOnly` as a plain `yyyy-MM-dd` string, so the Grid's
 * `type="date"` / `format` pair never applies (it formats Date objects only).
 * Splitting the string rather than going through `new Date()` also keeps the
 * day off the timezone: `new Date('2026-03-01')` is UTC midnight and renders as
 * the 28th of February anywhere west of Greenwich.
 */
export function formatTripDate(value: unknown): string {
  if (typeof value !== 'string') return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!m) return ''
  const [, y, mo, d] = m
  return `${d}/${mo}/${y}`
}
