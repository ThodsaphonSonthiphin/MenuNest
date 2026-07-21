// frontend/src/pages/trips/components/HourlyPlanner.tsx
// Weather-based retiming (issue #46): the hourly strip mounted from StopDetailSheet (Task 8).
// Two quick actions (coolest daytime/nighttime hour), an horizontal 48h hourly strip with a
// feels-like headline + condition icon per cell, and an apply-preview card that resolves the
// picked hour into a new day-start time via the Task-6 lib/retiming pure functions, then calls
// retimeStop (Task 5) and closes.
import {useMemo, useState} from 'react'
import type {ItineraryDayDto, TripPlaceDto, HourlyReadingDto} from '../../../shared/api/api'
import {useGetHourlyForecastQuery, useRetimeStopMutation} from '../../../shared/api/api'
import {iconUrl} from '../lib/weather'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS} from '../lib/retiming'
import {ClockIcon} from './WeatherIcons'

const WINDOW_HOURS = 48

export function HourlyPlanner({
  day, stopId, place, tripId, onClose,
}: {
  day: ItineraryDayDto; stopId: string; place: TripPlaceDto; tripId: string; onClose: () => void
}) {
  const {data: hours = [], isLoading} = useGetHourlyForecastQuery({lat: place.lat, lng: place.lng, hours: WINDOW_HOURS})
  const [retime, {isLoading: applying}] = useRetimeStopMutation()
  const [picked, setPicked] = useState<HourlyReadingDto | null>(null)

  const offset = useMemo(() => offsetMinutes(day, stopId), [day, stopId])

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
    await retime({tripId, dayId: day.id, stopId, newDayStartTime: preview.newDayStartTime, newAnchorDate: preview.newAnchorDate})
    onClose()
  }

  const todayDate = day.date.slice(0, 10)
  return (
    <div className="sd-hourly">
      <div className="sd-hourly-head"><ClockIcon /> พยากรณ์รายชั่วโมง</div>
      <div className="sd-hourly-quick">
        {dayA && <button type="button" className="btn-text" onClick={() => setPicked(dayA)}>กลางวันเย็นสุด รู้สึก {Math.round(dayA.feelsLikeC as number)}°</button>}
        {dayN && <button type="button" className="btn-text" onClick={() => setPicked(dayN)}>กลางคืนเย็นสุด รู้สึก {Math.round(dayN.feelsLikeC as number)}°</button>}
      </div>
      <div className="sd-hourly-strip">
        {hours.map((h) => {
          const isNextDay = h.displayLocal.slice(0, 10) !== todayDate
          const isPicked = picked?.displayLocal === h.displayLocal
          return (
            <div key={h.displayLocal} className={`sd-hr${h.isDaytime ? ' day' : ' night'}${isPicked ? ' picked' : ''}`}>
              {isNextDay && <span className="sd-hr-div">พรุ่งนี้</span>}
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
              {preview.shift.movesTrip && <p className="warn">ทั้งทริปจะเลื่อนไป {preview.shift.deltaDays} วัน วันอื่นขยับตาม</p>}
              <p className="note">จะปิด “ใช้เวลาปัจจุบันเสมอ” ของวันนี้</p>
              <button type="button" className="btn-primary" disabled={applying} onClick={apply}>ปรับเลย</button>
            </>
          )}
        </div>
      )}
      <button type="button" className="btn-text sd-hourly-close" onClick={onClose}>ปิด</button>
    </div>
  )
}
