import { useMemo } from 'react'
import { useListEpisodesQuery } from '../../../shared/api/api'

/**
 * 7-day overview shown on Home: count of attacks per day (Mon→Sun in the
 * mock; we keep it as last-7-rolling for accuracy) plus peak severity
 * across the window. Rendered as inline SVG bars in `HealthHomePage`.
 */
export interface SevenDayBucket {
  /** ISO date `YYYY-MM-DD`. */
  date: string
  /** Thai weekday abbreviation: จ อ พ พฤ ศ ส อา. */
  label: string
  count: number
  peakSeverity: number
}

const THAI_DAYS = ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส']

export interface SevenDayOverview {
  buckets: SevenDayBucket[]
  totalAttacks: number
  peakSeverity: number
  isLoading: boolean
}

function toIsoDate(d: Date): string {
  const yyyy = d.getFullYear()
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${yyyy}-${mm}-${dd}`
}

export function useSevenDayOverview(): SevenDayOverview {
  const { from, to } = useMemo(() => {
    const today = new Date()
    const start = new Date(today)
    start.setDate(start.getDate() - 6) // includes today → 7 days
    return { from: toIsoDate(start), to: toIsoDate(today) }
  }, [])

  const { data, isLoading } = useListEpisodesQuery({ from, to })

  return useMemo<SevenDayOverview>(() => {
    const buckets: SevenDayBucket[] = []
    const today = new Date()
    for (let i = 6; i >= 0; i--) {
      const d = new Date(today)
      d.setDate(d.getDate() - i)
      buckets.push({
        date: toIsoDate(d),
        label: THAI_DAYS[d.getDay()] ?? '',
        count: 0,
        peakSeverity: 0,
      })
    }
    if (!data) {
      return { buckets, totalAttacks: 0, peakSeverity: 0, isLoading }
    }

    let total = 0
    let peak = 0
    for (const ep of data) {
      const day = ep.startedAt.slice(0, 10)
      const bucket = buckets.find((b) => b.date === day)
      if (!bucket) continue
      bucket.count += 1
      bucket.peakSeverity = Math.max(bucket.peakSeverity, ep.severity)
      total += 1
      peak = Math.max(peak, ep.severity)
    }
    return { buckets, totalAttacks: total, peakSeverity: peak, isLoading }
  }, [data, isLoading])
}
