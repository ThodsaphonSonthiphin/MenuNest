import type {SeasonPeriod} from '../../../shared/api/api'

/** Thai month abbreviations, 0-indexed (0 = January). */
export const THAI_MONTHS = [
  'ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.',
  'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.',
] as const

export type MonthStatus =
  | {kind: 'bad'; period: SeasonPeriod}
  | {kind: 'good'; period: SeasonPeriod}
  | {kind: 'none'}

/** Resolve a place's season for month `m` (0..11): the first `Bad` period wins, then the first `Good`. */
export function monthStatus(periods: SeasonPeriod[] | undefined, m: number): MonthStatus {
  const list = periods ?? []
  const bad = list.find((p) => p.kind === 'Bad' && p.months.includes(m))
  if (bad) return {kind: 'bad', period: bad}
  const good = list.find((p) => p.kind === 'Good' && p.months.includes(m))
  if (good) return {kind: 'good', period: good}
  return {kind: 'none'}
}

/** 0-based month (0 = January) of a 'yyyy-MM-dd' date, matching useSchedule.dayOfWeek's UTC parse. */
export function monthOfDate(isoDate: string): number {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCMonth()
}

/** Compress a month set into wrap-aware Thai ranges, e.g. [10,11,0,1] → "ม.ค.–ก.พ., พ.ย.–ธ.ค.". */
export function rangeLabel(months: number[]): string {
  const sorted = [...new Set(months)].filter((m) => m >= 0 && m <= 11).sort((a, b) => a - b)
  if (sorted.length === 0) return ''
  const runs: Array<[number, number]> = []
  for (const m of sorted) {
    const last = runs[runs.length - 1]
    if (last && m === last[1] + 1) last[1] = m
    else runs.push([m, m])
  }
  return runs
    .map(([s, e]) => (s === e ? THAI_MONTHS[s] : `${THAI_MONTHS[s]}–${THAI_MONTHS[e]}`))
    .join(', ')
}
