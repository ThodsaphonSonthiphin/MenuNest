import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { QRCodeShare } from './components/QRCodeShare'
import { useShareLinks } from './hooks/useShareLinks'
import type {
  CreateShareLinkResultDto,
  ShareLinkSummaryDto,
} from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Share Links — manage doctor-share links.
 *
 *  - Top section: create a new link (date range + validity).
 *  - After create: modal renders the QR code + the URL + a copy button.
 *  - List section: existing links with revoke action. Each row shows
 *    status (active / expired / revoked) and access count.
 *
 *  Mock: builds on `patient-share-links` brainstorm — no dedicated mock
 *  file ships in the repo, so we follow the same card chrome and color
 *  tokens as the rest of the health module.
 */

const VALIDITY_OPTIONS = [
  { value: 7, label: '7 วัน' },
  { value: 14, label: '14 วัน' },
  { value: 30, label: '30 วัน' },
  { value: 90, label: '90 วัน' },
]

function defaultDateFrom(): string {
  // 30 days ago
  const d = new Date()
  d.setDate(d.getDate() - 30)
  return d.toISOString().slice(0, 10)
}

function defaultDateTo(): string {
  return new Date().toISOString().slice(0, 10)
}

function linkStatus(link: ShareLinkSummaryDto): {
  text: string
  className: string
} {
  if (link.revokedAt) return { text: '🚫 ยกเลิกแล้ว', className: 'revoked' }
  const expires = new Date(link.expiresAt).getTime()
  if (expires < Date.now()) return { text: '⌛ หมดอายุ', className: 'expired' }
  return { text: '✓ ใช้งานได้', className: 'active' }
}

function formatThaiDate(iso: string): string {
  const d = new Date(iso)
  const buddhist = d.getFullYear() + 543
  return `${d.getDate()}/${d.getMonth() + 1}/${buddhist}`
}

export function ShareLinksPage() {
  const navigate = useNavigate()
  const { links, isLoading, isError, create, revoke, isCreating, isRevoking } =
    useShareLinks()

  const [dateFrom, setDateFrom] = useState(defaultDateFrom())
  const [dateTo, setDateTo] = useState(defaultDateTo())
  const [validForDays, setValidForDays] = useState(30)
  const [created, setCreated] = useState<CreateShareLinkResultDto | null>(null)
  const [copyToast, setCopyToast] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [pendingRevoke, setPendingRevoke] = useState<ShareLinkSummaryDto | null>(
    null,
  )

  const sortedLinks = useMemo(() => {
    return [...links].sort(
      (a, b) =>
        new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
    )
  }, [links])

  const handleCreate = async () => {
    setSubmitError(null)
    if (!dateFrom || !dateTo) {
      setSubmitError('กรุณาเลือกช่วงวันที่')
      return
    }
    if (dateFrom > dateTo) {
      setSubmitError('วันที่เริ่มต้องมาก่อนวันที่สิ้นสุด')
      return
    }
    try {
      const result = await create({ dateFrom, dateTo, validForDays })
      setCreated(result)
    } catch (err) {
      const message =
        err && typeof err === 'object' && 'data' in err && err.data
          ? String((err as { data?: unknown }).data)
          : 'สร้าง share link ไม่สำเร็จ'
      setSubmitError(message)
    }
  }

  const handleCopy = async () => {
    if (!created) return
    try {
      await navigator.clipboard.writeText(created.shareUrl)
      setCopyToast('📋 Copied to clipboard')
      window.setTimeout(() => setCopyToast(null), 1800)
    } catch {
      setCopyToast('Copy ไม่สำเร็จ — เลือกแล้วก๊อปด้วยมือ')
      window.setTimeout(() => setCopyToast(null), 2200)
    }
  }

  const handleRevoke = async () => {
    if (!pendingRevoke) return
    const id = pendingRevoke.id
    setPendingRevoke(null)
    await revoke(id)
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
              onClick={() => navigate('/health')}
            >
              ←
            </button>
            <span>แชร์ให้หมอดู</span>
          </div>
        </header>

        {/* Create form */}
        <div className="health-card">
          <div
            style={{
              fontSize: 14,
              fontWeight: 600,
              marginBottom: 10,
            }}
          >
            🔗 สร้าง share link ใหม่
          </div>
          <div className="health-form-section">
            <div className="health-form-label">📅 จากวันที่</div>
            <input
              type="date"
              className="health-form-input"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
            />
          </div>
          <div className="health-form-section">
            <div className="health-form-label">📅 ถึงวันที่</div>
            <input
              type="date"
              className="health-form-input"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
            />
          </div>
          <div className="health-form-section">
            <div className="health-form-label">⏳ Link มีอายุ</div>
            <select
              className="health-select"
              value={validForDays}
              onChange={(e) => setValidForDays(Number(e.target.value))}
            >
              {VALIDITY_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
          {submitError && (
            <div
              style={{
                marginBottom: 8,
                padding: 10,
                background: 'var(--hl-danger-bg)',
                color: 'var(--hl-danger)',
                fontSize: 13,
                borderRadius: 8,
              }}
            >
              {submitError}
            </div>
          )}
          <button
            type="button"
            className="health-save-btn"
            style={{ width: '100%' }}
            onClick={handleCreate}
            disabled={isCreating}
          >
            🔗 {isCreating ? 'กำลังสร้าง...' : 'สร้าง share link'}
          </button>
        </div>

        {/* Existing links */}
        <div className="health-section-title">
          <span>Share links ที่มีอยู่</span>
          {sortedLinks.length > 0 && (
            <span className="health-section-title__count">{sortedLinks.length}</span>
          )}
        </div>

        {isLoading && (
          <div style={{ padding: 16, color: 'var(--hl-text-muted)', fontSize: 13 }}>
            กำลังโหลด...
          </div>
        )}

        {isError && (
          <div className="health-card" style={{ color: 'var(--hl-danger)' }}>
            ⚠ โหลด share links ไม่สำเร็จ
          </div>
        )}

        {!isLoading && sortedLinks.length === 0 && (
          <div className="health-empty-state">ยังไม่ได้สร้าง share link</div>
        )}

        {sortedLinks.map((link) => {
          const status = linkStatus(link)
          const canRevoke = !link.revokedAt
          return (
            <div key={link.id} className="health-share-link-card">
              <div className="health-share-link-card__top">
                <div className="health-share-link-card__range">
                  📅 {formatThaiDate(link.dateFrom)} — {formatThaiDate(link.dateTo)}
                </div>
                <span
                  className={`health-share-link-status health-share-link-status--${status.className}`}
                >
                  {status.text}
                </span>
              </div>
              <div className="health-share-link-card__meta">
                <span>สร้าง: {formatThaiDate(link.createdAt)}</span>
                <span>หมดอายุ: {formatThaiDate(link.expiresAt)}</span>
                <span>เข้าดู: {link.accessCount} ครั้ง</span>
              </div>
              {canRevoke && (
                <div style={{ marginTop: 8, display: 'flex', justifyContent: 'flex-end' }}>
                  <button
                    type="button"
                    className="health-action-btn health-action-btn--danger"
                    onClick={() => setPendingRevoke(link)}
                    disabled={isRevoking}
                  >
                    🗑 ยกเลิก link
                  </button>
                </div>
              )}
            </div>
          )
        })}

        {/* Created modal — QR code + copy */}
        {created && (
          <div
            className="health-modal-backdrop"
            onClick={() => setCreated(null)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="Share link สร้างเรียบร้อย"
            >
              <div className="health-modal__title">🎉 สร้าง share link สำเร็จ</div>
              <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 12 }}>
                <QRCodeShare shareUrl={created.shareUrl} size={200} />
              </div>
              <div className="health-share-url-box">{created.shareUrl}</div>
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={handleCopy}
                >
                  📋 Copy
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--primary"
                  onClick={() => setCreated(null)}
                >
                  ปิด
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Revoke confirm modal */}
        {pendingRevoke && (
          <div
            className="health-modal-backdrop"
            onClick={() => setPendingRevoke(null)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="ยกเลิก share link"
            >
              <div className="health-modal__title">ยกเลิก share link นี้?</div>
              <div style={{ fontSize: 13, color: 'var(--hl-text-muted)' }}>
                หลังยกเลิก ลิงก์นี้จะใช้งานไม่ได้ทันที — ต้องสร้างใหม่ถ้าจะแชร์อีก
              </div>
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setPendingRevoke(null)}
                >
                  ปิด
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--danger"
                  onClick={handleRevoke}
                  disabled={isRevoking}
                >
                  {isRevoking ? 'กำลังยกเลิก...' : 'ยกเลิก link'}
                </button>
              </div>
            </div>
          </div>
        )}

        {copyToast && <div className="health-toast">{copyToast}</div>}
      </div>
    </div>
  )
}
