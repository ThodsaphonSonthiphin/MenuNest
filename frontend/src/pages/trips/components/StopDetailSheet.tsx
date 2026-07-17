// frontend/src/pages/trips/components/StopDetailSheet.tsx
// Tap-a-card detail popup for the itinerary (issue #34). A body-portaled Syncfusion
// Dialog styled as a bottom sheet (same mechanism as StopEditorDialog). Holds the
// detail moved off the now-compact card: times, dwell, forecast-forward weather,
// timing flag, and the นำทาง / รีวิว / แก้ไข / มาแล้ว actions.
import {Dialog} from '@syncfusion/react-popups'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {catColor, catLabel} from '../placeCategory'
import {formatDurationMinutes} from '../utils/time'
import {reviewHost, reviewLabel} from '../lib/reviewLinks'
import {monthStatus, rangeLabel} from '../lib/season'
import {WeatherChip} from './WeatherChip'
import {WeatherDiorama} from './WeatherDiorama'
import {FlagNote} from './FlagNote'
import {NavIcon} from './NavIcon'
import {ReviewIcon} from './ReviewIcon'
import {CheckIcon} from './FlagIcons'

export function StopDetailSheet({
  place,
  arrival,
  depart,
  dwell,
  flag,
  tripMonth,
  dayNumber,
  ordinal,
  navUrl,
  nowReading,
  arrivalReading,
  weatherLoading = false,
  onEdit,
  onNavigate,
  onToggleVisited,
  onClose,
}: {
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  tripMonth: number
  dayNumber: number
  ordinal: number
  navUrl: string | null
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
  onEdit: () => void
  onNavigate?: () => void
  onToggleVisited: (next: boolean) => void
  onClose: () => void
}) {
  const links = place.reviewLinks ?? []
  const seasonPeriods = place.seasonPeriods ?? []
  const season = monthStatus(place.seasonPeriods, tripMonth)

  const header = (
    <div className="sd-head">
      <div className="sd-title">{place.name}</div>
      <div className="sd-meta">
        <span className="sd-cat">
          <span className="sd-cat-dot" style={{background: catColor(place.category)}} />
          {catLabel(place.category)}
        </span>
        <span className="sd-crumb">วัน {dayNumber} · จุดที่ {ordinal}</span>
      </div>
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="stop-detail-sheet"
      header={header}
      position="CenterBottom"
      style={{width: 'min(480px, 100vw)'}}
    >
      <div className="stop-detail" data-testid="stop-detail-sheet">
        <div className="sd-times">
          <div className="sd-time-col">
            <div className="sd-time-lab">ถึง</div>
            <div className="sd-time-val">{arrival}</div>
          </div>
          <div className="sd-time-arrow">→</div>
          <div className="sd-time-col">
            <div className="sd-time-lab">ออก</div>
            <div className="sd-time-val">{depart}</div>
          </div>
          <div className="sd-dwell"><span className="sd-dwell-lab">อยู่</span> {formatDurationMinutes(dwell)}</div>
        </div>

        <div className="sd-weather">
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>

        {season.kind !== 'none' && (
          <div className={`stop-season ${season.kind}`}>
            <WeatherDiorama kind={season.kind} />
            {season.kind === 'bad' && (
              <div className="stop-season-note">
                <strong>เดือนนี้ควรเลี่ยง{season.period.note ? ` · ${season.period.note}` : ''}</strong>
                <span>ย้ายทริปไปเดือนอื่น</span>
              </div>
            )}
          </div>
        )}

        {flag && <FlagNote flag={flag} />}

        {links.length > 0 && (
          <div className="sd-reviews">
            <div className="sd-sec-lab">รีวิว</div>
            {links.map((l, i) => (
              <a key={l.url + i} className="sd-review" href={l.url} target="_blank" rel="noopener noreferrer">
                <ReviewIcon />
                <span className="sd-review-label">{reviewLabel(l, i)}</span>
                <span className="sd-review-host">{reviewHost(l.url)}</span>
              </a>
            ))}
          </div>
        )}

        {seasonPeriods.length > 0 && (
          <div className="sd-seasons">
            <div className="sd-sec-lab">ช่วงเดือน</div>
            <ul className="season-rows">
              {seasonPeriods.map((p, i) => (
                <li key={i} className={`sp-row ${p.kind === 'Bad' ? 'bad' : 'good'}`}>
                  <span className="sp-pill">{p.kind === 'Bad' ? 'ควรเลี่ยง' : 'ควรไป'}</span>
                  <span className="sp-range">{rangeLabel(p.months)}</span>
                  {p.note && <span className="sp-note">{p.note}</span>}
                </li>
              ))}
            </ul>
          </div>
        )}

        <div className="sd-actions">
          {navUrl ? (
            <a
              className="sd-act primary"
              href={navUrl}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => onNavigate?.()}
            >
              <NavIcon /> นำทาง
            </a>
          ) : (
            <span className="sd-act primary disabled" role="img" aria-disabled="true" aria-label="ไม่มีพิกัดสำหรับนำทาง">
              <NavIcon /> นำทาง
            </span>
          )}
          <button type="button" className="sd-act" onClick={onEdit}>แก้ไข</button>
          <button type="button" className="sd-act visited" onClick={() => onToggleVisited(true)}>
            <CheckIcon /> มาแล้ว
          </button>
        </div>
      </div>
    </Dialog>
  )
}
