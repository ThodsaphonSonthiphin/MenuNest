import type { DoctorReportDto } from '../../../../shared/api/healthTypes'
import { formatDateRange, formatGeneratedAt, percent } from './reportFormat'

/**
 * Patient header (purple/violet gradient card at the top of the doctor
 * report). Mirrors the mock's:
 *   👤 Patient name (22px bold)
 *   📅 Date range + generated timestamp
 *   🧠 ICD-3 focused-report tag
 *   4-stat grid: attacks / days affected / acute med days / aura %
 *
 * The acute-med-days stat lights up red when the value would trigger a
 * MOH-risk flag (>=10 days/month per ICHD-3 §8.2). We re-derive this
 * locally from `Summary.AcuteMedDays` rather than from the flag list so
 * the visual stays in sync even if the threshold or flag wording changes.
 *
 * Mock: docs/mocks/doctor-report-mock.html (top patient-header block).
 */
export interface ReportHeaderProps {
  report: DoctorReportDto
}

const MOH_THRESHOLD_DAYS = 10

export function ReportHeader({ report }: ReportHeaderProps) {
  const { summary } = report
  const acuteAlert = summary.acuteMedDays >= MOH_THRESHOLD_DAYS
  // The mock prints a short generated-at code (RPT-xxxx) for the doctor
  // to reference. We derive a stable short code from the timestamp so
  // repeat loads of the same report show the same suffix.
  const shortCode = makeShortCode(report.generatedAtUtc)

  return (
    <div className="health-report-card health-report-card--header">
      <div className="health-report-patient-name">👤 {report.patientName}</div>
      <div className="health-report-patient-meta">
        📅 {formatDateRange(report.dateFrom, report.dateTo, report.durationDays)}
        <br />
        Generated {formatGeneratedAt(report.generatedAtUtc)}{' '}
        <code className="health-report-code">{shortCode}</code>
        <div className="health-report-icd-tag">🧠 Focused report: Migraine</div>
      </div>
      <div className="health-report-stats-grid">
        <Stat value={summary.totalAttacks} label={`attacks\n(${report.durationDays} วัน)`} />
        <Stat value={summary.daysAffected} label={`วันที่\nมี attack`} />
        <Stat
          value={`${acuteAlert ? '⚠ ' : ''}${summary.acuteMedDays}`}
          label="acute&#10;med days"
          alert={acuteAlert}
        />
        <Stat value={percent(summary.auraPercentage)} label={`with\naura`} />
      </div>
    </div>
  )
}

function Stat({
  value,
  label,
  alert = false,
}: {
  value: number | string
  label: string
  alert?: boolean
}) {
  return (
    <div className={`health-report-stat${alert ? ' health-report-stat--alert' : ''}`}>
      <div className="health-report-stat-value">{value}</div>
      <div className="health-report-stat-label">
        {label.split('\n').map((line, i) => (
          <span key={i}>
            {line}
            {i < label.split('\n').length - 1 && <br />}
          </span>
        ))}
      </div>
    </div>
  )
}

/**
 * Stable 4-char short code derived from generatedAtUtc. Uses base-32
 * (Crockford-like) to avoid ambiguous characters. Not cryptographic —
 * just for "tell me which report you're looking at" phone calls.
 */
function makeShortCode(iso: string): string {
  const ts = Date.parse(iso) || 0
  const alphabet = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ'
  let n = Math.floor(ts / 1000) // seconds — enough resolution
  let out = ''
  for (let i = 0; i < 4; i++) {
    out = alphabet[n & 31] + out
    n = Math.floor(n / 32)
  }
  return `RPT-${out}`
}
