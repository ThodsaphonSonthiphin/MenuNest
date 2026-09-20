// frontend/src/pages/trips/components/GhostPinCard.tsx
//
// What a tapped **Ghost pin** shows on the Plan sheet / panel (spec §6.1).
//
// Two shapes, one card:
//   - a Place scheduled on NO Day → `เพิ่มเข้าวัน N`, the ADR-158 one-tap copy onto the
//     active Day. This is the whole point of issue #6: a saved Place you can finally see
//     on the map and schedule without leaving it.
//   - a Place scheduled on ANOTHER Day → it says so (`อยู่ในวัน M`) and offers `ไปวัน M`,
//     which switches the active Day. It deliberately does NOT offer to move the Stop:
//     spec §11.1 chose that as the least surprising default and flagged it for review.
import type {GhostPin} from '../lib/ghostPins'
import {CloseIcon} from './TripFormIcons'

export function GhostPinCard({
  ghost,
  dayNumber,
  onAddToDay,
  onGoToDay,
  onEditPlace,
  onClose,
}: {
  ghost: GhostPin
  /** The active Day's 1-based number — what `เพิ่มเข้าวัน N` commits to. */
  dayNumber?: number
  onAddToDay(): void
  onGoToDay(): void
  onEditPlace(): void
  onClose(): void
}) {
  const elsewhere = ghost.scheduledDayNumber != null

  return (
    <div className="ghost-card" data-testid="ghost-pin-card">
      <div className="gc-head">
        <span className="gc-dot" aria-hidden="true" />
        <div className="gc-title">
          <div className="gc-name">{ghost.name}</div>
          <div className="gc-sub">
            {elsewhere ? `อยู่ในวัน ${ghost.scheduledDayNumber}` : 'ยังไม่อยู่ในแผนวันไหน'}
          </div>
        </div>
        <button type="button" className="gc-close" aria-label="ปิด" onClick={onClose}>
          <CloseIcon />
        </button>
      </div>

      <div className="gc-actions">
        {elsewhere ? (
          <button type="button" className="gc-act main" onClick={onGoToDay}>
            ไปวัน {ghost.scheduledDayNumber}
          </button>
        ) : (
          <button type="button" className="gc-act main" onClick={onAddToDay}>
            เพิ่มเข้าวัน {dayNumber ?? ''}
          </button>
        )}
        <button type="button" className="gc-act" onClick={onEditPlace}>แก้ไขสถานที่</button>
      </div>
    </div>
  )
}
