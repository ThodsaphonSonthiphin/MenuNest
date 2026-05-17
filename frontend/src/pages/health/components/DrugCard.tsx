import { useState } from 'react'
import { BlockedReason, DrugType } from '../../../shared/api/healthTypes'

/**
 * Drug card surface used across the Take Medication flow (Task 14b) and
 * the Drug Master list (Task 14c). Four visual variants:
 *
 *  - `active`   yellow tint + progress bar (drug still in effect).
 *  - `takeable` green tint + dose action buttons.
 *  - `blocked`  gray + strikethrough + reason copy.
 *  - `master`   neutral chrome for the Drug Master list page.
 *
 * Each variant has a distinct prop shape; we discriminate via the
 * `variant` field so call sites stay strongly typed without `any`.
 *
 * Mock: docs/mocks/patient-take-medication-mock.html.
 */
export interface ActiveDrugCardProps {
  variant: 'active'
  drugName: string
  doseStrength: string
  lastTakenAt: string
  effectEndsAt: string
  remainingMinutes: number
  progressPct: number
  drugType?: DrugType
}

export interface TakeableDrugCardProps {
  variant: 'takeable'
  drugId: string
  drugName: string
  doseStrength: string
  stockCount: number
  effectDurationMinHours?: number
  effectDurationMaxHours?: number
  drugType?: DrugType
  onTakeDose: (amount: number) => Promise<void>
  disabled?: boolean
}

export interface BlockedDrugCardProps {
  variant: 'blocked'
  drugName: string
  doseStrength: string
  reason: BlockedReason
  availableAt?: string | null
  drugType?: DrugType
}

export interface MasterDrugCardProps {
  variant: 'master'
  drugName: string
  doseStrength: string
  drugType?: DrugType
  stockCount?: number
  onClick?: () => void
}

export type DrugCardProps =
  | ActiveDrugCardProps
  | TakeableDrugCardProps
  | BlockedDrugCardProps
  | MasterDrugCardProps

const DRUG_TYPE_LABEL: Record<DrugType, string> = {
  [DrugType.Analgesic]: 'Analgesic',
  [DrugType.Nsaid]: 'NSAID',
  [DrugType.Triptan]: 'Triptan',
  [DrugType.Other]: 'Other',
}

function DrugTypeTag({ type }: { type?: DrugType }) {
  if (!type) return null
  const isTriptan = type === DrugType.Triptan
  return (
    <span
      className={`health-drug-tag${isTriptan ? ' health-drug-tag--triptan' : ''}`}
    >
      {DRUG_TYPE_LABEL[type]}
    </span>
  )
}

function formatTimeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatRelativeMinutes(minutes: number): string {
  if (minutes <= 0) return 'หมดฤทธิ์แล้ว'
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

function formatRelativeFromNow(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime()
  const diffMin = Math.max(0, Math.floor(diffMs / 60_000))
  if (diffMin < 1) return 'เมื่อสักครู่'
  const h = Math.floor(diffMin / 60)
  const m = diffMin % 60
  if (h > 0) return `${h}h ${m}m ที่แล้ว`
  return `${m}m ที่แล้ว`
}

const BLOCKED_REASON_LABEL: Record<BlockedReason, string> = {
  [BlockedReason.MaxDoseReached]: '⛔ เกิน max daily dose',
  [BlockedReason.StillActive]: '⏳ ยังออกฤทธิ์อยู่',
  [BlockedReason.OutOfStock]: '📦 ยาหมด stock',
}

export function DrugCard(props: DrugCardProps) {
  if (props.variant === 'active') return <ActiveDrugCard {...props} />
  if (props.variant === 'takeable') return <TakeableDrugCard {...props} />
  if (props.variant === 'blocked') return <BlockedDrugCard {...props} />
  return <MasterDrugCard {...props} />
}

function ActiveDrugCard(props: ActiveDrugCardProps) {
  const {
    drugName,
    doseStrength,
    lastTakenAt,
    effectEndsAt,
    remainingMinutes,
    progressPct,
    drugType,
  } = props
  const fillPct = Math.min(100, Math.max(0, progressPct))
  return (
    <div className="health-drug-card-v2 health-drug-card-v2--active">
      <div className="health-drug-card-v2__name">
        <span>
          💊 {drugName} {doseStrength}
        </span>
        <DrugTypeTag type={drugType} />
      </div>
      <div className="health-drug-card-v2__meta">
        กิน {formatTimeOfDay(lastTakenAt)} ({formatRelativeFromNow(lastTakenAt)}) • หมดฤทธิ์ ~
        {formatTimeOfDay(effectEndsAt)}
      </div>
      <div className="health-progress-bar">
        <div className="health-progress-bar__fill" style={{ width: `${fillPct}%` }} />
      </div>
      <div className="health-progress-text">
        <span>{Math.round(fillPct)}%</span>
        <span>
          เหลืออีก{' '}
          <span className="health-progress-text__countdown">
            {formatRelativeMinutes(remainingMinutes)}
          </span>
        </span>
      </div>
      <div className="health-active-warn">⚠ อย่ากินซ้ำจนหมดฤทธิ์</div>
    </div>
  )
}

function TakeableDrugCard(props: TakeableDrugCardProps) {
  const {
    drugName,
    doseStrength,
    stockCount,
    effectDurationMinHours,
    effectDurationMaxHours,
    drugType,
    onTakeDose,
    disabled,
  } = props
  const [busy, setBusy] = useState<number | null>(null)
  const lowStock = stockCount > 0 && stockCount <= 2

  // Show "2 เม็ด" option only when stock supports it. Triptans typically
  // come 1-per-dose so we omit the second button there.
  const showTwoButton = stockCount >= 2 && drugType !== DrugType.Triptan

  const handle = async (amount: number) => {
    if (disabled || busy != null) return
    setBusy(amount)
    try {
      await onTakeDose(amount)
    } finally {
      setBusy(null)
    }
  }

  let durationHint = ''
  if (
    effectDurationMinHours != null &&
    effectDurationMaxHours != null &&
    effectDurationMaxHours > 0
  ) {
    durationHint =
      effectDurationMinHours === effectDurationMaxHours
        ? `ออกฤทธิ์ ${effectDurationMinHours} ชม.`
        : `ออกฤทธิ์ ${effectDurationMinHours}-${effectDurationMaxHours} ชม.`
  }

  return (
    <div className="health-drug-card-v2 health-drug-card-v2--takeable">
      <div className="health-drug-card-v2__name">
        <span>
          💊 {drugName} {doseStrength}
        </span>
        <DrugTypeTag type={drugType} />
      </div>
      <div className="health-stock-info">
        Stock: <strong>{stockCount} เม็ด</strong>
        {durationHint && <> • {durationHint}</>}
        {lowStock && (
          <div>
            <span className="health-stock-warn">⚠ Stock น้อย — ลองซื้อเพิ่ม</span>
          </div>
        )}
      </div>
      <div className="health-dose-buttons">
        <button
          type="button"
          className="health-dose-btn"
          onClick={() => handle(1)}
          disabled={!!disabled || stockCount < 1 || busy != null}
        >
          {busy === 1 ? 'กำลังบันทึก...' : 'กิน 1 เม็ด'}
        </button>
        {showTwoButton && (
          <button
            type="button"
            className="health-dose-btn"
            onClick={() => handle(2)}
            disabled={!!disabled || stockCount < 2 || busy != null}
          >
            {busy === 2 ? 'กำลังบันทึก...' : 'กิน 2 เม็ด'}
          </button>
        )}
      </div>
    </div>
  )
}

function BlockedDrugCard(props: BlockedDrugCardProps) {
  const { drugName, doseStrength, reason, availableAt, drugType } = props
  return (
    <div className="health-drug-card-v2 health-drug-card-v2--blocked">
      <div className="health-drug-card-v2__name">
        <span>
          💊 {drugName} {doseStrength}
        </span>
        <DrugTypeTag type={drugType} />
      </div>
      <div className="health-blocked-reason">{BLOCKED_REASON_LABEL[reason]}</div>
      {availableAt && (
        <div className="health-available-at">
          ⏰ กินได้อีกครั้ง: <strong>{formatTimeOfDay(availableAt)}</strong>
        </div>
      )}
    </div>
  )
}

function MasterDrugCard(props: MasterDrugCardProps) {
  const { drugName, doseStrength, drugType, stockCount, onClick } = props
  return (
    <button
      type="button"
      className="health-drug-card-v2 health-drug-card-v2--master"
      onClick={onClick}
      disabled={!onClick}
    >
      <div className="health-drug-card-v2__name">
        <span>
          💊 {drugName} {doseStrength}
        </span>
        <DrugTypeTag type={drugType} />
      </div>
      {stockCount != null && (
        <div className="health-stock-info">
          Stock: <strong>{stockCount} เม็ด</strong>
        </div>
      )}
    </button>
  )
}
