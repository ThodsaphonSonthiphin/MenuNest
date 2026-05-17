import type { DoctorReportPatterns } from '../../../../shared/api/healthTypes'
import { ONSET_BUCKET_ORDER, dayOfWeekToMondayFirst, onsetBucketLabel } from './reportFormat'

/**
 * Pattern analysis card with three sub-blocks:
 *
 *  1. Onset time: 4 colored boxes for morning / afternoon / evening /
 *     night with the count of attacks that started in each window.
 *  2. Day of week: 7-bar mini chart (Mon-first).
 *  3. Menstrual correlation: 4 metric rows + rate-ratio callout that
 *     fires when the during-period rate exceeds the outside rate by
 *     >=2× (the threshold used by ICHD-3 §A1.1.2 for menstrual migraine).
 *
 * Mock: docs/mocks/doctor-report-mock.html — "🔍 Pattern analysis" card.
 */
export interface PatternAnalysisProps {
  patterns: DoctorReportPatterns
  totalAttacks: number
}

const ONSET_STYLES: Record<string, { bg: string; fg: string }> = {
  morning: { bg: 'var(--hl-rpt-symptom-soft)', fg: 'var(--hl-rpt-symptom)' },
  afternoon: { bg: 'var(--hl-rpt-warning-soft)', fg: 'var(--hl-rpt-warning)' },
  evening: { bg: 'var(--hl-rpt-primary-soft)', fg: 'var(--hl-rpt-primary)' },
  night: { bg: 'var(--hl-rpt-muted-soft)', fg: 'var(--hl-rpt-muted)' },
}

export function PatternAnalysis({ patterns, totalAttacks }: PatternAnalysisProps) {
  const dowBars = dayOfWeekToMondayFirst(patterns.dayOfWeekCounts)
  const maxDow = Math.max(1, ...dowBars.map((b) => b.count))
  const peakIdx = dowBars.findIndex((b) => b.count === maxDow && maxDow > 0)
  const peakName =
    peakIdx >= 0
      ? ['จันทร์', 'อังคาร', 'พุธ', 'พฤหัสบดี', 'ศุกร์', 'เสาร์', 'อาทิตย์'][peakIdx]
      : null

  const rateRatio =
    patterns.attackRateOutsidePeriod > 0
      ? patterns.attackRateDuringPeriod / patterns.attackRateOutsidePeriod
      : null

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">🔍 Pattern analysis</h2>

      <div className="health-report-sub-h">Onset time</div>
      <div className="health-report-onset-grid">
        {ONSET_BUCKET_ORDER.map((key) => {
          const count = patterns.onsetTimeBuckets[key] ?? 0
          const style = ONSET_STYLES[key]
          return (
            <div
              key={key}
              className="health-report-onset-cell"
              style={{ background: style.bg }}
            >
              <div className="health-report-onset-cell-value" style={{ color: style.fg }}>
                {count}
              </div>
              <div className="health-report-onset-cell-label">{onsetBucketLabel(key)}</div>
            </div>
          )
        })}
      </div>

      <div className="health-report-sub-h">Day of week</div>
      <div className="health-report-dow-chart">
        {dowBars.map((b, i) => {
          const hPct = maxDow === 0 ? 0 : (b.count / maxDow) * 100
          return (
            <div key={i} className="health-report-dow-col">
              <div className="health-report-dow-bar" style={{ height: `${hPct}%` }} />
              <div className="health-report-dow-label">{b.label}</div>
            </div>
          )
        })}
      </div>
      {peakName && totalAttacks > 0 && (
        <div className="health-report-dow-caption">
          Peak: วัน{peakName} ({maxDow} attack{maxDow > 1 ? 's' : ''})
        </div>
      )}

      <div className="health-report-sub-h">Menstrual correlation</div>
      <Row
        label="Attacks during period"
        value={`${patterns.attacksDuringPeriod} attacks`}
        tone={patterns.attacksDuringPeriod > 0 ? 'danger' : 'neutral'}
      />
      <Row
        label="Attacks outside period"
        value={`${patterns.attacksOutsidePeriod} attacks`}
      />
      <Row
        label="Attack rate during period"
        value={`${patterns.attackRateDuringPeriod.toFixed(2)} / วัน`}
        tone={patterns.attackRateDuringPeriod > patterns.attackRateOutsidePeriod ? 'danger' : 'neutral'}
      />
      <Row
        label="Attack rate outside period"
        value={`${patterns.attackRateOutsidePeriod.toFixed(2)} / วัน`}
      />
      {rateRatio !== null && rateRatio >= 2 && (
        <div className="health-report-menstrual-callout">
          ⚠️ Menstrual attacks เพิ่มขึ้น <strong>{rateRatio.toFixed(1)}×</strong>{' '}
          ของช่วงปกติ — เข้าเกณฑ์ menstrual migraine
        </div>
      )}
    </div>
  )
}

function Row({
  label,
  value,
  tone = 'neutral',
}: {
  label: string
  value: string
  tone?: 'danger' | 'warning' | 'neutral'
}) {
  return (
    <div className="health-report-metric-row">
      <span className="health-report-metric-label">{label}</span>
      <span
        className={`health-report-metric-value${
          tone !== 'neutral' ? ` health-report-metric-value--${tone}` : ''
        }`}
      >
        {value}
      </span>
    </div>
  )
}
