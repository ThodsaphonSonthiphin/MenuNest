// frontend/src/pages/trips/components/AddPlacePreviewCard.tsx
// Preview card shown after a place is picked (search) or tapped (map). Category is
// pre-filled from the Google-types guess (ADR-016) and stays editable. Colour dot +
// Thai label — no emoji (project rule). Layout mirrors docs/mocks/trip-add-place-search-mock.html.
import {DropDownList} from '@syncfusion/react-dropdowns'
import type {PlaceCategory, ResolvedPlaceDto} from '../../../shared/api/api'
import {ReviewLinksSection} from './ReviewLinksSection'
import type {ReviewDraft} from '../lib/reviewLinks'

const CAT_COLOR: Record<PlaceCategory, string> = {
  Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76',
  Cafe: '#b4791f', Shop: '#c2418f', Other: '#0e8f9e',
}
const CAT_LABEL: Record<PlaceCategory, string> = {
  Stay: 'ที่พัก', Eat: 'ร้านอาหาร', See: 'ที่เที่ยว',
  Cafe: 'คาเฟ่', Shop: 'ช้อปปิ้ง', Other: 'อื่นๆ',
}
const CATS = (Object.keys(CAT_LABEL) as PlaceCategory[]).map((value) => ({
  label: CAT_LABEL[value], value,
}))

export interface AddPlacePreviewCardProps {
  place: ResolvedPlaceDto
  /** The name to save. Google's for a resolved place; user-typed for a coordinate capture. */
  name: string
  onNameChange(v: string): void
  /** True for a coordinate capture, which has no Google name to show (R4.1). */
  nameEditable: boolean
  category: PlaceCategory
  guessedCategory?: PlaceCategory
  onCategoryChange(c: PlaceCategory): void
  onCancel(): void
  onAdd(): void
  saving: boolean
  variant?: 'floating' | 'sheet'
  reviewDrafts: ReviewDraft[]
  onReviewDraftsChange(drafts: ReviewDraft[]): void
  confirmLabel?: string
  /** Label of the second, same-level action. Rendered only with `onSecondary`. */
  secondaryLabel?: string
  /** Present only where the Trip is undecided (Discover) — see ADR-155 / TC-801. */
  onSecondary?(): void
  /** R8.5's `▾`: reopen the trip picker instead of committing to the remembered Trip. */
  onPrimaryAlt?(): void
  primaryAltLabel?: string
  error?: string | null
}

export function AddPlacePreviewCard({
  place, name, onNameChange, nameEditable, category, guessedCategory, onCategoryChange,
  onCancel, onAdd, saving, variant = 'floating',
  reviewDrafts, onReviewDraftsChange, confirmLabel = 'เพิ่มลงทริป', error, secondaryLabel, onSecondary,
  onPrimaryAlt, primaryAltLabel,
}: AddPlacePreviewCardProps) {
  return (
    <div className={`add-preview add-preview-${variant}`}>
      {variant === 'sheet' && <div className="add-preview-grip" />}
      <div className="add-preview-head">
        <div className="add-preview-title">
          {nameEditable ? (
            <input
              className="add-preview-name-input"
              type="text"
              value={name}
              onChange={(e) => onNameChange(e.target.value)}
              placeholder="ตั้งชื่อสถานที่นี้"
              aria-label="ชื่อสถานที่"
              autoFocus
            />
          ) : (
            <div className="add-preview-name">{name}</div>
          )}
          {place.address
            ? <div className="add-preview-addr">{place.address}</div>
            : nameEditable && <div className="add-preview-addr">{place.lat.toFixed(5)}, {place.lng.toFixed(5)}</div>}
        </div>
        <button type="button" className="add-preview-close" aria-label="ปิด" onClick={onCancel}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
        </button>
      </div>

      <div className="add-preview-cat">
        <div className="add-preview-cat-lab">
          หมวดหมู่{' '}
          {guessedCategory !== undefined && category === guessedCategory && (
            <span className="add-preview-auto">เดาจาก Google: {CAT_LABEL[category]}</span>
          )}
        </div>
        <span className="add-preview-cat-dot" style={{background: CAT_COLOR[category]}} />
        <DropDownList
          dataSource={CATS}
          fields={{text: 'label', value: 'value'}}
          value={category}
          onChange={(e: {value: unknown}) => onCategoryChange((e.value as PlaceCategory) ?? 'Other')}
        />
      </div>

      <ReviewLinksSection drafts={reviewDrafts} onChange={onReviewDraftsChange} />

      {error && <p className="trips-field-error">{error}</p>}

      {/* Two same-level actions when the Trip is not yet decided (ADR-155): the primary
          commits to a chosen/remembered Trip, the secondary creates one in a single tap.
          TC-801 requires the create action to be a SIBLING here and never a row inside
          the picker, so it is rendered in this footer rather than by the picker. */}
      <div className={`add-preview-foot${onSecondary ? ' add-preview-foot--stacked' : ''}`}>
        {!onSecondary && (
          <button type="button" className="add-preview-cancel" onClick={onCancel}>ยกเลิก</button>
        )}
        {/* Split action: the label commits, the ▾ reopens the trip picker (R8.5).
            One control, because they are the same decision at two confidences. */}
        <span className={`add-preview-primary${onPrimaryAlt ? ' add-preview-primary--split' : ''}`}>
          <button type="button" className="add-preview-add" onClick={onAdd} disabled={saving}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M12 5v14M5 12h14" /></svg>
            {saving ? 'กำลังเพิ่ม…' : confirmLabel}
          </button>
          {onPrimaryAlt && (
            <button type="button" className="add-preview-alt" onClick={onPrimaryAlt} disabled={saving} aria-label={primaryAltLabel}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round"><path d="M6 9l6 6 6-6" /></svg>
            </button>
          )}
        </span>
        {onSecondary && (
          <button type="button" className="add-preview-second" onClick={onSecondary} disabled={saving}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M12 5v14M5 12h14" /></svg>
            {secondaryLabel}
          </button>
        )}
      </div>
    </div>
  )
}
