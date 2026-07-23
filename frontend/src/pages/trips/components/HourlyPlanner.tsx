// frontend/src/pages/trips/components/HourlyPlanner.tsx
// Weather-based retiming (issue #46): the hourly planner mounted from StopDetailSheet (Task 8).
// Restyled to match the approved #46 mockup (boxed teal panel: teal-soft header bar, pill quick
// actions with a navy "night" accent, bordered day/night hour cells with a big feels-like numeral,
// a "แผนตอนนี้" current-plan marker + coolest rings + solid-teal selected state, and a tinted
// suggestion card with apply/cancel). Behaviour (offset/suggest/classify/coolest + retimeStop) unchanged.
import {Fragment, useMemo, useState} from 'react'
import type {ItineraryDayDto, TripPlaceDto, HourlyReadingDto} from '../../../shared/api/api'
import {useGetHourlyForecastQuery, useRetimeStopMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {iconUrl, hourlyRolloverLabel} from '../lib/weather'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS, withinHorizon} from '../lib/retiming'
import {SunIcon, MoonIcon, PowerIcon, AlertIcon} from './WeatherIcons'
import {CheckIcon} from './FlagIcons'

const WINDOW_HOURS = 48

export function HourlyPlanner({
  day, stopId, place, tripId, tripDayCount, isDaily, arrival, onClose,
}: {
  day: ItineraryDayDto
  stopId: string
  place: TripPlaceDto
  tripId: string
  tripDayCount: number
  isDaily: boolean
  arrival: string
  onClose: () => void
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

  const todayDate = day.date.slice(0, 10)

  // A cross-day retime moves Trip.StartDate — surface that before the planner closes
  // instead of silently vanishing like a same-day apply.
  if (tripMovedNote) {
    return (
      <div className="sd-hourly">
        <div className="sd-pl-head"><b>อุณหภูมิรายชั่วโมง · {place.name}</b></div>
        <div className="sd-sugg">
          <p className="sd-sugg-line">{tripMovedNote}</p>
          <div className="sd-sugg-foot">
            <button type="button" className="sd-btn-apply" onClick={onClose}><CheckIcon /> ตกลง</button>
          </div>
        </div>
      </div>
    )
  }

  if (isLoading) return <div className="sd-hourly sd-hourly--loading">กำลังโหลดพยากรณ์รายชั่วโมง…</div>
  if (hours.length === 0) return <div className="sd-hourly sd-hourly--empty">ไม่มีข้อมูลอากาศรายชั่วโมง</div>

  const dayA = coolestHour(hours, true)
  const dayN = coolestHour(hours, false)
  // "แผนตอนนี้" marker — the cell the stop currently lands on: the anchor Day's date (todayDate)
  // at the current arrival hour. yyyy-MM-ddTHH prefix match against each cell's displayLocal.
  const planKey = `${todayDate}T${arrival.slice(0, 2)}`

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

  return (
    <div className="sd-hourly">
      <div className="sd-pl-head">
        <b>อุณหภูมิรายชั่วโมง · {place.name}</b>
        <button type="button" className="sd-pl-x" onClick={onClose} aria-label="ปิด">×</button>
      </div>

      <div className="sd-pl-quick">
        {dayA && (
          <button
            type="button"
            className={`sd-qbtn${picked?.displayLocal === dayA.displayLocal ? ' sel' : ''}`}
            onClick={() => setPicked(dayA)}
          >
            <SunIcon /> กลางวันเย็นสุด รู้สึก {Math.round(dayA.feelsLikeC as number)}°
          </button>
        )}
        {dayN && (
          <button
            type="button"
            className={`sd-qbtn night${picked?.displayLocal === dayN.displayLocal ? ' sel' : ''}`}
            onClick={() => setPicked(dayN)}
          >
            <MoonIcon /> กลางคืนเย็นสุด รู้สึก {Math.round(dayN.feelsLikeC as number)}°
          </button>
        )}
      </div>

      <div className="sd-pl-scroll">
        {hours.map((h, i) => {
          const hDate = h.displayLocal.slice(0, 10)
          const isRollover = i > 0 && hours[i - 1].displayLocal.slice(0, 10) !== hDate
          const isPicked = picked?.displayLocal === h.displayLocal
          const isPlan = h.displayLocal.slice(0, 13) === planKey
          const isCoolDay = !isPicked && dayA?.displayLocal === h.displayLocal
          const isCoolNight = !isPicked && dayN?.displayLocal === h.displayLocal
          const cls = ['sd-hr', h.isDaytime ? 'day' : 'night']
          if (isPlan) cls.push('plan')
          if (isPicked) {
            cls.push('sel')
            if (!h.isDaytime) cls.push('nightsel')
          } else if (isCoolDay) cls.push('coolday')
          else if (isCoolNight) cls.push('coolnight')
          return (
            <Fragment key={h.displayLocal}>
              {isRollover && <div className="sd-daydiv">{hourlyRolloverLabel(hDate, todayDate)}</div>}
              <button type="button" className={cls.join(' ')} disabled={isDaily} onClick={isDaily ? undefined : () => setPicked(h)}>
                {isPlan && <span className="sd-hr-tag">แผนตอนนี้</span>}
                <span className="sd-hr-time mono">{h.displayLocal.slice(11, 16)}</span>
                {h.iconBaseUri && <img className="sd-hr-ic" src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={19} height={19} />}
                {h.feelsLikeC != null && <span className="sd-hr-f mono">{Math.round(h.feelsLikeC)}°</span>}
                <span className="sd-hr-fl">รู้สึก</span>
              </button>
            </Fragment>
          )
        })}
      </div>

      {isDaily ? (
        <div className="sd-sugg"><p className="sd-sugg-line">โหมดประจำวันเริ่มจากเวลาปัจจุบันเสมอ — ปรับเวลาตามอากาศไม่ได้</p></div>
      ) : preview && (
        <div className="sd-sugg">
          {preview.unreachable ? (
            <p className="sd-sugg-line">ช่วงเวลานี้ไปถึงไม่ทัน — จุดนี้อยู่ลึกในวันเกินไป</p>
          ) : (
            <>
              <div className="sd-sugg-line">
                เลือกไปถึง <b className="mono">{picked!.displayLocal.slice(11, 16)}</b>
                {picked!.feelsLikeC != null && <> · <b>รู้สึก {Math.round(picked!.feelsLikeC)}°</b></>}
              </div>
              <div className="sd-sugg-res">
                เริ่มวันเป็น <span className="pill mono">{preview.newDayStartTime.slice(0, 5)}</span> → จุดนี้ถึง{' '}
                <span className="pill mono">{picked!.displayLocal.slice(11, 16)}</span>
              </div>
              {preview.shift.movesTrip &&
                (tripDayCount === 1 ? (
                  <div className="sd-sugg-off"><PowerIcon /> วันของทริปจะย้ายไป {hourlyRolloverLabel(preview.newAnchorDate, todayDate)}</div>
                ) : (
                  <div className="sd-sugg-warn">
                    <AlertIcon /> <span>ทั้งทริปจะเลื่อนไป {preview.shift.deltaDays} วัน — วันอื่นในทริปขยับตาม</span>
                  </div>
                ))}
              <div className="sd-sugg-off"><PowerIcon /> จะปิด “ใช้เวลาปัจจุบันเสมอ” ให้อัตโนมัติ</div>
              {applyError && <p className="trips-field-error">{applyError}</p>}
              <div className="sd-sugg-foot">
                <button type="button" className="sd-btn-apply" disabled={applying} onClick={apply}><CheckIcon /> ปรับเลย</button>
                <button type="button" className="sd-btn-cancel" onClick={() => setPicked(null)}>ยกเลิก</button>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  )
}
