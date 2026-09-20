// frontend/src/pages/trips/components/CompactStopCard.tsx
//
// What the **Selected Stop** shows on the **Plan sheet** at the summary detent
// (menunest-238, mock frame 4). Tapping a route pin drops the sheet to summary and puts
// this in place of the day stats, so the Stop the user just tapped is the thing in front of
// them — they never have to go find its card in the list.
//
// It takes the ALREADY-PROJECTED ScheduledStop and reading: the times, the flag and the
// weather line are the same values the list card shows, not a second derivation (spec §6.3).
// Desktop has no equivalent — the panel's list is always visible (spec §6.3).
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {buildStopSummary} from '../lib/stopSummary'
import {iconUrl} from '../lib/weather'
import {catEmoji} from '../placeCategory'
import {formatDurationMinutes} from '../utils/time'
import {CheckIcon, CloseIcon} from './TripFormIcons'
import {NavIcon} from './NavIcon'

export function CompactStopCard({
  place,
  ordinal,
  dayNumber,
  arrival,
  depart,
  dwell,
  flag,
  arrivalReading,
  uvWarn,
  feelsWarn,
  navUrl,
  isVisited,
  onNavigate,
  onEdit,
  onToggleVisited,
  onOpenDetail,
  onClose,
}: {
  place: TripPlaceDto
  ordinal: number
  dayNumber: number
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  arrivalReading?: WeatherReadingDto
  uvWarn?: number | null
  feelsWarn?: number | null
  navUrl: string | null
  isVisited: boolean
  onNavigate(): void
  onEdit(): void
  onToggleVisited(next: boolean): void
  onOpenDetail(): void
  onClose(): void
}) {
  const summary = buildStopSummary({arrivalReading, dwellMinutes: dwell, flag, uvWarn, feelsWarn})
  const severity = flag ? (flag.severity === 'problem' ? ' bad' : ' warn') : ''

  return (
    <div className="compact-stop" data-testid="compact-stop-card" data-stop-place={place.id}>
      <div className="cs-head">
        <span className={`cs-num${severity}`} aria-hidden="true">{ordinal}</span>
        <div className="cs-title">
          <div className="cs-name">{catEmoji(place.category)} {place.name}</div>
          <div className="cs-sub">
            จุดที่ {ordinal} ของวัน {dayNumber}
            {summary.weather && (
              <>
                {' · '}
                {summary.weather.iconBaseUri && (
                  <img src={iconUrl(summary.weather.iconBaseUri, false)} alt="" width={14} height={14} />
                )}
                {summary.weather.label}
              </>
            )}
          </div>
        </div>
        <button type="button" className="cs-close" aria-label="เลิกเลือกจุดแวะ" onClick={onClose}>
          <CloseIcon />
        </button>
      </div>

      <div className="cs-times">
        <div className="cs-stat"><span className="k">ถึง</span><span className="v">{arrival}</span></div>
        <div className="cs-stat"><span className="k">ออก</span><span className="v">{depart}</span></div>
        <div className="cs-stat"><span className="k">อยู่</span><span className="v">{formatDurationMinutes(dwell)}</span></div>
      </div>

      {summary.flag && (
        <div className={`cs-flag ${summary.flag.severity === 'problem' ? 'bad' : 'warn'}`}>
          <span className="d" aria-hidden="true" />
          {summary.flag.label}
        </div>
      )}

      <div className="cs-actions">
        {/* An unusable place (no coords, no place_id) yields no URL — render the slot
            disabled rather than a link that goes nowhere. */}
        {navUrl ? (
          <a className="cs-act main" href={navUrl} target="_blank" rel="noopener noreferrer" onClick={onNavigate}>
            <NavIcon /> นำทาง
          </a>
        ) : (
          <span className="cs-act main disabled" aria-disabled="true"><NavIcon /> นำทาง</span>
        )}
        <button type="button" className="cs-act" onClick={onEdit}>แก้ไข</button>
        <button
          type="button"
          className={`cs-act${isVisited ? ' on' : ''}`}
          aria-pressed={isVisited}
          onClick={() => onToggleVisited(!isVisited)}
        >
          <CheckIcon /> มาแล้ว
        </button>
      </div>

      <button type="button" className="cs-more" onClick={onOpenDetail}>ดูทั้งหมด</button>
    </div>
  )
}
