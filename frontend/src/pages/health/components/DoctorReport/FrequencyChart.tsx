import type { DoctorReportDay } from '../../../../shared/api/healthTypes'
import { formatDateShort } from './reportFormat'

/**
 * Bar chart of attack count per day across the report's date range.
 * Sized to roughly match the mock proportions (300x56 viewBox), but
 * scales horizontally inside its container.
 *
 *  - Each bar = one day. Height = (attackCount / max) × max-height.
 *  - Color shifts from amber → red as the count grows.
 *  - Bottom pink band marks period days.
 *  - Three x-axis labels (start, mid, end) — full daily ticks would be
 *    illegible at this width.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "Attacks per day" SVG.
 */
export interface FrequencyChartProps {
  days: DoctorReportDay[]
}

const CHART_W = 300
const CHART_H = 56
const PADDING_TOP = 6

export function FrequencyChart({ days }: FrequencyChartProps) {
  if (days.length === 0) {
    return null
  }

  const max = Math.max(1, ...days.map((d) => d.attackCount))
  const peak = days.reduce((acc, d) => Math.max(acc, d.attackCount), 0)
  // Even-spacing — if days.length=30, each bar gets CHART_W/30 px.
  const slot = CHART_W / days.length
  const barW = Math.max(2, slot * 0.7)

  // Find start, mid, end dates for the x-axis labels.
  const start = days[0]
  const mid = days[Math.floor(days.length / 2)]
  const end = days[days.length - 1]

  return (
    <div className="health-report-chart-row">
      <div className="health-report-chart-title">
        <span>Attacks per day</span>
        <span className="health-report-chart-peak">peak {peak}/วัน</span>
      </div>
      <svg
        viewBox={`0 0 ${CHART_W} ${CHART_H}`}
        width="100%"
        height={CHART_H}
        preserveAspectRatio="none"
        role="img"
        aria-label="Daily attack count"
      >
        {days.map((d, i) => {
          const x = i * slot + (slot - barW) / 2
          const heightAvail = CHART_H - PADDING_TOP - 2 // leave 2px for period band
          const h = d.attackCount === 0 ? 0 : (d.attackCount / max) * heightAvail
          const y = CHART_H - 2 - h
          // Darken when the day is at the maximum to draw the eye.
          const color =
            d.attackCount === 0
              ? 'transparent'
              : d.attackCount === max && max > 1
                ? '#dc2626'
                : '#ef4444'
          return (
            <rect key={d.date} x={x} y={y} width={barW} height={h} rx={1} fill={color} />
          )
        })}
        {/* Period band along the bottom edge — one pink rect per
            contiguous run of period days. */}
        {periodRuns(days).map(([startIdx, endIdx], i) => {
          const x = startIdx * slot
          const w = (endIdx - startIdx + 1) * slot
          return (
            <rect
              key={`period-${i}`}
              x={x}
              y={CHART_H - 2}
              width={w}
              height={2}
              fill="#ec4899"
            />
          )
        })}
      </svg>
      <div className="health-report-chart-axis">
        <span>{formatDateShort(start.date)}</span>
        <span>{formatDateShort(mid.date)}</span>
        <span>{formatDateShort(end.date)}</span>
      </div>
    </div>
  )
}

/** Collapse consecutive period days into [start, end] index runs. */
function periodRuns(days: DoctorReportDay[]): Array<[number, number]> {
  const runs: Array<[number, number]> = []
  let run: [number, number] | null = null
  for (let i = 0; i < days.length; i++) {
    if (days[i].isPeriodDay) {
      if (run) run[1] = i
      else run = [i, i]
    } else if (run) {
      runs.push(run)
      run = null
    }
  }
  if (run) runs.push(run)
  return runs
}
