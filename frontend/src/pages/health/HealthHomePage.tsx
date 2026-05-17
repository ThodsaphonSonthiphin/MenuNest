import { useNavigate } from 'react-router-dom'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { ActiveBanner } from './components/ActiveBanner'
import { PushPermissionPrompt } from './components/PushPermissionPrompt'
import { useActiveEpisodes } from './hooks/useActiveEpisodes'
import { useSevenDayOverview } from './hooks/useSevenDayOverview'
import './styles/health.css'

/**
 * Home — the landing surface of the migraine module.
 *
 *  - If any episode is active, a pulsing banner takes priority and the
 *    primary CTA shifts to "manage this attack" (handled inside the
 *    banner button). Otherwise the CTA is "log a new attack".
 *  - "กินยา" goes to TakeMedication for the first active episode, or
 *    falls back to the log flow when there's none (Phase 1: no
 *    drug-picker page yet).
 *  - 7-day mini bars come from the same /api/episodes endpoint used by
 *    History — bucketed client-side.
 *
 * Mock: docs/mocks/patient-active-episode-mock.html (left phone).
 */
export function HealthHomePage() {
  const navigate = useNavigate()
  const { displayName } = useCurrentUser()
  const active = useActiveEpisodes()
  const overview = useSevenDayOverview()

  const firstActive = active.data?.[0]
  const hasActive = !!firstActive

  const initial = (displayName || '?').trim().charAt(0).toUpperCase() || '?'

  const handleTakeMedication = () => {
    if (firstActive) {
      navigate(`/health/take-med/${firstActive.id}`)
    } else {
      // Phase 1: no episode-less drug picker yet — bounce through the
      // log flow so any drug logged becomes part of an episode.
      navigate('/health/log')
    }
  }

  // Mini bars chart: render the 7 buckets as a tiny inline SVG so we
  // don't pull in a chart library for a 50px-tall visual.
  const maxCount = Math.max(1, ...overview.buckets.map((b) => b.count))

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user">
            <div className="health-header__avatar">{initial}</div>
            <span>{displayName || 'You'}</span>
          </div>
          <div className="health-header__icons">
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Settings"
              onClick={() => navigate('/health/settings')}
            >
              ⚙️
            </button>
          </div>
        </header>

        {hasActive ? (
          <ActiveBanner episode={firstActive} />
        ) : (
          <button
            type="button"
            className="health-action-btn health-action-btn--primary health-action-btn--large"
            onClick={() => navigate('/health/log')}
          >
            🤒 มี Migraine Attack
          </button>
        )}

        <button type="button" className="health-action-btn" onClick={handleTakeMedication}>
          💊 กินยา
        </button>

        {hasActive && firstActive.firstDrugName && (
          <>
            <div className="health-section-title">สถานะ</div>
            <div className="health-card">
              <div className="health-status-row">
                <span className="health-status-row__icon">💊</span>
                <span>
                  <strong>{firstActive.firstDrugName}</strong> ที่กินใน episode นี้
                  {firstActive.intakeCount > 1 && ` (+${firstActive.intakeCount - 1} อื่นๆ)`}
                </span>
              </div>
              {firstActive.isOnPeriod && (
                <div className="health-status-row">
                  <span className="health-status-row__icon">⚭</span>
                  <span>
                    <strong>กำลังมีประจำเดือน</strong>
                  </span>
                </div>
              )}
            </div>
          </>
        )}

        <div className="health-section-title">7 วันที่ผ่านมา</div>
        <div className="health-card">
          <svg
            viewBox="0 0 280 50"
            role="img"
            aria-label="7-day attack count"
            style={{ width: '100%', height: 50, display: 'block' }}
          >
            {overview.buckets.map((b, i) => {
              const barW = 28
              const gap = (280 - barW * 7) / 8
              const x = gap + i * (barW + gap)
              const heightPct = b.count === 0 ? 0 : (b.count / maxCount) * 0.9 + 0.1
              const h = Math.max(2, heightPct * 50)
              const y = 50 - h
              return (
                <rect
                  key={b.date}
                  x={x}
                  y={y}
                  width={barW}
                  height={h}
                  rx={3}
                  fill={b.count === 0 ? 'var(--hl-bg-muted)' : 'var(--hl-symptom)'}
                  fillOpacity={b.count === 0 ? 0.35 : 0.85}
                />
              )
            })}
          </svg>
          <div className="health-mini-bar-labels">
            {overview.buckets.map((b) => (
              <div key={b.date} className="health-mini-bar-label">
                {b.label}
              </div>
            ))}
          </div>
          <div style={{ marginTop: 10, fontSize: 13, color: 'var(--hl-text-muted)' }}>
            <strong style={{ color: 'var(--hl-text)' }}>{overview.totalAttacks} attacks</strong>
            {overview.peakSeverity > 0 && ` • peak ${overview.peakSeverity}/10`}
          </div>
        </div>

        <button
          type="button"
          className="health-link-btn"
          onClick={() => navigate('/health/history')}
        >
          📜 ดูประวัติทั้งหมด
        </button>
        <button
          type="button"
          className="health-link-btn"
          onClick={() => navigate('/health/share')}
        >
          🔗 แชร์ให้หมอดู
        </button>
        <button
          type="button"
          className="health-link-btn"
          onClick={() => navigate('/health/drugs')}
        >
          💊 ยา
        </button>
      </div>
      {/* Web-push opt-in. Only offered once the user has at least one
          episode in history — never on a cold first launch. */}
      <PushPermissionPrompt shouldOffer={overview.totalAttacks > 0 || hasActive} />
    </div>
  )
}
