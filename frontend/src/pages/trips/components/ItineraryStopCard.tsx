// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'

export function ItineraryStopCard({
  place,
  arrival,
  depart,
  dwell,
  flag,
  bestLabel,
  onEdit,
  onUp,
  onDown,
  canUp,
  canDown,
}: {
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  bestLabel: string | null
  onEdit: () => void
  onUp: () => void
  onDown: () => void
  canUp: boolean
  canDown: boolean
}) {
  return (
    <div className={`stop-card${flag === 'amber' ? ' warn' : ''}`}>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{place.name}</div>
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {dwell} น.</span>
          {bestLabel && (
            <span className={`chip ${flag === 'amber' ? 'warn' : 'good'}`}>
              {flag === 'amber' ? '⚠' : '✓'} {bestLabel}
            </span>
          )}
        </div>
      </button>
      <div className="stop-reorder">
        <button disabled={!canUp} onClick={onUp} aria-label="ขึ้น">▲</button>
        <button disabled={!canDown} onClick={onDown} aria-label="ลง">▼</button>
      </div>
    </div>
  )
}
