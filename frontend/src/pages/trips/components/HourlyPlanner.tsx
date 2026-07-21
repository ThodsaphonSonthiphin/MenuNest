// frontend/src/pages/trips/components/HourlyPlanner.tsx
// Weather-based retiming (issue #46): the hourly strip mounted from StopDetailSheet (Task 8).
// Two quick actions (coolest daytime/nighttime hour), an horizontal 48h hourly strip with a
// feels-like headline + condition icon per cell, and an apply-preview card that resolves the
// picked hour into a new day-start time via the Task-6 lib/retiming pure functions, then calls
// retimeStop (Task 5) and closes.
import {useMemo, useState} from 'react'
import type {ItineraryDayDto, TripPlaceDto, HourlyReadingDto} from '../../../shared/api/api'
import {useGetHourlyForecastQuery, useRetimeStopMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {iconUrl} from '../lib/weather'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS, withinHorizon} from '../lib/retiming'
import {ClockIcon} from './WeatherIcons'

const WINDOW_HOURS = 48

export function HourlyPlanner({
  day, stopId, place, tripId, tripDayCount, onClose,
}: {
  day: ItineraryDayDto; stopId: string; place: TripPlaceDto; tripId: string; tripDayCount: number; onClose: () => void
}) {
  const {data: allHours = [], isLoading} = useGetHourlyForecastQuery({lat: place.lat, lng: place.lng, hours: WINDOW_HOURS})
  const [retime, {isLoading: applying}] = useRetimeStopMutation()
  const [picked, setPicked] = useState<HourlyReadingDto | null>(null)
  const [applyError, setApplyError] = useState<string | null>(null)
  const [tripMovedNote, setTripMovedNote] = useState<string | null>(null)

  const offset = useMemo(() => offsetMinutes(day, stopId), [day, stopId])
  // H2 guard: never let the user pick an hour that's already past or beyond the 240h
  // forecast horizon. Filtering here (rather than per-cell) also gives the coolest
  // quick-actions below — derived from this same list — the same guard for free.
  const hours = useMemo(
    () => allHours.filter((h) => withinHorizon(Date.parse(h.displayLocal), Date.now())),
    [allHours],
  )

  // A cross-day retime moves Trip.StartDate — surface that before the planner closes
  // instead of silently vanishing like a same-day apply.
  if (tripMovedNote) {
    return (
      <div className="sd-hourly">
        <div className="sd-hourly-preview">
          <p>{tripMovedNote}</p>
          <button type="button" className="btn-primary" onClick={onClose}>ตกลง</button>
        </div>
      </div>
    )
  }

  if (isLoading) return <div className="sd-hourly sd-hourly--loading">กำลังโหลดพยากรณ์รายชั่วโมง…</div>
  if (hours.length === 0) return <div className="sd-hourly sd-hourly--empty">ไม่มีข้อมูลอากาศรายชั่วโมง</div>

  const dayA = coolestHour(hours, true)
  const dayN = coolestHour(hours, false)

  const preview = (() => {
    if (!picked || offset == null) return null
    const t = new Date(picked.displayLocal)
    const targetMin = t.getHours() * 60 + t.getMinutes()
    const startMin = suggestedStartMinutes(targetMin, offset)
    if (startMin < 0) return {unreachable: true as const}
    const targetDate = picked.displayLocal.slice(0, 10)
    const shift = classifyShift(targetDate, day.date)
    return {
      unreachable: false as const,
      newDayStartTime: minutesToHHMMSS(startMin),
      newAnchorDate: targetDate,
      shift,
    }
  })()

  const apply = async () => {
    if (!preview || preview.unreachable) return
    setApplyError(null)
    try {
      const res = await retime({
        tripId, dayId: day.id, stopId,
        newDayStartTime: preview.newDayStartTime, newAnchorDate: preview.newAnchorDate,
      }).unwrap()
      if (res.movedTrip) {
        // Keep the planner open just long enough to show the moved trip start date;
        // the user dismisses it explicitly (see the tripMovedNote render above).
        setTripMovedNote(`ทริปเลื่อนเป็น ${res.tripStartAfter}`)
      } else {
        onClose()
      }
    } catch (err) {
      setApplyError(getErrorMessage(err))
    }
  }

  const todayDate = day.date.slice(0, 10)
  // Date-rollover label for the hour strip: the window spans 48h from "now" (not midnight-aligned),
  // so it can cross THREE calendar dates. Label by day-delta from the anchor date, not a fixed
  // "today vs not-today" check (see classifyShift's date-diff idiom in lib/retiming.ts).
  const rolloverLabel = (dateStr: string) => {
    const deltaDays = Math.round((Date.parse(dateStr) - Date.parse(todayDate)) / 86_400_000)
    return deltaDays === 1
      ? 'พรุ่งนี้'
      : new Date(`${dateStr}T00:00:00`).toLocaleDateString('th-TH', {weekday: 'short', day: 'numeric', month: 'short'})
  }
  return (
    <div className="sd-hourly">
      <div className="sd-hourly-head"><ClockIcon /> พยากรณ์รายชั่วโมง</div>
      <div className="sd-hourly-quick">
        {dayA && <button type="button" className="btn-text" onClick={() => setPicked(dayA)}>กลางวันเย็นสุด รู้สึก {Math.round(dayA.feelsLikeC as number)}°</button>}
        {dayN && <button type="button" className="btn-text" onClick={() => setPicked(dayN)}>กลางคืนเย็นสุด รู้สึก {Math.round(dayN.feelsLikeC as number)}°</button>}
      </div>
      <div className="sd-hourly-strip">
        {hours.map((h, i) => {
          const hDate = h.displayLocal.slice(0, 10)
          const isRollover = i > 0 && hours[i - 1].displayLocal.slice(0, 10) !== hDate
          const isPicked = picked?.displayLocal === h.displayLocal
          return (
            <div key={h.displayLocal} className={`sd-hr${h.isDaytime ? ' day' : ' night'}${isPicked ? ' picked' : ''}`}>
              {isRollover && <span className="sd-hr-div">{rolloverLabel(hDate)}</span>}
              <button type="button" onClick={() => setPicked(h)}>
                <span className="sd-hr-time">{h.displayLocal.slice(11, 16)}</span>
                {h.iconBaseUri && <img src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={20} height={20} />}
                {h.feelsLikeC != null && <span className="sd-hr-feels">รู้สึก {Math.round(h.feelsLikeC)}°</span>}
              </button>
            </div>
          )
        })}
      </div>
      {preview && (
        <div className="sd-hourly-preview">
          {preview.unreachable ? (
            <p>ช่วงเวลานี้ไปถึงไม่ทัน — จุดนี้อยู่ลึกในวันเกินไป</p>
          ) : (
            <>
              <p>เริ่มวันใหม่ {preview.newDayStartTime.slice(0, 5)} → ถึงตอน {picked!.displayLocal.slice(11, 16)}</p>
              {preview.shift.movesTrip && (
                tripDayCount === 1 ? (
                  <p className="note">วันของทริปจะย้ายไป {rolloverLabel(preview.newAnchorDate)}</p>
                ) : (
                  <p className="warn">ทั้งทริปจะเลื่อนไป {preview.shift.deltaDays} วัน วันอื่นขยับตาม</p>
                )
              )}
              <p className="note">จะปิด “ใช้เวลาปัจจุบันเสมอ” ของวันนี้</p>
              {applyError && <p className="trips-field-error">{applyError}</p>}
              <button type="button" className="btn-primary" disabled={applying} onClick={apply}>ปรับเลย</button>
            </>
          )}
        </div>
      )}
      <button type="button" className="btn-text sd-hourly-close" onClick={onClose}>ปิด</button>
    </div>
  )
}
