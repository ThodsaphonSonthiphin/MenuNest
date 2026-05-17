import type { DoctorReportEpisode } from '../../../../shared/api/healthTypes'
import { PingResponse } from '../../../../shared/api/healthTypes'
import {
  associatedSymptomIcon,
  associatedSymptomLabel,
  episodeOutcome,
  formatClockTime,
  formatDuration,
  functionalImpactLabel,
  noDrugReasonLabel,
  symptomLocationLabel,
  symptomQualityLabel,
} from './reportFormat'

/**
 * Single migraine attack block rendered inside DailyTimeline. Layout:
 *
 *   1. Header — episode number + symptom name + outcome status
 *   2. Attribute pills — side / quality / period / associated symptoms / impact
 *   3. Event list — symptom start → drug intakes → follow-up responses,
 *      with timestamps in the viewer's local zone
 *   4. Bottom banner — colored summary (resolved / no-drug / ongoing)
 *      with the episode duration on the right
 *
 * The episode number is sequenced from the parent (DailyTimeline) so
 * the doctor sees "#1, #2, #3..." in chronological order across the
 * report.
 *
 * Mock: docs/mocks/doctor-report-mock.html — `.episode` blocks.
 */
export interface EpisodeReportCardProps {
  episode: DoctorReportEpisode
  sequenceNumber: number
}

export function EpisodeReportCard({ episode, sequenceNumber }: EpisodeReportCardProps) {
  const outcome = episodeOutcome(episode)

  const sideLabel = symptomLocationLabel(episode.location)
  const qualityLabel = symptomQualityLabel(episode.quality)
  const impactLabel = functionalImpactLabel(episode.functionalImpact)
  const auraOn = episode.hasAura === true

  const duration = formatDuration(episode.startedAt, episode.endedAt)
  const durationLabel = episode.endedAt ? duration : `${duration}+ (ongoing)`

  const headerLabel = auraOn ? 'Migraine with aura' : 'Migraine attack'

  let statusLabel: string
  if (outcome === 'resolved') {
    const firstDrug = episode.intakes[0]?.drugName
    statusLabel = firstDrug ? `✅ resolved (${firstDrug})` : '✅ resolved'
  } else if (outcome === 'no-drug-warning') {
    statusLabel = '⚠️ failed treatment'
  } else {
    statusLabel = '🔄 ongoing'
  }

  return (
    <div className={`health-report-episode health-report-episode--${outcome}`}>
      <div className="health-report-episode-header">
        <span className="health-report-episode-num">
          <span className="health-report-episode-pill">#{sequenceNumber}</span>
          {headerLabel}
        </span>
        <span className="health-report-episode-status">{statusLabel}</span>
      </div>

      <div className="health-report-attr-row">
        {auraOn && <span className="health-report-attr health-report-attr--aura">🌀 visual aura</span>}
        {sideLabel && (
          <span className="health-report-attr health-report-attr--side">
            📍 {sideLabel}
          </span>
        )}
        {qualityLabel && (
          <span className="health-report-attr health-report-attr--quality">
            💢 {qualityLabel}
          </span>
        )}
        {episode.isOnPeriod && (
          <span className="health-report-attr health-report-attr--period">⚭ period</span>
        )}
        {episode.associatedSymptoms.map((sym) => (
          <span key={sym} className="health-report-attr health-report-attr--assoc">
            {associatedSymptomIcon(sym)} {associatedSymptomLabel(sym)}
          </span>
        ))}
        {impactLabel && (
          <span className="health-report-attr health-report-attr--impact">
            🛏 {impactLabel}
          </span>
        )}
      </div>

      <div className="health-report-event-list">
        <Event kind="symptom" time={episode.startedAt} icon="🤒">
          {headerLabel}{' '}
          <span className="health-report-severity-pill">{episode.severity}/10</span>
        </Event>

        {episode.noDrugTaken && (
          <Event kind="no-drug" time={episode.startedAt} icon="⚠️">
            <strong>ไม่มียาให้กิน</strong>
            {episode.noDrugReasonCode != null && (
              <div className="health-report-event-meta">
                {noDrugReasonLabel(episode.noDrugReasonCode)}
              </div>
            )}
          </Event>
        )}

        {episode.intakes.map((intake, i) => (
          <Event key={`intake-${i}`} kind="drug" time={intake.takenAt} icon="💊">
            {intake.drugName}
            {intake.doseAmount > 1 ? ` × ${intake.doseAmount}` : ''}
          </Event>
        ))}

        {episode.followUps
          .filter((fu) => fu.respondedAt && fu.severityAtCheck != null)
          .map((fu, i) => {
            const sev = fu.severityAtCheck ?? 0
            const pillCls =
              sev === 0
                ? 'health-report-severity-pill health-report-severity-pill--zero'
                : sev <= 3
                  ? 'health-report-severity-pill health-report-severity-pill--low'
                  : 'health-report-severity-pill'
            const verdict =
              fu.response === PingResponse.Resolved
                ? 'หายแล้ว'
                : fu.response === PingResponse.Improved
                  ? 'ดีขึ้น'
                  : fu.response === PingResponse.Same
                    ? 'ยังเหมือนเดิม'
                    : fu.response === PingResponse.Worse
                      ? 'แย่ลง'
                      : 'follow-up'
            return (
              <Event
                key={`fu-${i}`}
                kind="followup"
                time={fu.respondedAt as string}
                icon="📊"
              >
                {verdict} <span className={pillCls}>{sev}/10</span>
              </Event>
            )
          })}
      </div>

      <div className={`health-report-episode-close health-report-episode-close--${outcome}`}>
        <span>
          <span className="health-report-episode-check">
            {outcome === 'resolved' ? '✅' : outcome === 'no-drug-warning' ? '⚠️' : '🔄'}
          </span>{' '}
          Attack #{sequenceNumber} —{' '}
          {outcome === 'resolved'
            ? 'resolved'
            : outcome === 'no-drug-warning'
              ? 'no acute options'
              : 'still active'}
        </span>
        <span className="health-report-episode-duration">{durationLabel}</span>
      </div>
    </div>
  )
}

function Event({
  kind,
  time,
  icon,
  children,
}: {
  kind: 'symptom' | 'drug' | 'followup' | 'no-drug' | 'aura'
  time: string
  icon: string
  children: React.ReactNode
}) {
  return (
    <div className={`health-report-event health-report-event--${kind}`}>
      <span className="health-report-event-time">{formatClockTime(time)}</span>
      <span className="health-report-event-icon">{icon}</span>
      <div className="health-report-event-text">{children}</div>
    </div>
  )
}
