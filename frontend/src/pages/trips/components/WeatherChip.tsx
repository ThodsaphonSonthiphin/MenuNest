// frontend/src/pages/trips/components/WeatherChip.tsx
// Renders one Now/On-arrival weather chip for a trip stop. Visual state comes from
// weatherChipState (F2): loading placeholder, no-data glyph+copy, or the Google
// condition icon + temp + rain%. CSS classes (.chip.wx.*) land in F4.

import type {WeatherReadingDto} from '../../../shared/api/api'
import {iconUrl, isRainy, weatherChipState, uvBand} from '../lib/weather'
import {RainDropIcon, NoWeatherIcon, SunIcon} from './WeatherIcons'

const LABEL = {now: 'ตอนนี้', arr: 'ไปถึง'} as const

export function WeatherChip({
  kind,
  reading,
  isLoading,
  isDark = false,
}: {
  kind: 'now' | 'arr'
  reading: WeatherReadingDto | undefined
  isLoading: boolean
  isDark?: boolean
}) {
  const state = weatherChipState(isLoading, reading)

  if (state === 'loading') {
    return <span className={`chip wx ${kind} loading`} aria-hidden="true"><span className="lab">{LABEL[kind]}</span></span>
  }
  if (state === 'nodata') {
    return (
      <span className="chip wx nodata">
        <span className="lab">{LABEL[kind]}</span>
        <NoWeatherIcon />
        ไม่มีข้อมูลอากาศ
      </span>
    )
  }

  const r = reading! // state === 'data' ⇒ reading is present and hasData
  const rainy = kind === 'arr' && isRainy(r.rainPct)
  // Forecast-forward (issue #34): condition + rain% lead, temperature is last and muted.
  return (
    <span className={`chip wx ${kind}${rainy ? ' rainy' : ''}`}>
      <span className="lab">{LABEL[kind]}</span>
      {r.iconBaseUri && <img src={iconUrl(r.iconBaseUri, isDark)} alt={r.description ?? ''} width={22} height={22} />}
      {r.description && <span className="cond">{r.description}</span>}
      {r.rainPct != null && (
        <span className="r"><RainDropIcon />{r.rainPct}%</span>
      )}
      {r.tempC != null && <span className="t">{Math.round(r.tempC)}°</span>}
      {r.feelsLikeC != null && <span className="feels">รู้สึก {Math.round(r.feelsLikeC)}°</span>}
      {r.uvIndex != null && (() => {
        const b = uvBand(r.uvIndex)
        return <span className={`uv ${b.key}`}><SunIcon /> UV {r.uvIndex} {b.word}</span>
      })()}
    </span>
  )
}
