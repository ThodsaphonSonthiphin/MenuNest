// frontend/src/pages/trips/components/AddPlacePreviewCard.tsx
// Preview card shown after a place is picked (search) or tapped (map). Category is
// pre-filled from the Google-types guess (ADR-016) and stays editable. Colour dot +
// Thai label — no emoji (project rule). Layout mirrors docs/mocks/trip-add-place-search-mock.html.
import {DropDownList} from '@syncfusion/react-dropdowns'
import type {PlaceCategory, ResolvedPlaceDto} from '../../../shared/api/api'

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
  category: PlaceCategory
  onCategoryChange(c: PlaceCategory): void
  onCancel(): void
  onAdd(): void
  saving: boolean
  variant?: 'floating' | 'sheet'
}

export function AddPlacePreviewCard({
  place, category, onCategoryChange, onCancel, onAdd, saving, variant = 'floating',
}: AddPlacePreviewCardProps) {
  return (
    <div className={`add-preview add-preview-${variant}`}>
      {variant === 'sheet' && <div className="add-preview-grip" />}
      <div className="add-preview-head">
        <div className="add-preview-title">
          <div className="add-preview-name">{place.name}</div>
          {place.address && <div className="add-preview-addr">{place.address}</div>}
        </div>
        <button type="button" className="add-preview-close" aria-label="ปิด" onClick={onCancel}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
        </button>
      </div>

      <div className="add-preview-cat">
        <div className="add-preview-cat-lab">
          หมวดหมู่ <span className="add-preview-auto">เดาจาก Google: {CAT_LABEL[category]}</span>
        </div>
        <span className="add-preview-cat-dot" style={{background: CAT_COLOR[category]}} />
        <DropDownList
          dataSource={CATS}
          fields={{text: 'label', value: 'value'}}
          value={category}
          onChange={(e: {value: unknown}) => onCategoryChange((e.value as PlaceCategory) ?? 'Other')}
        />
      </div>

      <div className="add-preview-foot">
        <button type="button" className="add-preview-cancel" onClick={onCancel}>ยกเลิก</button>
        <button type="button" className="add-preview-add" onClick={onAdd} disabled={saving}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M12 5v14M5 12h14" /></svg>
          {saving ? 'กำลังเพิ่ม…' : 'เพิ่มลงทริป'}
        </button>
      </div>
    </div>
  )
}
