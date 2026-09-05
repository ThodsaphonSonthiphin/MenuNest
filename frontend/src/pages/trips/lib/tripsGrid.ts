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
 * The Grid's own `format` attribute does work on the `yyyy-MM-dd` string the API
 * serialises `DateOnly` into — the column previously carried `format="yMd"`, which
 * rendered 2026-01-10 as `1/10/2026`, i.e. US month-first, which a Thai reader
 * reads as 1 October. `format="dd/MM/yyyy"` fixes the field order but not the
 * second, worse half: the Grid parses the string as UTC midnight and formats it in
 * the viewer's timezone, so west of Greenwich every trip shows the day *before* it
 * starts (measured under America/Los_Angeles: 2026-01-10 rendered `09/01/2026`).
 *
 * Splitting the string keeps the day off the clock entirely, which is right for a
 * DateOnly: it names a calendar day, not an instant.
 */
export function formatTripDate(value: unknown): string {
  if (typeof value !== 'string') return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!m) return ''
  const [, y, mo, d] = m
  return `${d}/${mo}/${y}`
}
