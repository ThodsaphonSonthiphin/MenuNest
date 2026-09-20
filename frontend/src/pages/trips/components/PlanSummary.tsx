// frontend/src/pages/trips/components/PlanSummary.tsx
//
// The **Plan sheet**'s (and **Plan panel**'s) always-visible header: what a Day amounts to
// in one glance — `วัน N · M จุด`, `x/y มาแล้ว`, `เริ่ม–เสร็จ`, `เดินทางรวม`, and `นำทาง`.
// This is everything the `summary` detent shows (spec §4.1), so its measured height is what
// that detent resolves to.
//
// While a Stop is selected on a sheet surface it is replaced by the **Compact stop card** —
// the caller decides, because desktop's always-visible list means the panel never swaps
// (spec §6.3).
import type {Plan} from '../hooks/usePlan'
import {DayStartEditor} from './DayStartEditor'
import {NavIcon} from './NavIcon'
import {formatDurationMinutes} from '../utils/time'
import {appInsights} from '../../../shared/telemetry/appInsights'

export function PlanSummary({
  tripId,
  plan,
  isDaily,
  onError,
}: {
  tripId: string
  plan: Plan
  isDaily: boolean
  onError(msg: string | null): void
}) {
  const {day, dayNumber, remaining, done, scheduled, dayEnd, dayNav, dayMode, mixedMode, navPointCount} = plan
  const visitedCount = done.length

  return (
    <div className="plan-summary" data-testid="plan-summary">
      <div className="ps-row1">
        <span className="ps-title">
          วัน {dayNumber} · {remaining.length} จุด
        </span>
        {scheduled.length > 0 && (
          <span className="ps-visited">
            <span className="d" aria-hidden="true" />
            {visitedCount}/{scheduled.length} มาแล้ว
          </span>
        )}
      </div>

      <div className="ps-row2">
        {day && (
          <div className="ps-stat">
            <span className="k">เริ่ม–เสร็จ</span>
            <span className="v">
              <DayStartEditor
                key={day.id}
                tripId={tripId}
                dayId={day.id}
                dayStartTime={day.dayStartTime}
                // Daily mode re-seeds the start from the clock on every fetch, so the value is
                // not editable then either — the same lock the `ใช้เวลาปัจจุบันเสมอ` flag applies.
                useCurrentTimeAsStart={isDaily || day.useCurrentTimeAsStart}
                onError={onError}
              />
              <span className="ps-dash">–</span>
              {dayEnd}
            </span>
          </div>
        )}
        <span className="ps-sep" aria-hidden="true" />
        <div className="ps-stat">
          {/* Once anything is visited the remaining figure is the useful one — what is left
              to drive today, not what the whole day adds up to (unchanged from ItineraryTab). */}
          <span className="k">{visitedCount > 0 ? 'เหลือเดินทาง' : 'เดินทางรวม'}</span>
          <span className="v">
            {formatDurationMinutes(
              (visitedCount > 0 ? plan.remainingTravelSeconds : plan.totalTravelSeconds) / 60,
            )}
          </span>
        </div>

        {dayNav && (
          <a
            className="ps-nav"
            href={dayNav.url}
            target="_blank"
            rel="noopener noreferrer"
            onClick={() =>
              appInsights.trackEvent(
                {name: 'TripNavHandoff'},
                {
                  scope: 'day',
                  travelMode: dayMode,
                  stopCount: navPointCount,
                  coveredCount: dayNav.coveredCount,
                  overflow: dayNav.overflow,
                  mixedMode,
                },
              )
            }
          >
            <NavIcon /> นำทาง
          </a>
        )}
      </div>
    </div>
  )
}
