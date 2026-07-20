import type {DiscoverPlaceView} from '../lib/discoverFilter'

const CAT_LABEL: Record<string, string> = {See: 'เที่ยว', Eat: 'กิน', Cafe: 'คาเฟ่', Stay: 'ที่พัก', Shop: 'ช้อป', Other: 'อื่น ๆ'}

function distanceLabel(km: number | null): string {
  if (km == null) return ''
  return km < 1 ? `${Math.round(km * 1000)} ม.` : `${km.toFixed(1)} กม.`
}

interface Props {
  places: DiscoverPlaceView[]
  onSelect: (key: string) => void
}

export function PlaceBottomSheet({places, onSelect}: Props) {
  return (
    <div className="disc-sheet">
      <div className="disc-grip" />
      <div className="disc-sheet-head">
        <span className="h">ใกล้คุณ</span>
        <span className="n">{places.length} ที่ · เรียงตามระยะ</span>
      </div>
      <ul className="disc-list">
        {places.length === 0 && <li className="disc-empty">ยังไม่มีที่บันทึกไว้ในบริเวณนี้ — ลองเลื่อนแผนที่ หรือปิดตัวกรอง</li>}
        {places.map((p) => (
          <li key={p.key}>
            <button type="button" className={p.visited ? 'disc-row visited' : 'disc-row'} onClick={() => onSelect(p.key)}>
              <span className="disc-name">{p.name}</span>
              <span className="disc-meta">
                <span className="disc-dist">{distanceLabel(p.distanceKm)}</span>
                <span className="disc-cat-lab">{CAT_LABEL[p.category] ?? p.category}</span>
                {p.openNow === true && <span className="disc-badge open">เปิดอยู่</span>}
                {p.openNow === false && <span className="disc-badge closed">ปิดอยู่</span>}
                {p.seasonStatus === 'good' && <span className="disc-badge season">เดือนนี้ดี</span>}
                {p.trips[0] && <span className="disc-badge trip">{p.trips[0].tripName}</span>}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
