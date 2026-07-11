// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {FlagReason, FlagSeverity, StopFlag, TimingFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {flagText} from '../timingFlag'
import {NavIcon} from './NavIcon'
import {ClockIcon, LockIcon, MoonIcon} from './FlagIcons'
import {WeatherChip} from './WeatherChip'
import {formatDurationMinutes} from '../utils/time'

// Reason → icon component. `typeof LockIcon` avoids naming the JSX namespace.
const REASON_ICON: Record<FlagReason, typeof LockIcon> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}
// Severity → CSS class. NEVER interpolate the raw severity string (enum ≠ class name).
const CARD_CLASS: Record<FlagSeverity, string> = {problem: 'bad', suggestion: 'warn'}

function FlagNote({flag}: {flag: TimingFlag}) {
  const Icon = REASON_ICON[flag.reason]
  const {reasonLine, fixLine} = flagText(flag)
  return (
    <div className={`flag-note${flag.severity === 'problem' ? ' bad' : ''}`}>
      <Icon />
      <span><b>{reasonLine}</b> <span className="fix">{fixLine}</span></span>
    </div>
  )
}

export function ItineraryStopCard({
  place,
  arrival,
  depart,
  dwell,
  flag,
  onEdit,
  onUp,
  onDown,
  canUp,
  canDown,
  navUrl,
  onNavigate,
  nowReading,
  arrivalReading,
  weatherLoading = false,
}: {
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  onEdit: () => void
  onUp: () => void
  onDown: () => void
  canUp: boolean
  canDown: boolean
  navUrl: string | null
  onNavigate?: () => void
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
}) {
  return (
    <div className={`stop-card${flag ? ' ' + CARD_CLASS[flag.severity] : ''}`}>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {formatDurationMinutes(dwell)}</span>
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>
        {flag && <FlagNote flag={flag} />}
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
