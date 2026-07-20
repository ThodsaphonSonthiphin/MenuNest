import type {PlaceCategory} from '../../../shared/api/api'
import type {DiscoverToggles} from '../lib/discoverFilter'

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
  return (
    <div className="disc-filters">
      <select className="disc-cat" value={category} onChange={(e) => onCategory(e.target.value as PlaceCategory | 'all')}>
        {CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
      </select>
      {TOGGLES.map((t) => (
        <button
          key={t.key}
          type="button"
          className={toggles[t.key] ? 'disc-chip on' : 'disc-chip'}
          aria-pressed={toggles[t.key]}
          onClick={() => onToggle(t.key)}
        >
          {t.label}
        </button>
      ))}
    </div>
  )
}
