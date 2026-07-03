// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {NavIcon} from './NavIcon'

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
  overnight = false,
  navUrl,
  onNavigate,
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
  overnight?: boolean
  navUrl: string | null
  onNavigate?: () => void
}) {
  return (
    <div className={`stop-card${flag === 'amber' ? ' warn' : ''}`}>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {dwell} น.</span>
          {overnight && <span className="chip warn">+1วัน</span>}
          {bestLabel && (
            <span className={`chip ${flag === 'amber' ? 'warn' : 'good'}`}>
              {flag === 'amber' ? '⚠' : '✓'} {bestLabel}
            </span>
          )}
        </div>
      </button>
      {navUrl ? (
        <a
          className="stop-nav"
          href={navUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="นำทาง"
          onClick={(e) => {
            e.stopPropagation()
            onNavigate?.()
          }}
        >
          <NavIcon />
        </a>
      ) : (
        <span className="stop-nav" role="img" aria-label="ไม่มีพิกัดสำหรับนำทาง" aria-disabled="true">
          <NavIcon />
        </span>
      )}
      <div className="stop-reorder">
        <button disabled={!canUp} onClick={onUp} aria-label="ขึ้น">▲</button>
        <button disabled={!canDown} onClick={onDown} aria-label="ลง">▼</button>
      </div>
    </div>
  )
}
