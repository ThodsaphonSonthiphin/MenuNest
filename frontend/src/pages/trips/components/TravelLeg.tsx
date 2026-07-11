// frontend/src/pages/trips/components/TravelLeg.tsx
import type {LegDto, TravelMode} from '../../../shared/api/api'

const ICON: Record<TravelMode, string> = {Drive: '🚗', Walk: '🚶', Transit: '🚃'}

export function TravelLeg({leg, mode}: {leg: LegDto; mode: TravelMode}) {
  // Missing/undefined source is treated as Estimated so the pill never over-promises.
  const estimated = leg.source !== 'Routed'
  const prefix = estimated ? '~' : ''
  return (
    <div className="travel-leg">
      {/* ADR-024 locks this pill's exact text: full word "นาที", never abbreviated. */}
      <span className="leg-pill">{ICON[mode]} {prefix}{Math.round(leg.seconds / 60)} นาที</span>
      <span className="leg-line" />
      <span className="leg-dist">{prefix}{(leg.meters / 1000).toFixed(1)} กม.</span>
      {estimated && <span className="leg-approx">ประมาณ</span>}
    </div>
  )
}
