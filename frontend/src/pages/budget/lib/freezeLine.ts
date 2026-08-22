const MONTHS_SHORT = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
]

/**
 * menunest-181: the daily allowance is a *frozen* figure — it changes only
 * on a Budgeting event (marking/unmarking an everyday envelope, assigning
 * into one, or the month rolling over), never merely because a new
 * calendar day began. `frozenOn` can therefore lag behind `today` by any
 * number of days within the current month: the backend only re-freezes
 * across a month rollover (GetMonthlySummaryHandler's `IsForMonth` check),
 * not a plain day rollover, so a family that hasn't touched an everyday
 * envelope in a week is still shown a week-old freeze date, correctly.
 *
 * Both dates are viewer-local 'YYYY-MM-DD' calendar-day strings. Parsed by
 * splitting on '-', never through `new Date(string)` — that parses a bare
 * date as UTC midnight, which shifts to the previous day in any
 * negative-UTC-offset zone (the same bug `formatDateThai` exists to avoid).
 */
export function formatFreezeLine(frozenOn: string, today: string): string {
  const when = frozenOn === today ? 'Set this morning' : `Set ${formatShortDate(frozenOn)}`
  return `${when} · won't change if you spend more today`
}

function formatShortDate(isoDate: string): string {
  const [, monthStr, dayStr] = isoDate.split('-')
  const month = MONTHS_SHORT[Number(monthStr) - 1]
  return `${month} ${Number(dayStr)}`
}
