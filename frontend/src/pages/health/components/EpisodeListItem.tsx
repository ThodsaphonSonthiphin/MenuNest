import type { EpisodeDto } from '../../../shared/api/healthTypes'

/**
 * Card row used by the History list. Whole card is a button so a tap on
 * any part navigates to the Episode Detail page. Icon on the left
 * reflects outcome (✓ resolved, ⚠ failed/no-drug, ⚡ ongoing).
 *
 * Mock: docs/mocks/patient-history-mock.html (left phone).
 */
export interface EpisodeListItemProps {
  episode: EpisodeDto
  onClick: (id: string) => void
}

function formatTimeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDurationMinutes(startMs: number, endMs: number): string {
  const diffMin = Math.max(0, Math.floor((endMs - startMs) / 60_000))
  const h = Math.floor(diffMin / 60)
  const m = diffMin % 60
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

type Outcome = 'ongoing' | 'resolved' | 'failed' | 'unknown'

function outcomeFor(episode: EpisodeDto): Outcome {
  if (!episode.endedAt) return 'ongoing'
  // Failed = closed without a drug, OR retro-closed because the user
  // forgot to mark it. Surface both under the warning icon.
  if (episode.noDrugTaken || episode.retroClosed) return 'failed'
  return 'resolved'
}

const OUTCOME_ICON: Record<Outcome, string> = {
  ongoing: '⚡',
  resolved: '✓',
  failed: '⚠',
  unknown: '?',
}

export function EpisodeListItem({ episode, onClick }: EpisodeListItemProps) {
  const outcome = outcomeFor(episode)
  const startMs = new Date(episode.startedAt).getTime()
  const endMs = episode.endedAt ? new Date(episode.endedAt).getTime() : Date.now()
  const duration = formatDurationMinutes(startMs, endMs)
  const durationLabel = episode.endedAt
    ? episode.retroClosed
      ? `${duration} (closed manual)`
      : duration
    : `${duration} (ongoing)`

  return (
    <button
      type="button"
      className="health-ep-card"
      onClick={() => onClick(episode.id)}
    >
      <div className={`health-ep-icon health-ep-icon--${outcome}`}>
        {OUTCOME_ICON[outcome]}
      </div>
      <div className="health-ep-body">
        <div className="health-ep-header-row">
          <span className="health-ep-time">{formatTimeOfDay(episode.startedAt)}</span>
          <span className="health-ep-symptom">{episode.symptomName}</span>
          <span className="health-ep-severity">
            {episode.severity}
            <span className="health-ep-severity__arrow"> → </span>
            <span
              className={
                episode.severityAfter != null
                  ? 'health-ep-severity__end'
                  : 'health-ep-severity__pending'
              }
            >
              {episode.severityAfter ?? '–'}
            </span>
          </span>
        </div>
        <div className="health-ep-meta">
          {episode.firstDrugName && (
            <span className="health-ep-meta-pill health-ep-meta-pill--drug">
              💊 {episode.firstDrugName}
              {episode.intakeCount > 1 ? ` +${episode.intakeCount - 1}` : ''}
            </span>
          )}
          {episode.isOnPeriod && (
            <span className="health-ep-meta-pill health-ep-meta-pill--period">
              ⚭ period
            </span>
          )}
          {episode.noDrugTaken && (
            <span className="health-ep-meta-pill health-ep-meta-pill--failed">
              ⚠ no drug
            </span>
          )}
          <span className="health-ep-meta-duration">⏱ {durationLabel}</span>
        </div>
      </div>
    </button>
  )
}
