// frontend/src/pages/trips/components/VisitedStopRow.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import {catEmoji} from '../placeCategory'

/** Slim, non-draggable row for a Visited ("มาแล้ว") Stop inside the collapsible drawer
 *  (ADR-048). It lives OUTSIDE the DndContext and never calls useSortable. Un-ticking the
 *  checkbox sends the Stop back to the active "ที่เหลือ" list. */
export function VisitedStopRow({
  place,
  arrival,
  onUnvisit,
}: {
  place: TripPlaceDto
  arrival: string
  onUnvisit: () => void
}) {
  return (
    <div className="done-item">
      <label className="stop-check">
        <input
          type="checkbox"
          checked
          onChange={() => onUnvisit()}
          aria-label={`เอาออกจากรายการมาแล้ว: ${place.name}`}
        />
      </label>
      <span className="di-time">{arrival}</span>
      <span className="di-name">{catEmoji(place.category)} {place.name}</span>
    </div>
  )
}
