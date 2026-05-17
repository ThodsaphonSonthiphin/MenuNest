import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  useDeleteEpisodeMutation,
  useListTriggersQuery,
} from '../../shared/api/api'
import { TimelineView } from './components/TimelineView'
import { useEpisodeDetail } from './hooks/useEpisodeDetail'
import {
  AssociatedSymptom,
  SymptomLocation,
  SymptomQuality,
} from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Episode Detail — read-only view of a past (or active) episode with the
 * full attribute + intake + follow-up record.
 *
 *  - Status banner colors itself by outcome (green = resolved, yellow =
 *    failed / no-drug). Severity start→end appears as two side-by-side
 *    chips so the journey is obvious.
 *  - Trigger names are resolved client-side from the triggers list. The
 *    DTO only exposes `triggerIds` to keep the payload small.
 *  - Edit is a Phase 2 stub (links back to Quick Log for now); share &
 *    delete are wired.
 *
 * Mock: docs/mocks/patient-history-mock.html (right phone).
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

const THAI_MONTH = [
  'ม.ค.',
  'ก.พ.',
  'มี.ค.',
  'เม.ย.',
  'พ.ค.',
  'มิ.ย.',
  'ก.ค.',
  'ส.ค.',
  'ก.ย.',
  'ต.ค.',
  'พ.ย.',
  'ธ.ค.',
]
const THAI_WEEKDAY = [
  'อาทิตย์',
  'จันทร์',
  'อังคาร',
  'พุธ',
  'พฤหัส',
  'ศุกร์',
  'เสาร์',
]

function formatThaiDate(iso: string): string {
  const d = new Date(iso)
  // Buddhist calendar year (พ.ศ.) = CE + 543
  const buddhistYear = d.getFullYear() + 543
  return `${d.getDate()} ${THAI_MONTH[d.getMonth()]} ${buddhistYear} • ${THAI_WEEKDAY[d.getDay()]}`
}

function formatTimeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDurationBetween(startIso: string, endIso: string | null): string {
  const start = new Date(startIso).getTime()
  const end = endIso ? new Date(endIso).getTime() : Date.now()
  const diffMin = Math.max(0, Math.floor((end - start) / 60_000))
  const h = Math.floor(diffMin / 60)
  const m = diffMin % 60
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

export function EpisodeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { detail, timelineEvents, isLoading, isError } = useEpisodeDetail(id)
  const triggersQuery = useListTriggersQuery()
  const [deleteEpisode, deleteState] = useDeleteEpisodeMutation()
  const [showDeleteModal, setShowDeleteModal] = useState(false)

  const triggerNames = useMemo(() => {
    if (!detail || !triggersQuery.data) return [] as string[]
    return detail.triggerIds
      .map((tid) => triggersQuery.data?.find((t) => t.id === tid)?.name)
      .filter((n): n is string => !!n)
  }, [detail, triggersQuery.data])

  if (!id) {
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

  if (isLoading || !detail) {
    return (
      <div className="health-page">
        <div className="health-page__container">
          <div style={{ padding: 32, color: 'var(--hl-text-muted)' }}>
            {isError ? 'ไม่พบ episode' : 'กำลังโหลด...'}
          </div>
        </div>
      </div>
    )
  }

  const isFailed = detail.noDrugTaken || detail.retroClosed
  const isOngoing = !detail.endedAt
  const statusTag = isOngoing
    ? '⚡ Ongoing'
    : isFailed
      ? '⚠ No drug / closed manual'
      : '✓ Resolved'
  const statusClassName = isOngoing
    ? 'health-detail-status--ongoing'
    : isFailed
      ? 'health-detail-status--failed'
      : 'health-detail-status--resolved'

  const durationText = formatDurationBetween(detail.startedAt, detail.endedAt)
  const timeRange = detail.endedAt
    ? `${formatTimeOfDay(detail.startedAt)} — ${formatTimeOfDay(detail.endedAt)}`
    : `${formatTimeOfDay(detail.startedAt)} — ตอนนี้`
  const firstDrugName = detail.intakes?.[0]?.drugName ?? null

  const handleDelete = async () => {
    setShowDeleteModal(false)
    await deleteEpisode(id).unwrap()
    navigate('/health/history')
  }

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 15 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate('/health/history')}
            >
              ←
            </button>
            <span>Episode</span>
          </div>
        </header>

        {/* Status card */}
        <div className={`health-detail-status ${statusClassName}`}>
          <span className={`health-detail-tag ${statusClassName}__tag`}>
            {statusTag}
          </span>
          <div className="health-detail-date">📅 {formatThaiDate(detail.startedAt)}</div>
          <div className="health-detail-symptom">{detail.symptomName}</div>

          <div className="health-detail-severity-row">
            <div className="health-detail-sev health-detail-sev--start">
              <div className="health-detail-sev__label">เริ่ม</div>
              <div className="health-detail-sev__value">{detail.severity}</div>
            </div>
            <span className="health-detail-sev__arrow">→</span>
            <div className="health-detail-sev health-detail-sev--end">
              <div className="health-detail-sev__label">จบ</div>
              <div className="health-detail-sev__value">
                {detail.severityAfter ?? '–'}
              </div>
            </div>
          </div>

          <div className="health-detail-meta-row">
            <span>
              ⏱ <strong>{durationText}</strong>
            </span>
            {firstDrugName && (
              <span>
                🎯 <strong>{firstDrugName}</strong>
              </span>
            )}
            <span>{timeRange}</span>
          </div>
        </div>

        {/* Attributes */}
        <div className="health-section-title">attributes</div>
        <div className="health-attr-list">
          {detail.location && (
            <span className="health-attr-pill health-attr-pill--side">
              {LOCATION_LABEL[detail.location]}
            </span>
          )}
          {detail.quality && (
            <span className="health-attr-pill health-attr-pill--quality">
              {QUALITY_LABEL[detail.quality]}
            </span>
          )}
          {detail.isOnPeriod && (
            <span className="health-attr-pill health-attr-pill--period">
              ⚭ ประจำเดือน
            </span>
          )}
          {detail.associatedSymptoms?.map((s) => (
            <span
              key={s}
              className="health-attr-pill health-attr-pill--assoc"
            >
              {ASSOC_LABEL[s]}
            </span>
          ))}
          {!detail.location &&
            !detail.quality &&
            !detail.isOnPeriod &&
            (!detail.associatedSymptoms || detail.associatedSymptoms.length === 0) && (
              <span style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
                ยังไม่มี attributes
              </span>
            )}
        </div>

        {/* Triggers */}
        <div className="health-section-title">trigger</div>
        <div className="health-attr-list">
          {triggerNames.length === 0 ? (
            <span style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
              ไม่มี trigger
            </span>
          ) : (
            triggerNames.map((name) => (
              <span
                key={name}
                className="health-attr-pill health-attr-pill--trigger"
              >
                {name}
              </span>
            ))
          )}
        </div>

        {/* Timeline */}
        <div className="health-section-title">timeline</div>
        <TimelineView events={timelineEvents} />

        {/* Note */}
        {detail.notes && (
          <>
            <div className="health-section-title">note</div>
            <div className="health-note-card">"{detail.notes}"</div>
          </>
        )}

        {/* Actions */}
        <div className="health-actions-row">
          <button
            type="button"
            className="health-action-btn-detail health-action-btn-detail--primary"
            onClick={() => navigate(`/health/active/${detail.id}`)}
          >
            ✏️ แก้ไข
          </button>
          <button
            type="button"
            className="health-action-btn-detail"
            onClick={() => navigate('/health/share')}
          >
            📤 แชร์
          </button>
          <button
            type="button"
            className="health-action-btn-detail health-action-btn-detail--danger"
            onClick={() => setShowDeleteModal(true)}
            disabled={deleteState.isLoading}
            aria-label="Delete episode"
          >
            🗑
          </button>
        </div>

        {/* Delete confirm */}
        {showDeleteModal && (
          <div
            className="health-modal-backdrop"
            onClick={() => setShowDeleteModal(false)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="ลบ episode"
            >
              <div className="health-modal__title">ลบ episode นี้?</div>
              <div style={{ fontSize: 13, color: 'var(--hl-text-muted)' }}>
                จะลบทั้ง intakes และ follow-ups ของ episode นี้
                — ไม่สามารถกู้คืนได้
              </div>
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setShowDeleteModal(false)}
                >
                  ยกเลิก
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--danger"
                  onClick={handleDelete}
                  disabled={deleteState.isLoading}
                >
                  {deleteState.isLoading ? 'กำลังลบ...' : 'ลบ'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
