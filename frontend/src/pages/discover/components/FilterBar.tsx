import type {PlaceCategory} from '../../../shared/api/api'
import type {DiscoverToggles} from '../lib/discoverFilter'
import {ChevronIcon, SlidersIcon} from './DiscoverIcons'

const CATEGORIES: {value: PlaceCategory | 'all'; label: string}[] = [
  {value: 'all', label: 'ทั้งหมด'}, {value: 'See', label: 'เที่ยว'}, {value: 'Eat', label: 'กิน'},
  {value: 'Cafe', label: 'คาเฟ่'}, {value: 'Stay', label: 'ที่พัก'}, {value: 'Shop', label: 'ช้อป'}, {value: 'Other', label: 'อื่น ๆ'},
]
const TOGGLES: {key: keyof DiscoverToggles; label: string}[] = [
  {key: 'openNow', label: 'เปิดตอนนี้'}, {key: 'season', label: 'เดือนนี้'},
  {key: 'bestTime', label: 'ช่วงเวลา'}, {key: 'hideVisited', label: 'ซ่อนที่ไปแล้ว'},
]

interface Props {
  category: PlaceCategory | 'all'
  toggles: DiscoverToggles
  onCategory: (c: PlaceCategory | 'all') => void
  onToggle: (k: keyof DiscoverToggles) => void
}

export function FilterBar({category, toggles, onCategory, onToggle}: Props) {
  const activeLabel = CATEGORIES.find((c) => c.value === category)?.label ?? 'ทั้งหมด'
  return (
    <div className="disc-filters">
      {/* `.fchip.cat` in the mock is a dark pill with a sliders glyph and a caret.
          The native <select> is kept underneath — it gives the platform picker on
          mobile, which no custom dropdown matches — and is made transparent so the
          styled face below shows through. */}
      <span className="disc-cat-wrap">
        <SlidersIcon className="disc-cat-ico" />
        <span className="disc-cat-face">{activeLabel}</span>
        <ChevronIcon className="disc-cat-caret" />
        <select
          className="disc-cat"
          value={category}
          aria-label="หมวดสถานที่"
          onChange={(e) => onCategory(e.target.value as PlaceCategory | 'all')}
        >
          {CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
        </select>
      </span>
      {TOGGLES.map((t) => (
        <button
          key={t.key}
          type="button"
          className={toggles[t.key] ? 'disc-chip on' : 'disc-chip'}
          aria-pressed={toggles[t.key]}
          onClick={() => onToggle(t.key)}
        >
          <span className="disc-sw" aria-hidden="true" />
          {t.label}
        </button>
      ))}
    </div>
  )
}
