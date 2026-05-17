import type { DoctorReportFlag } from '../../../../shared/api/healthTypes'

/**
 * Clinical flags banner — the colored alert cards under the patient
 * header. Each flag is one of:
 *   - `danger`  (red, left bar)   — MOH risk, severe disability
 *   - `warning` (amber, left bar) — frequency near chronic, functional impact
 *
 * Renders nothing when the backend produced no flags so the report
 * does not show an empty card with a header.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "⚠️ Clinical flags" card.
 */
export interface ClinicalFlagsProps {
  flags: DoctorReportFlag[]
}

const ICON_FOR_CODE: Record<string, string> = {
  MOH_RISK: '🚨',
  FREQUENCY_NEAR_CHRONIC: '📈',
  FUNCTIONAL_DISABILITY: '🛏️',
}

function iconFor(flag: DoctorReportFlag): string {
  return ICON_FOR_CODE[flag.code] ?? (flag.severity === 'danger' ? '🚨' : '⚠️')
}

export function ClinicalFlags({ flags }: ClinicalFlagsProps) {
  if (flags.length === 0) return null

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">⚠️ Clinical flags</h2>
      <div className="health-report-flags">
        {flags.map((flag, i) => (
          <div
            key={`${flag.code}-${i}`}
            className={`health-report-flag health-report-flag--${
              flag.severity === 'danger' ? 'danger' : 'warning'
            }`}
          >
            <span className="health-report-flag-icon">{iconFor(flag)}</span>
            <div className="health-report-flag-content">
              <strong>{flag.title}</strong>
              <span>{flag.detail}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
