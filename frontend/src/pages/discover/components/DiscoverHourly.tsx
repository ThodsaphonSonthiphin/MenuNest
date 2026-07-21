// frontend/src/pages/discover/components/DiscoverHourly.tsx
// Issue #47: display-only hourly forecast strip in the Discover place-detail sheet.
// Reuses the coordinate-based hourly query + helpers shipped for trips (#46). No retiming (ADR-124).
import {useGetHourlyForecastQuery} from '../../../shared/api/api'
import {iconUrl, hourlyRolloverLabel} from '../../trips/lib/weather'
import {withinHorizon} from '../../trips/lib/retiming'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

const WINDOW_HOURS = 48

export function DiscoverHourly({place}: {place: DiscoverPlaceView}) {
  const {data: allHours = [], isLoading} = useGetHourlyForecastQuery({
    lat: place.lat, lng: place.lng, hours: WINDOW_HOURS,
  })
  // Drop past hours + anything beyond the 10-day forecast horizon -- same guard as the trips planner.
  const hours = allHours.filter((h) => withinHorizon(Date.parse(h.displayLocal), Date.now()))

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
          ตอนนี้ <b>{now.tempC != null ? `${Math.round(now.tempC)}°` : '—'}</b>
          {now.feelsLikeC != null && <span className="fl">รู้สึก {Math.round(now.feelsLikeC)}°</span>}
        </span>
      </div>
      <div className="disc-wx-strip">
        {hours.map((h, i) => {
          const hDate = h.displayLocal.slice(0, 10)
          const isRollover = i > 0 && hours[i - 1].displayLocal.slice(0, 10) !== hDate
          return (
            <div key={h.displayLocal} className={`disc-hr ${h.isDaytime ? 'day' : 'night'}`}>
              {isRollover && <span className="disc-hr-div">{hourlyRolloverLabel(hDate, anchorDate)}</span>}
              <span className="disc-hr-time">{i === 0 ? 'ตอนนี้' : h.displayLocal.slice(11, 16)}</span>
              {h.iconBaseUri && <img src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={22} height={22} />}
              {h.tempC != null && <span className="disc-hr-temp">{Math.round(h.tempC)}°</span>}
              {h.feelsLikeC != null && <span className="disc-hr-feels">รู้สึก {Math.round(h.feelsLikeC)}°</span>}
              <span className={`disc-hr-rain${h.rainPct ? '' : ' dry'}`}>{h.rainPct ? `ฝน ${h.rainPct}%` : 'แห้ง'}</span>
            </div>
          )
        })}
      </div>
    </section>
  )
}