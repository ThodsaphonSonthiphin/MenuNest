import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { SeveritySlider } from './components/SeveritySlider'
import { TimerLive } from './components/TimerLive'
import { useActiveEpisode } from './hooks/useActiveEpisode'
import { useResolveEpisode } from './hooks/useResolveEpisode'
import { useUpdateEpisodeSeverity } from './hooks/useUpdateEpisodeSeverity'
import {
  AssociatedSymptom,
  SymptomLocation,
  SymptomQuality,
} from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Active Episode — full-screen control surface while an attack is
 * ongoing. The page polls every 30s so the elapsed timer / drug
 * progress / next follow-up countdown stay current.
 *
 *  - Big "หายแล้ว" CTA resolves the episode and bounces back to
 *    History so the user gets immediate closure.
 *  - "กินยาเพิ่ม" links into Task 14b's TakeMedication flow.
 *  - The severity update modal reuses `SeveritySlider` so the visual
 *    is consistent with Quick Log.
 *
 * Mock: docs/mocks/patient-active-episode-mock.html (right phone).
 */
const LOCATION_LABEL: Record<SymptomLocation, string> = {
  [SymptomLocation.Left]: '📍 ซ้าย',
  [SymptomLocation.Right]: '📍 ขวา',
  [SymptomLocation.Bilateral]: '📍 ทั้งสอง',
  [SymptomLocation.Frontal]: '📍 Frontal',
  [SymptomLocation.Temporal]: '📍 Temporal',
  [SymptomLocation.Occipital]: '📍 Occipital',
}

const QUALITY_LABEL: Record<SymptomQuality, string> = {
  [SymptomQuality.Throbbing]: '💢 ตุบๆ',
  [SymptomQuality.Pressure]: '💢 บีบ',
  [SymptomQuality.Stabbing]: '💢 แทง',
  [SymptomQuality.Burning]: '💢 แสบ',
}

const ASSOC_LABEL: Record<AssociatedSymptom, string> = {
  [AssociatedSymptom.Nausea]: '🤢 คลื่นไส้',
  [AssociatedSymptom.Vomiting]: '🤮 อาเจียน',
  [AssociatedSymptom.Photophobia]: '💡 กลัวแสง',
  [AssociatedSymptom.Phonophobia]: '🔊 กลัวเสียง',
  [AssociatedSymptom.Osmophobia]: '👃 กลัวกลิ่น',
}

function formatTimeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function ActiveEpisodePage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: episode, isLoading, error } = useActiveEpisode(id)
  const resolve = useResolveEpisode()
  const update = useUpdateEpisodeSeverity()

  const [showSeverityModal, setShowSeverityModal] = useState(false)
  const [pendingSeverity, setPendingSeverity] = useState<number>(0)

  if (isLoading || !episode) {
    return (
      <div className="health-page">
        <div className="health-page__container">
          <div style={{ padding: 32, color: 'var(--hl-text-muted)' }}>
            {error ? 'ไม่พบ episode' : 'กำลังโหลด episode...'}
          </div>
        </div>
      </div>
    )
  }

  const isResolved = !!episode.endedAt
  const startedTime = formatTimeOfDay(episode.startedAt)

  // Next pending follow-up (soonest scheduledAt that hasn't been
  // answered yet). PingStatus 1=Pending, 2=Asked.
  const nextPing = [...(episode.followUps ?? [])]
    .filter((f) => f.status === 1 || f.status === 2)
    .sort(
      (a, b) =>
        new Date(a.scheduledAt).getTime() - new Date(b.scheduledAt).getTime(),
    )[0]

  const minutesUntilPing = nextPing
    ? Math.max(
        0,
        Math.round(
          (new Date(nextPing.scheduledAt).getTime() - Date.now()) / 60_000,
        ),
      )
    : null

  const openSeverityModal = () => {
    setPendingSeverity(episode.severity)
    setShowSeverityModal(true)
  }

  const submitSeverityUpdate = async () => {
    if (!id) return
    await update.updateSeverity(id, pendingSeverity)
    setShowSeverityModal(false)
  }

  const handleResolve = async () => {
    if (!id) return
    await resolve.resolveEpisode(id, 0)
    // Give the toast a moment to render before navigating away.
    window.setTimeout(() => navigate('/health/history'), 600)
  }

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 14 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate('/health')}
            >
              ←
            </button>
            <span>{isResolved ? 'Resolved Attack' : 'Active Attack'}</span>
          </div>
        </header>

        {/* Status card */}
        <div className="health-status-card">
          <div className="health-status-card__row">
            <span className="health-status-tag">
              {!isResolved && <span className="health-pulse-dot" />}
              {isResolved ? 'Resolved' : 'Ongoing'}
            </span>
            <span style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
              started {startedTime}
            </span>
          </div>
          <div className="health-timer">
            ⏱ Episode duration:{' '}
            <span className="health-timer__value">
              <TimerLive startedAt={episode.startedAt} />
            </span>
          </div>

          <div className="health-severity-display--active">
            <div className="health-severity-label">ตอนนี้ปวด</div>
            <div>
              <span className="health-severity-value">{episode.severity}</span>
              <span className="health-severity-value__sub">/10</span>
            </div>
            {!isResolved && (
              <button
                type="button"
                className="health-severity-update"
                onClick={openSeverityModal}
              >
                ▼ update severity
              </button>
            )}
          </div>
        </div>

        {/* Attribute pills */}
        <div className="health-section-title">attributes</div>
        <div className="health-card">
          <div className="health-attr-list">
            {episode.location && (
              <span className="health-attr-pill health-attr-pill--side">
                {LOCATION_LABEL[episode.location]}
              </span>
            )}
            {episode.quality && (
              <span className="health-attr-pill health-attr-pill--quality">
                {QUALITY_LABEL[episode.quality]}
              </span>
            )}
            {episode.isOnPeriod && (
              <span className="health-attr-pill health-attr-pill--period">⚭ ประจำเดือน</span>
            )}
            {episode.associatedSymptoms?.map((s) => (
              <span key={s} className="health-attr-pill health-attr-pill--assoc">
                {ASSOC_LABEL[s]}
              </span>
            ))}
            {!episode.location &&
              !episode.quality &&
              !episode.isOnPeriod &&
              (!episode.associatedSymptoms || episode.associatedSymptoms.length === 0) && (
                <span style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
                  ยังไม่มี attributes — ดูที่ episode detail เพื่อแก้ไข
                </span>
              )}
          </div>
          <button
            type="button"
            className="health-edit-link"
            onClick={() => navigate(`/health/episode/${id}`)}
          >
            ✏️ ดู / แก้ไข attributes
          </button>
        </div>

        {/* Intakes */}
        <div className="health-section-title">ยาที่กินใน episode นี้</div>
        <div className="health-card">
          {episode.intakes && episode.intakes.length > 0 ? (
            episode.intakes.map((intake) => (
              <div key={intake.id} className="health-drug-card">
                <div className="health-drug-card__name">
                  <span>
                    💊 {intake.drugName} {intake.doseStrength}
                    {intake.doseAmount > 1 ? ` ×${intake.doseAmount}` : ''}
                  </span>
                  <span className="health-drug-card__time">
                    {formatTimeOfDay(intake.takenAt)}
                  </span>
                </div>
                <div className="health-drug-card__sub">
                  ✅ บันทึกเวลากินยาแล้ว
                </div>
              </div>
            ))
          ) : (
            <div style={{ fontSize: 13, color: 'var(--hl-text-muted)' }}>
              ยังไม่ได้กินยาใน episode นี้
            </div>
          )}
        </div>

        {/* Follow-up */}
        {nextPing && !isResolved && (
          <div className="health-followup-card">
            <div className="health-followup-info">
              ⏰ ระบบจะถามอีกใน <strong>{minutesUntilPing} นาที</strong>
              <div className="health-followup-info__sub">
                {formatTimeOfDay(nextPing.scheduledAt)}
              </div>
            </div>
          </div>
        )}

        {/* Actions */}
        {!isResolved && (
          <>
            <div className="health-section-title">Actions</div>
            <button
              type="button"
              className="health-action-btn health-action-btn--green health-action-btn--large"
              onClick={handleResolve}
              disabled={resolve.isLoading}
            >
              ✅ {resolve.isLoading ? 'กำลังบันทึก...' : 'หายแล้ว'}
            </button>
            <button
              type="button"
              className="health-action-btn"
              onClick={() => navigate(`/health/take-med/${id}`)}
            >
              💊 กินยาเพิ่ม
            </button>
            <button
              type="button"
              className="health-action-btn health-action-btn--danger"
              onClick={openSeverityModal}
            >
              📈 แย่ลง / update severity
            </button>
          </>
        )}

        <button
          type="button"
          className="health-link-btn"
          onClick={() => navigate(`/health/episode/${id}`)}
        >
          📜 ดู timeline ของ episode
        </button>

        {/* Severity update modal */}
        {showSeverityModal && (
          <div
            className="health-modal-backdrop"
            onClick={() => setShowSeverityModal(false)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="Update severity"
            >
              <div className="health-modal__title">อัปเดต severity</div>
              <SeveritySlider
                value={pendingSeverity}
                onChange={setPendingSeverity}
                min={1}
                max={10}
                bare
              />
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setShowSeverityModal(false)}
                >
                  ยกเลิก
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--primary"
                  onClick={submitSeverityUpdate}
                  disabled={update.isLoading}
                >
                  {update.isLoading ? 'กำลังบันทึก...' : 'บันทึก'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Toasts */}
        {resolve.toast && <div className="health-toast">{resolve.toast}</div>}
        {update.toast && <div className="health-toast">{update.toast}</div>}
      </div>
    </div>
  )
}
