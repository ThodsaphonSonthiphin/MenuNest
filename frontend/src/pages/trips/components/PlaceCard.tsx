// frontend/src/pages/trips/components/PlaceCard.tsx
import type { TripPlaceDto } from '../../../shared/api/api'
import { catColor, catLabel } from '../placeCategory'

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
        style={{ background: catColor(place.category) }}
      />
      <span className="place-body">
        <span className="place-name">{place.name}</span>
        <span className="place-sub">
          {catLabel(place.category)}
          {place.priceLevel != null
            ? ` · ${'฿'.repeat(Math.max(1, place.priceLevel))}`
            : ''}
        </span>
      </span>
    </button>
  )
}
