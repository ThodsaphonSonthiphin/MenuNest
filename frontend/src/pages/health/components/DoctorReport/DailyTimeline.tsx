import type { DoctorReportDay } from '../../../../shared/api/healthTypes'
import { EpisodeReportCard } from './EpisodeReportCard'
import { formatDayHeader } from './reportFormat'

/**
 * Scrollable list of daily cards (newest first). Each day:
 *
 *  - Date header + badges (period / bedrest / failed-Tx / quiet / aura)
 *  - Summary line (attacks count, peak severity, doses)
 *  - One `<EpisodeReportCard />` per attack on that day
 *
 * Days without attacks render as a low-opacity card with just a "quiet"
 * badge so doctors can see complete coverage at a glance (gaps in the
 * timeline could otherwise look like missing data).
 *
 * Episode numbering is shared across the whole report — we sequence
 * attacks chronologically from oldest to newest and display
 * "Attack #N" so the doctor can reference them in the summary.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "🗓️ Detailed timeline" section.
 */
export interface DailyTimelineProps {
  days: DoctorReportDay[]
}

export function DailyTimeline({ days }: DailyTimelineProps) {
  // Number episodes globally in chronological (oldest-first) order, so
  // attack #1 is the earliest in the window even after we render newest
  // days first.
  const epOrder = new Map<string, number>()
  let n = 0
  const chronological = [...days].sort(
    (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime(),
  )
  for (const d of chronological) {
    const sorted = [...d.episodes].sort(
      (a, b) => new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime(),
    )
    for (const ep of sorted) {
      n += 1
      epOrder.set(ep.id, n)
    }
  }

  // Show newest first to match clinician convention.
  const newestFirst = [...days].sort(
    (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime(),
  )

  return (
    <>
      <h2 className="health-report-section-title">🗓️ Detailed timeline (latest first)</h2>
      {newestFirst.map((day) => (
        <DayCard key={day.date} day={day} epOrder={epOrder} />
      ))}
    </>
  )
}

function DayCard({
  day,
  epOrder,
}: {
  day: DoctorReportDay
  epOrder: Map<string, number>
}) {
  const quiet = day.attackCount === 0
  const hasBedrest = day.episodes.some(
    // FunctionalImpact.SevereBedrest = 4
    (ep) => ep.functionalImpact === 4,
  )
  const hasFailedTx = day.noDrugEvents > 0 || day.episodes.some((ep) => ep.noDrugTaken)
  const hasAura = day.episodes.some((ep) => ep.hasAura === true)

  return (
    <div className={`health-report-day-card${quiet ? ' health-report-day-card--quiet' : ''}`}>
      <div className="health-report-day-header">
        <div className="health-report-day-date">📅 {formatDayHeader(day.date)}</div>
        <div className="health-report-day-badges">
          {day.isPeriodDay && (
            <span className="health-report-badge health-report-badge--period">⚭ รอบเดือน</span>
          )}
          {hasBedrest && (
            <span className="health-report-badge health-report-badge--bedrest">🛏️ bedrest</span>
          )}
          {hasFailedTx && (
            <span className="health-report-badge health-report-badge--warning">⚠ failed Tx</span>
          )}
          {hasAura && (
            <span className="health-report-badge health-report-badge--aura">🌀 with aura</span>
          )}
          {quiet && (
            <span className="health-report-badge health-report-badge--quiet">ไม่มี attack</span>
          )}
        </div>
      </div>

      {!quiet && (
        <div className="health-report-day-summary">
          <span>
            🧠 <strong>{day.attackCount}</strong> attack
            {day.attackCount > 1 ? 's' : ''}
          </span>
          <span>
            ⚡ peak <strong>{day.peakSeverity}/10</strong>
          </span>
          <span>
            💊 <strong>{day.doseCount}</strong> dose{day.doseCount === 1 ? '' : 's'}
          </span>
        </div>
      )}

      {[...day.episodes]
        .sort((a, b) => new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime())
        .map((ep) => (
          <EpisodeReportCard
            key={ep.id}
            episode={ep}
            sequenceNumber={epOrder.get(ep.id) ?? 0}
          />
        ))}
    </div>
  )
}
