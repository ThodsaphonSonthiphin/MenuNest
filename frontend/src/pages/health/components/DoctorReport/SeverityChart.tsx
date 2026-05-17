import type { DoctorReportDay } from '../../../../shared/api/healthTypes'
import { formatDateShort } from './reportFormat'

/**
 * Dot chart of severity per attack across the date range.
 *
 *  - x: when the attack started (normalised across the date span)
 *  - y: severity (0..10, with 10 at top)
 *  - dot color: yellow (mild) → orange (moderate) → red (severe)
 *  - purple ring: episode has aura
 *  - pink ring: episode happened on a period day
 *
 * Mock: docs/mocks/doctor-report-mock.html — "Severity per attack" SVG.
 */
export interface SeverityChartProps {
  days: DoctorReportDay[]
}

const CHART_W = 300
const CHART_H = 60
const X_PAD = 20

interface AttackPoint {
  startedAt: number
  severity: number
  hasAura: boolean
  isOnPeriod: boolean
}

function collectAttacks(days: DoctorReportDay[]): AttackPoint[] {
  const out: AttackPoint[] = []
  for (const d of days) {
    for (const ep of d.episodes) {
      out.push({
        startedAt: new Date(ep.startedAt).getTime(),
        severity: ep.severity,
        hasAura: ep.hasAura === true,
        isOnPeriod: ep.isOnPeriod,
      })
    }
  }
  return out
}

export function SeverityChart({ days }: SeverityChartProps) {
  if (days.length === 0) return null
  const attacks = collectAttacks(days)
  if (attacks.length === 0) {
    return (
      <div className="health-report-chart-row">
        <div className="health-report-chart-title">
          <span>Severity per attack</span>
          <span className="health-report-chart-sub">no attacks recorded</span>
        </div>
      </div>
    )
  }

  // Time axis spans the entire reported date range so the same x maps
  // to the same calendar day across both charts.
  const start = new Date(days[0].date + 'T00:00:00').getTime()
  // end is the last day's end-of-day so attacks late on the final day
  // sit near the right edge.
  const lastDay = days[days.length - 1].date
  const end = new Date(lastDay + 'T23:59:59').getTime()
  const span = Math.max(1, end - start)

  const peak = attacks.reduce((acc, a) => Math.max(acc, a.severity), 0)
  const avg = attacks.reduce((acc, a) => acc + a.severity, 0) / attacks.length

  function xFor(ts: number): number {
    const t = Math.max(start, Math.min(end, ts))
    return X_PAD + ((t - start) / span) * (CHART_W - X_PAD - 4)
  }

  function yFor(sev: number): number {
    // sev 10 → y=4 (near top), sev 0 → y=CHART_H-4 (near bottom)
    const innerH = CHART_H - 8
    return 4 + ((10 - sev) / 10) * innerH
  }

  function fillFor(sev: number): string {
    if (sev >= 8) return '#dc2626'
    if (sev >= 5) return '#ef4444'
    if (sev >= 3) return '#f59e0b'
    return '#fbbf24'
  }

  const mid = days[Math.floor(days.length / 2)]

  return (
    <div className="health-report-chart-row">
      <div className="health-report-chart-title">
        <span>Severity per attack</span>
        <span className="health-report-chart-sub">
          avg {avg.toFixed(1)} • peak {peak}
        </span>
      </div>
      <svg
        viewBox={`0 0 ${CHART_W} ${CHART_H}`}
        width="100%"
        height={CHART_H}
        preserveAspectRatio="none"
        role="img"
        aria-label="Severity per attack"
      >
        {/* Gridlines + y-axis labels */}
        <line
          x1={X_PAD}
          y1={yFor(10)}
          x2={CHART_W}
          y2={yFor(10)}
          stroke="#e5e7eb"
          strokeDasharray="2,3"
        />
        <line
          x1={X_PAD}
          y1={yFor(5)}
          x2={CHART_W}
          y2={yFor(5)}
          stroke="#e5e7eb"
          strokeDasharray="2,3"
        />
        <line
          x1={X_PAD}
          y1={yFor(0)}
          x2={CHART_W}
          y2={yFor(0)}
          stroke="#e5e7eb"
          strokeDasharray="2,3"
        />
        <text x={2} y={yFor(10) + 3} fontSize={7} fill="#64748b">
          10
        </text>
        <text x={2} y={yFor(5) + 3} fontSize={7} fill="#64748b">
          5
        </text>
        <text x={2} y={yFor(0) + 3} fontSize={7} fill="#64748b">
          0
        </text>
        {attacks.map((a, i) => {
          const cx = xFor(a.startedAt)
          const cy = yFor(a.severity)
          const r = a.severity >= 8 ? 3.5 : 3
          // Period takes precedence visually if both rings would apply;
          // we still convey aura with a second outer outline.
          const stroke = a.hasAura ? '#a855f7' : a.isOnPeriod ? '#ec4899' : 'none'
          const strokeWidth = stroke === 'none' ? 0 : 1.5
          return (
            <circle
              key={i}
              cx={cx}
              cy={cy}
              r={r}
              fill={fillFor(a.severity)}
              stroke={stroke}
              strokeWidth={strokeWidth}
            />
          )
        })}
      </svg>
      <div className="health-report-chart-axis">
        <span>{formatDateShort(days[0].date)}</span>
        <span>{formatDateShort(mid.date)}</span>
        <span>{formatDateShort(lastDay)}</span>
      </div>
      <div className="health-report-chart-legend">
        <span style={{ color: '#ec4899' }}>⚭</span> ring = period •{' '}
        <span style={{ color: '#a855f7' }}>●</span> ring = with aura
      </div>
    </div>
  )
}
