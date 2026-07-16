// frontend/src/pages/trips/components/FlagNote.tsx
// The timing-flag reason banner, shared by the itinerary detail sheet (issue #34).
// Extracted from ItineraryStopCard; identical rendering.
import type {FlagReason, TimingFlag} from '../hooks/useSchedule'
import {flagText} from '../timingFlag'
import {ClockIcon, LockIcon, MoonIcon} from './FlagIcons'

// Reason → icon component. `typeof LockIcon` avoids naming the JSX namespace.
const REASON_ICON: Record<FlagReason, typeof LockIcon> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}

export function FlagNote({flag}: {flag: TimingFlag}) {
  const Icon = REASON_ICON[flag.reason]
  const {reasonLine, fixLine} = flagText(flag)
  return (
    <div className={`flag-note${flag.severity === 'problem' ? ' bad' : ''}`}>
      <Icon />
      <span><b>{reasonLine}</b> <span className="fix">{fixLine}</span></span>
    </div>
  )
}
