/**
 * The viewer's IANA time zone (e.g. "Asia/Bangkok"), sent with every Budget
 * call so the backend can resolve "today" into the viewer's local
 * wall-clock day (menunest-189) instead of the server's UTC day. Falls back
 * to "UTC" on the rare browser without Intl time-zone support.
 *
 * Mirrors the Trips module's own copy (frontend/src/pages/trips/utils/time.ts,
 * getViewerTimeZone) — kept as a separate shared helper so the Budget module
 * isn't coupled to Trips, per each module's call sites sharing one
 * definition instead of repeating the Intl expression inline.
 */
export function getViewerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
}
