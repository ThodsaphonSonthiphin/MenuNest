// frontend/src/pages/trips/components/TravelLeg.tsx
import type {LegDto, TravelMode} from '../../../shared/api/api'

const ICON: Record<TravelMode, string> = {Drive: '🚗', Walk: '🚶', Transit: '🚃'}

export function TravelLeg({leg, mode}: {leg: LegDto; mode: TravelMode}) {
  return (
    <div className="travel-leg">
      <span className="leg-pill">{ICON[mode]} {Math.round(leg.seconds / 60)} นาที</span>
      <span className="leg-line" />
      <span className="leg-dist">{(leg.meters / 1000).toFixed(1)} กม.</span>
    </div>
  )
}
