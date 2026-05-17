import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { DrugCard } from './components/DrugCard'
import { useTakeMedicationContext } from './hooks/useTakeMedicationContext'
import { useLogIntake } from './hooks/useLogIntake'
import { useLogNoDrug } from './hooks/useLogNoDrug'
import { NoDrugReason } from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Take Medication — pick what to take during an active attack.
 *
 *  - 3 categories of drugs (active in effect / takeable / blocked) so the
 *    user knows at a glance what's safe to take right now. The lists come
 *    from the backend `/take-medication-context` endpoint which already
 *    applies max-daily-dose + still-active checks.
 *  - "ไม่กินยา" fallback opens a small native <dialog> with reason picker
 *    so the user can log a no-drug event without leaving the page.
 *  - After any save we bounce back to Active Episode (handled inside the
 *    `useLogIntake` / `useLogNoDrug` hooks). The active screen is the
 *    canonical hub for an in-progress attack.
 *
 * Mock: docs/mocks/patient-take-medication-mock.html.
 */
const NO_DRUG_REASON_OPTIONS: { value: NoDrugReason; label: string }[] = [
  { value: NoDrugReason.MaxDoseReached, label: 'เกิน max ต่อวัน' },
  { value: NoDrugReason.AllDrugsActive, label: 'ยายังออกฤทธิ์อยู่ทั้งหมด' },
  { value: NoDrugReason.OutOfStock, label: 'ยาหมด' },
  { value: NoDrugReason.NoDrugTreatsThis, label: 'ไม่มียาที่รักษาอาการนี้' },
  { value: NoDrugReason.UserSkip, label: 'เลือกไม่กิน' },
]

export function TakeMedicationPage() {
  const { episodeId } = useParams<{ episodeId: string }>()
  const navigate = useNavigate()

  const { data: ctx, isLoading, error } = useTakeMedicationContext(episodeId)
  // We stay on the page after a single dose so the user can take a
  // second drug if needed; the back-button on header returns to active.
  const intake = useLogIntake({ navigateBack: true })
  const noDrug = useLogNoDrug()

  const [showNoDrugModal, setShowNoDrugModal] = useState(false)
  const [pendingReason, setPendingReason] = useState<NoDrugReason>(
    NoDrugReason.UserSkip,
  )

  // Optional client-side filter across all 3 drug buckets. We keep this
  // as a controlled <input> rather than the Syncfusion AutoComplete
  // because the lists are short (typically <10 items) and we want to
  // filter rather than pick — Syncfusion's full popup machinery would be
  // a poor UX fit here. Promote to the wrapper later if drug counts grow.
  const [searchTerm, setSearchTerm] = useState('')
  const normalizedSearch = searchTerm.trim().toLowerCase()
  const matchesSearch = (drugName: string): boolean =>
    !normalizedSearch || drugName.toLowerCase().includes(normalizedSearch)

  if (!episodeId) {
    return (
      <div className="health-page">
        <div className="health-page__container">
          <div style={{ padding: 32, color: 'var(--hl-text-muted)' }}>
            ไม่พบ episode id
          </div>
        </div>
      </div>
    )
  }

  if (isLoading || !ctx) {
    return (
      <div className="health-page">
        <div className="health-page__container">
          <div style={{ padding: 32, color: 'var(--hl-text-muted)' }}>
            {error ? 'ไม่พบข้อมูล take medication context' : 'กำลังโหลด...'}
          </div>
        </div>
      </div>
    )
  }

  const handleTake = async (drugId: string, doseAmount: number, displayName: string) => {
    await intake.logIntake(
      {
        drugId,
        doseAmount,
        symptomEpisodeId: episodeId,
      },
      displayName,
    )
  }

  const handleConfirmNoDrug = async () => {
    setShowNoDrugModal(false)
    await noDrug.logNoDrug(episodeId, pendingReason)
  }

  const otherDrugsCount =
    ctx.activeDrugs.length + ctx.takeableDrugs.length + ctx.blockedDrugs.length
  // Phase 1: we don't pre-fetch all-drugs minus the three buckets, so
  // the expand stub just links to the Drug Master page.

  const filteredActive = ctx.activeDrugs.filter((d) => matchesSearch(d.drugName))
  const filteredTakeable = ctx.takeableDrugs.filter((d) =>
    matchesSearch(d.drugName),
  )
  const filteredBlocked = ctx.blockedDrugs.filter((d) =>
    matchesSearch(d.drugName),
  )
  const totalShown =
    filteredActive.length + filteredTakeable.length + filteredBlocked.length

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 15 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate(`/health/active/${episodeId}`)}
            >
              ←
            </button>
            <span>กินยา</span>
          </div>
        </header>

        {/* Episode context card */}
        <div className="health-episode-context">
          <div className="health-episode-context__info">
            <div className="health-episode-context__label">อาการตอนนี้</div>
            <div className="health-episode-context__value">
              {ctx.symptomName ?? '—'}{' '}
              {ctx.currentSeverity != null && (
                <span className="health-episode-context__severity">
                  {ctx.currentSeverity}/10
                </span>
              )}
            </div>
          </div>
          <button
            type="button"
            className="health-episode-context__change"
            disabled
            title="แก้ไข episode (Phase 2)"
          >
            เปลี่ยน
          </button>
        </div>

        {/* Drug search — Phase 1 keeps this as a plain <input> because the
            list is short (typically <10 drugs) and filtering across three
            buckets is the desired UX, not single-pick autocompletion. */}
        {otherDrugsCount > 1 && (
          <div className="health-search-row">
            <input
              type="search"
              className="health-search-input"
              placeholder="🔎 ค้นหายาในลิสต์..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              aria-label="ค้นหายา"
            />
          </div>
        )}
        {normalizedSearch && totalShown === 0 && (
          <div
            className="health-card"
            style={{ fontSize: 12, color: 'var(--hl-text-muted)', marginTop: 8 }}
          >
            ไม่พบยาที่ตรงกับ "{searchTerm}"
          </div>
        )}

        {/* Active drugs */}
        {filteredActive.length > 0 && (
          <>
            <div className="health-section-title health-section-title--active">
              ⏳ ยาที่ยังออกฤทธิ์อยู่
            </div>
            {filteredActive.map((d) => (
              <DrugCard
                key={d.drugId}
                variant="active"
                drugName={d.drugName}
                doseStrength={d.doseStrength}
                lastTakenAt={d.lastTakenAt}
                effectEndsAt={d.effectEndsAt}
                remainingMinutes={d.remainingMinutes}
                progressPct={d.progressPct}
              />
            ))}
          </>
        )}

        {/* Takeable drugs */}
        {(!normalizedSearch || filteredTakeable.length > 0) && (
          <>
            <div className="health-section-title health-section-title--takeable">
              🎯 กินเพิ่มได้
              {ctx.symptomName ? ` (รักษา${ctx.symptomName})` : ''}
            </div>
            {filteredTakeable.length === 0 ? (
              <div className="health-card" style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
                ไม่มียาให้กินเพิ่มตอนนี้
              </div>
            ) : (
              filteredTakeable.map((d) => (
                <DrugCard
                  key={d.drugId}
                  variant="takeable"
                  drugId={d.drugId}
                  drugName={d.drugName}
                  doseStrength={d.doseStrength}
                  stockCount={d.stockCount}
                  drugType={d.drugType}
                  effectDurationMinHours={d.effectDurationMinHours}
                  effectDurationMaxHours={d.effectDurationMaxHours}
                  onTakeDose={(amount) =>
                    handleTake(d.drugId, amount, `${d.drugName} ${d.doseStrength}`)
                  }
                  disabled={intake.isLoading}
                />
              ))
            )}
          </>
        )}

        {/* Blocked drugs */}
        {filteredBlocked.length > 0 && (
          <>
            <div className="health-section-title health-section-title--blocked">
              ⛔ กินไม่ได้ตอนนี้
            </div>
            {filteredBlocked.map((d) => (
              <DrugCard
                key={d.drugId}
                variant="blocked"
                drugName={d.drugName}
                doseStrength={d.doseStrength}
                reason={d.reason}
                availableAt={d.availableAt}
              />
            ))}
          </>
        )}

        {/* Other drugs (stub for Phase 2 inline expand) */}
        {otherDrugsCount > 0 && (
          <button
            type="button"
            className="health-expand-link"
            onClick={() => navigate('/health/drugs')}
          >
            + ยาอื่นๆ ในระบบ ▾
          </button>
        )}

        <div className="health-divider-or">หรือ</div>

        {/* No-drug fallback */}
        <div className="health-no-drug-card">
          <button
            type="button"
            className="health-no-drug-btn"
            onClick={() => setShowNoDrugModal(true)}
            disabled={noDrug.isLoading}
          >
            📝 ไม่กินยา — log แค่อาการ
          </button>
          <div className="health-no-drug-sub">
            ระบบจะบันทึก:
            <br />
            <em>
              "{ctx.symptomName ?? 'อาการนี้'} {ctx.currentSeverity ?? '—'}/10 — ไม่ได้กินยา"
            </em>
            <br />
            เพื่อให้หมอเห็นใน report
          </div>
        </div>

        {/* Add new drug */}
        <button
          type="button"
          className="health-add-drug-btn"
          onClick={() => navigate('/health/drugs/new')}
        >
          ➕ เพิ่มยาใหม่ในระบบ
        </button>

        {/* No-drug reason modal */}
        {showNoDrugModal && (
          <div
            className="health-modal-backdrop"
            onClick={() => setShowNoDrugModal(false)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="ทำไมไม่กินยา"
            >
              <div className="health-modal__title">ทำไมไม่กินยา?</div>
              <select
                className="health-select"
                value={pendingReason}
                onChange={(e) =>
                  setPendingReason(Number(e.target.value) as NoDrugReason)
                }
              >
                {NO_DRUG_REASON_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setShowNoDrugModal(false)}
                >
                  ยกเลิก
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--primary"
                  onClick={handleConfirmNoDrug}
                  disabled={noDrug.isLoading}
                >
                  {noDrug.isLoading ? 'กำลังบันทึก...' : 'บันทึก'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Toasts */}
        {intake.toast && <div className="health-toast">{intake.toast}</div>}
        {noDrug.toast && <div className="health-toast">{noDrug.toast}</div>}
      </div>
    </div>
  )
}
