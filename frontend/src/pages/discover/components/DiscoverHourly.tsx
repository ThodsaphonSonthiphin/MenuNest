// frontend/src/pages/discover/components/DiscoverHourly.tsx
// Issue #47: display-only hourly forecast strip in the Discover place-detail sheet.
// Reuses the coordinate-based hourly query + helpers shipped for trips (#46). No retiming (ADR-124).
import {Fragment} from 'react'
import {useGetHourlyForecastQuery} from '../../../shared/api/api'
import {iconUrl, hourlyRolloverLabel} from '../../trips/lib/weather'
import {withinHorizon} from '../../trips/lib/retiming'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

const WINDOW_HOURS = 48

// Floor the live clock to the top of the current hour. The current hourly bucket's local
// timestamp is the hour start (already minutes in the past by wall-clock), so filtering on
// `>= Date.now()` would drop it and mislabel the NEXT hour as "ตอนนี้". Flooring keeps the
// genuinely-current bucket while still dropping older hours and anything past the horizon.
function startOfCurrentHourMs(): number {
  const d = new Date()
  d.setMinutes(0, 0, 0)
  return d.getTime()
}

// Rain probability marker (inline SVG raindrop — no emoji, per project convention).
function RainDrop() {
  return (
    <svg viewBox="0 0 24 24" width={9} height={9} fill="currentColor" aria-hidden="true">
      <path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z" />
    </svg>
  )
}

export function DiscoverHourly({place}: {place: DiscoverPlaceView}) {
  const {data: allHours = [], isLoading} = useGetHourlyForecastQuery({
    lat: place.lat, lng: place.lng, hours: WINDOW_HOURS,
  })
  const fromMs = startOfCurrentHourMs()
  const hours = allHours.filter((h) => withinHorizon(Date.parse(h.displayLocal), fromMs))

  if (isLoading) {
    return (
      <section className="disc-wx">
        <div className="disc-sec-lab">อากาศรายชั่วโมง</div>
        <div className="disc-wx-strip" aria-hidden="true">
          {Array.from({length: 6}).map((_, i) => <div key={i} className="disc-wx-sk" />)}
        </div>
      </section>
    )
  }
  if (hours.length === 0) {
    return (
      <section className="disc-wx">
        <div className="disc-sec-lab">อากาศรายชั่วโมง</div>
        <p className="disc-wx-empty">ไม่มีข้อมูลอากาศรายชั่วโมง</p>
      </section>
    )
  }

  const anchorDate = hours[0].displayLocal.slice(0, 10)
  const now = hours[0]
  return (
    <section className="disc-wx">
      <div className="disc-wx-head">
        <span className="disc-sec-lab">อากาศรายชั่วโมง</span>
        <span className="disc-wx-now">
          {now.iconBaseUri && <img src={iconUrl(now.iconBaseUri, false)} alt="" width={15} height={15} />}
          <span>ตอนนี้</span>
          {now.tempC != null && <b>{Math.round(now.tempC)}°</b>}
          {now.feelsLikeC != null && <span className="fl">รู้สึก {Math.round(now.feelsLikeC)}°</span>}
        </span>
      </div>
      <div className="disc-wx-strip">
        {hours.map((h, i) => {
          const hDate = h.displayLocal.slice(0, 10)
          const isRollover = i > 0 && hours[i - 1].displayLocal.slice(0, 10) !== hDate
          return (
            <Fragment key={h.displayLocal}>
              {isRollover && <div className="disc-daydiv">{hourlyRolloverLabel(hDate, anchorDate)}</div>}
              <div className={`disc-hr ${h.isDaytime ? 'day' : 'night'}`}>
                {i === 0 && <span className="disc-hr-now">ตอนนี้</span>}
                <span className="disc-hr-time">{h.displayLocal.slice(11, 16)}</span>
                {h.iconBaseUri && <img src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={22} height={22} />}
                {h.tempC != null && <span className="disc-hr-temp">{Math.round(h.tempC)}°</span>}
                {h.feelsLikeC != null && <span className="disc-hr-feels">รู้สึก {Math.round(h.feelsLikeC)}°</span>}
                {h.rainPct != null && (
                  h.rainPct > 0
                    ? <span className="disc-hr-rain"><RainDrop />{h.rainPct}%</span>
                    : <span className="disc-hr-rain dry">แห้ง</span>
                )}
              </div>
            </Fragment>
          )
        })}
      </div>
    </section>
  )
}
