import type { DoctorReportDay, DoctorReportSummary } from '../../../../shared/api/healthTypes'

/**
 * Migraine profile — labelled metric rows similar to a lab-results
 * panel. Each row shows label + reference range (where applicable) +
 * value, with the value colorised against the threshold:
 *
 *   - acute med days     warning at 8/mo, danger at ≥10/mo  (ICHD-3 §8.2)
 *   - severe attacks     danger when ≥50% of attacks reach severity 8+
 *   - frequency          warning when 4-14/mo, danger when ≥15/mo  (chronic)
 *
 * Duration range (min/max hours) is *not* in the backend Summary today,
 * so we compute it locally from `Days` — clinicians ask for the range
 * frequently in our user interviews.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "📋 Migraine profile" card.
 */
export interface MigraineProfileProps {
  summary: DoctorReportSummary
  durationDays: number
  days: DoctorReportDay[]
}

type Tone = 'ok' | 'warning' | 'danger' | 'neutral'

function frequencyTone(n: number): Tone {
  if (n >= 15) return 'danger'
  if (n >= 4) return 'warning'
  return 'ok'
}

function acuteMedTone(n: number): Tone {
  if (n >= 10) return 'danger'
  if (n >= 8) return 'warning'
  return 'ok'
}

function severeShareTone(severe: number, total: number): Tone {
  if (total === 0) return 'neutral'
  const share = severe / total
  if (share >= 0.5) return 'danger'
  if (share >= 0.25) return 'warning'
  return 'ok'
}

function bedrestTone(n: number): Tone {
  if (n >= 4) return 'danger'
  if (n >= 1) return 'warning'
  return 'ok'
}

export function MigraineProfile({ summary, durationDays, days }: MigraineProfileProps) {
  const severePct =
    summary.totalAttacks > 0
      ? Math.round((summary.severeAttacksCount / summary.totalAttacks) * 100)
      : 0

  const durationRange = computeDurationRangeHours(days)

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">📋 Migraine profile</h2>
      <MetricRow
        label="Attack frequency"
        refRange="ref: <4 episodic"
        value={`${summary.totalAttacks} / ${durationDays} วัน`}
        tone={frequencyTone(summary.totalAttacks)}
      />
      <MetricRow
        label="Days with attack"
        value={`${summary.daysAffected} / ${durationDays} วัน`}
      />
      <MetricRow
        label="Avg duration / attack"
        value={
          <>
            {summary.averageDurationHours.toFixed(1)} ชม.{' '}
            {durationRange && (
              <span className="health-report-metric-sub">
                (range {durationRange.min.toFixed(1)}-{durationRange.max.toFixed(1)}h)
              </span>
            )}
          </>
        }
      />
      <MetricRow
        label="Avg peak severity"
        value={`${summary.averagePeakSeverity.toFixed(1)} / 10`}
      />
      <MetricRow
        label="Severe attacks (≥8/10)"
        value={`${summary.severeAttacksCount} (${severePct}%)`}
        tone={severeShareTone(summary.severeAttacksCount, summary.totalAttacks)}
      />
      <MetricRow
        label="Acute med days"
        refRange="ref: ≤10/mo"
        value={`${summary.acuteMedDays} / ${durationDays}${
          summary.acuteMedDays >= 10 ? ' ⚠' : ''
        }`}
        tone={acuteMedTone(summary.acuteMedDays)}
      />
      <MetricRow
        label="Days fully disabled (bedrest)"
        value={`${summary.daysFullyDisabled} / ${durationDays}`}
        tone={bedrestTone(summary.daysFullyDisabled)}
      />
      <MetricRow
        label="With aura"
        value={`${summary.attacksWithAura} attacks (${Math.round(summary.auraPercentage)}%)`}
      />
    </div>
  )
}

function MetricRow({
  label,
  refRange,
  value,
  tone = 'neutral',
}: {
  label: string
  refRange?: string
  value: React.ReactNode
  tone?: Tone
}) {
  return (
    <div className="health-report-metric-row">
      <span className="health-report-metric-label">
        {label}
        {refRange && <span className="health-report-metric-ref">{refRange}</span>}
      </span>
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

function computeDurationRangeHours(
  days: DoctorReportDay[],
): { min: number; max: number } | null {
  let min = Number.POSITIVE_INFINITY
  let max = 0
  let any = false
  for (const d of days) {
    for (const ep of d.episodes) {
      if (!ep.endedAt) continue
      const ms = new Date(ep.endedAt).getTime() - new Date(ep.startedAt).getTime()
      if (Number.isNaN(ms) || ms <= 0) continue
      const hrs = ms / 3_600_000
      if (hrs < min) min = hrs
      if (hrs > max) max = hrs
      any = true
    }
  }
  return any ? { min, max } : null
}
