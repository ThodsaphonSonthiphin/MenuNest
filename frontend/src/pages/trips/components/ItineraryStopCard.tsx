// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import {useSortable} from '@dnd-kit/sortable'
import {CSS} from '@dnd-kit/utilities'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {buildStopSummary, type StopSummary} from '../lib/stopSummary'
import {iconUrl} from '../lib/weather'
import {GripIcon, ChevronRightIcon} from './TripFormIcons'
import {WeatherDiorama} from './WeatherDiorama'
import {monthStatus} from '../lib/season'

// One-line, forecast-forward summary shown under the stop name on the compact card (issue #34):
// arrival weather → dwell → timing-flag dot. Full detail lives in the StopDetailSheet.
function StopSummaryLine({summary}: {summary: StopSummary}) {
  return (
    <div className="stop-summary">
      {summary.weather && (
        <span className="sum-wx">
          {summary.weather.iconBaseUri && (
            <img src={iconUrl(summary.weather.iconBaseUri, false)} alt="" width={15} height={15} />
          )}
          {summary.weather.label}
        </span>
      )}
      <span className="sum-dwell">{summary.dwellText}</span>
      {summary.flag && (
        <span className={`sum-flag ${summary.flag.severity === 'problem' ? 'bad' : 'warn'}`}>
          <span className="sum-flag-dot" />
          {summary.flag.label}
        </span>
      )}
    </div>
  )
}

export function ItineraryStopCard({
  id,
  place,
  arrival,
  dwell,
  flag,
  arrivalReading,
  tripMonth,
  reorderMode = false,
  onOpenDetail,
}: {
  id: string
  place: TripPlaceDto
  arrival: string
  dwell: number
  flag: StopFlag
  arrivalReading?: WeatherReadingDto
  tripMonth: number
  reorderMode?: boolean
  onOpenDetail?: () => void
}) {
  const {attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging} =
    useSortable({id})
  const style = {transform: CSS.Transform.toString(transform), transition}

  const summary = buildStopSummary({arrivalReading, dwellMinutes: dwell, flag})
  const cardFlag = flag ? (flag.severity === 'problem' ? ' bad' : ' warn') : ''
  const season = monthStatus(place.seasonPeriods, tripMonth)

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`stop-card compact${cardFlag}${isDragging ? ' dragging' : ''}${season.kind === 'bad' ? ' season-bad' : season.kind === 'good' ? ' season-good' : ''}`}
      data-testid="itin-stop-card"
      data-stop-id={id}
    >
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

      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
      </div>

      {reorderMode ? (
        <div className="stop-body static">
          <div className="stop-text">
            <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
            <StopSummaryLine summary={summary} />
          </div>
        </div>
      ) : (
        // The chevron lives INSIDE the button so the whole row — name, summary, and the
        // "opens detail" affordance — is one tap target, not just the text column (#34).
        <button className="stop-body" onClick={onOpenDetail} aria-label={`ดูรายละเอียด: ${place.name}`}>
          <div className="stop-text">
            <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
            <StopSummaryLine summary={summary} />
          </div>
          <span className="stop-chevron" aria-hidden="true">
            <ChevronRightIcon />
          </span>
        </button>
      )}

      {reorderMode && (
        <button
          ref={setActivatorNodeRef}
          type="button"
          className="stop-drag-handle"
          aria-label="ลากเพื่อจัดลำดับ"
          data-testid="stop-drag-handle"
          {...attributes}
          {...listeners}
        >
          <GripIcon />
        </button>
      )}
    </div>
  )
}
