import type { NoDrugEventDto } from '../../../../shared/api/healthTypes'
import { formatDateShort, formatClockTime, noDrugReasonLabel } from './reportFormat'

/**
 * "Treatment failures" card — yellow warning cards listing every event
 * where the patient could not / did not take a drug during an attack.
 *
 * Empty list hides the card entirely (no need to clutter the report
 * with "no failures, all good!").
 *
 * Mock: docs/mocks/doctor-report-mock.html — "⚠️ Treatment failures" card.
 */
export interface TreatmentFailuresProps {
  events: NoDrugEventDto[]
}

export function TreatmentFailures({ events }: TreatmentFailuresProps) {
  if (events.length === 0) return null

  // Sort newest first — same convention the doctor uses for the daily
  // timeline at the bottom of the report.
  const sorted = [...events].sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
  )

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">
        ⚠️ Treatment failures ({events.length} events)
      </h2>
      {sorted.map((evt, i) => {
        const dateLabel = formatDateShort(evt.startedAt.slice(0, 10))
        const timeLabel = formatClockTime(evt.startedAt)
        const reason = noDrugReasonLabel(evt.reason)
        return (
          <div key={`${evt.episodeId}-${i}`} className="health-report-flag health-report-flag--warning">
            <span className="health-report-flag-icon">⚠️</span>
            <div className="health-report-flag-content">
              <strong>
                {dateLabel} {timeLabel} — {evt.symptomName} ({evt.severity}/10)
              </strong>
              <span>ไม่ได้รับการรักษา{reason ? ` — ${reason}` : ''}</span>
            </div>
          </div>
        )
      })}
    </div>
  )
}
