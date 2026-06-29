// frontend/src/pages/trips/components/PlaceCard.tsx
import type { TripPlaceDto } from '../../../shared/api/api'

const CAT_COLOR: Record<string, string> = {
  Stay: '#6d5ae6',
  Eat: '#e2553e',
  See: '#1f9d76',
  Cafe: '#b4791f',
  Shop: '#c2418f',
  Other: '#94a3b8',
}

const CAT_LABEL: Record<string, string> = {
  Stay: 'ที่พัก',
  Eat: 'ร้านอาหาร',
  See: 'ที่เที่ยว',
  Cafe: 'คาเฟ่',
  Shop: 'ช้อปปิ้ง',
  Other: 'อื่นๆ',
}

export function PlaceCard({
  place,
  onClick,
}: {
  place: TripPlaceDto
  onClick?: () => void
}) {
  return (
    <button className="place-card" onClick={onClick}>
      <span
        className="place-dot"
        style={{ background: CAT_COLOR[place.category] ?? '#94a3b8' }}
      />
      <span className="place-body">
        <span className="place-name">{place.name}</span>
        <span className="place-sub">
          {CAT_LABEL[place.category] ?? place.category}
          {place.priceLevel != null
            ? ` · ${'฿'.repeat(Math.max(1, place.priceLevel))}`
            : ''}
        </span>
      </span>
    </button>
  )
}
