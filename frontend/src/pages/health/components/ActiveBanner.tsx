import { useNavigate } from 'react-router-dom'
import type { EpisodeDto } from '../../../shared/api/healthTypes'
import { TimerLive } from './TimerLive'

/**
 * Big pulsing pink/red call-to-action shown on Home when at least one
 * episode is active. Whole banner is a button — tap takes the user
 * straight to the Active Episode screen so they can manage / resolve it.
 *
 * Visual brief: docs/mocks/patient-active-episode-mock.html (left phone).
 */
export interface ActiveBannerProps {
  episode: EpisodeDto
}

export function ActiveBanner({ episode }: ActiveBannerProps) {
  const navigate = useNavigate()

  return (
    <button
      type="button"
      className="health-active-banner"
      onClick={() => navigate(`/health/active/${episode.id}`)}
    >
      <div className="health-active-banner__label">
        <span className="health-pulse-dot" />
        Active Now
      </div>
      <div className="health-active-banner__title">⚡ Migraine attack</div>
      <div className="health-active-banner__meta">
        ⏱ <TimerLive startedAt={episode.startedAt} /> • severity {episode.severity}/10
      </div>
      {episode.firstDrugName && episode.intakeCount > 0 && (
        <div className="health-active-banner__meta">
          💊 {episode.firstDrugName}
          {episode.intakeCount > 1 ? ` +${episode.intakeCount - 1}` : ''} ออกฤทธิ์อยู่
        </div>
      )}
      <div className="health-active-banner__cta">จัดการ episode →</div>
    </button>
  )
}
