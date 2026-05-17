import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useWebPushSubscription } from './hooks/useWebPushSubscription'
import './styles/health.css'

/**
 * Health Settings — light-touch preferences page.
 *
 *  - Theme toggle: persists in localStorage and applies `body.light`
 *    class so health pages flip palette. Other modules already share
 *    the same convention.
 *  - Web push subscription: delegated entirely to
 *    `useWebPushSubscription`. The UI just shows the right state.
 *  - Period tracking: Phase 1 client-side checkbox saved in
 *    localStorage. The backend doesn't yet persist a per-user setting;
 *    this is on the Phase 2 list.
 *  - Quick links to Drug Master + Share Links so this page is the
 *    canonical hub for non-attack settings.
 *
 *  Export ข้อมูล is a Phase 2 stub (CSV/PDF export of episodes).
 */
const THEME_STORAGE_KEY = 'health-theme'
const PERIOD_TRACK_STORAGE_KEY = 'health-period-tracking'

type Theme = 'dark' | 'light'

function readStoredTheme(): Theme {
  if (typeof window === 'undefined') return 'dark'
  const v = window.localStorage.getItem(THEME_STORAGE_KEY)
  return v === 'light' ? 'light' : 'dark'
}

function readPeriodTracking(): boolean {
  if (typeof window === 'undefined') return false
  return window.localStorage.getItem(PERIOD_TRACK_STORAGE_KEY) === '1'
}

export function HealthSettingsPage() {
  const navigate = useNavigate()
  const [theme, setTheme] = useState<Theme>(readStoredTheme())
  const [periodTracking, setPeriodTracking] = useState<boolean>(
    readPeriodTracking(),
  )
  const push = useWebPushSubscription()

  // Apply / persist theme — keep this in sync with the body class so
  // dark mode survives across navigations.
  useEffect(() => {
    document.body.classList.toggle('light', theme === 'light')
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])

  useEffect(() => {
    window.localStorage.setItem(
      PERIOD_TRACK_STORAGE_KEY,
      periodTracking ? '1' : '0',
    )
  }, [periodTracking])

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 15 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate('/health')}
            >
              ←
            </button>
            <span>Settings</span>
          </div>
        </header>

        {/* Theme */}
        <div className="health-section-title">🎨 ธีม</div>
        <div className="health-settings-row">
          <div>
            <div className="health-settings-row__title">โหมดสี</div>
            <div className="health-settings-row__sub">
              Dark mode = สบายตา / photophobia-friendly
            </div>
          </div>
          <div className="health-segmented">
            <button
              type="button"
              className={`health-segmented__btn${
                theme === 'dark' ? ' health-segmented__btn--active' : ''
              }`}
              onClick={() => setTheme('dark')}
            >
              🌙 Dark
            </button>
            <button
              type="button"
              className={`health-segmented__btn${
                theme === 'light' ? ' health-segmented__btn--active' : ''
              }`}
              onClick={() => setTheme('light')}
            >
              ☀️ Light
            </button>
          </div>
        </div>

        {/* Push notifications */}
        <div className="health-section-title">🔔 การแจ้งเตือน</div>
        {!push.isSupported ? (
          <div className="health-card" style={{ fontSize: 13, color: 'var(--hl-text-muted)' }}>
            ⚠ Browser ของคุณไม่รองรับ web push (เช่น Safari ที่ไม่ได้ติดตั้งเป็น PWA)
          </div>
        ) : (
          <div className="health-settings-row">
            <div>
              <div className="health-settings-row__title">
                Follow-up reminders
              </div>
              <div className="health-settings-row__sub">
                ให้ระบบเตือนถามอาการหลังกินยา 1/2/4 ชั่วโมง
              </div>
              {push.error && (
                <div style={{ marginTop: 4, fontSize: 12, color: 'var(--hl-danger)' }}>
                  ⚠ {push.error}
                </div>
              )}
            </div>
            {push.isSubscribed ? (
              <button
                type="button"
                className="health-action-btn health-action-btn--danger"
                onClick={() => push.unsubscribe()}
                disabled={push.isLoading}
              >
                {push.isLoading ? '...' : 'ปิดการแจ้งเตือน'}
              </button>
            ) : (
              <button
                type="button"
                className="health-action-btn health-action-btn--primary"
                onClick={() => push.subscribe()}
                disabled={push.isLoading}
              >
                {push.isLoading ? '...' : '🔔 เปิด'}
              </button>
            )}
          </div>
        )}

        {/* Period tracking — Phase 1 client-side only */}
        <div className="health-section-title">⚭ การติดตามรอบเดือน</div>
        <button
          type="button"
          className={`health-check-row${
            periodTracking ? ' health-check-row--checked' : ''
          }`}
          onClick={() => setPeriodTracking(!periodTracking)}
          aria-pressed={periodTracking}
        >
          <div className="health-check-row__box">
            {periodTracking ? '✓' : ''}
          </div>
          <div>
            <div style={{ fontWeight: 600, fontSize: 14 }}>
              ติดตามรอบเดือน
            </div>
            <div style={{ fontSize: 11, color: 'var(--hl-text-muted)' }}>
              เปิดถ้าต้องการ pattern analysis เกี่ยวกับประจำเดือน
              (Phase 1: บันทึกใน browser; sync server = Phase 2)
            </div>
          </div>
        </button>

        {/* Shortcuts */}
        <div className="health-section-title">📋 ทางลัด</div>
        <button
          type="button"
          className="health-action-btn-detail"
          style={{ width: '100%', marginBottom: 8, justifyContent: 'flex-start' }}
          onClick={() => navigate('/health/drugs')}
        >
          💊 ยาทั้งหมด
        </button>
        <button
          type="button"
          className="health-action-btn-detail"
          style={{ width: '100%', marginBottom: 8, justifyContent: 'flex-start' }}
          onClick={() => navigate('/health/share')}
        >
          🔗 Share links ของฉัน
        </button>

        {/* Export — Phase 2 */}
        <div className="health-section-title">💾 ส่งออกข้อมูล</div>
        <button
          type="button"
          className="health-action-btn-detail"
          style={{ width: '100%', justifyContent: 'flex-start', opacity: 0.6 }}
          disabled
          title="Phase 2"
        >
          📥 Export episodes เป็น CSV (เร็วๆ นี้)
        </button>
      </div>
    </div>
  )
}
