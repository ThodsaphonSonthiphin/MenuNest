import {describe, it, expect} from 'vitest'
import {
  weatherWindow, iconUrl, isRainy, RAIN_TINT_THRESHOLD, weatherChipState, arrivalIso, buildWeatherBatches,
  uvBand, effectiveThreshold, weatherAlertBadges, hourlyRolloverLabel,
} from './weather'
import type {WeatherReadingDto} from '../../../shared/api/api'

const HOUR = 3600_000
const H240 = 240 * HOUR

describe('weatherWindow', () => {
  const now = 1_000_000_000
  it('is past when arrival < now', () => expect(weatherWindow(now - 1, now)).toBe('past'))
  it('is ok at exactly now + 240h (inclusive)', () => expect(weatherWindow(now + H240, now)).toBe('ok'))
  it('is beyond just past 240h', () => expect(weatherWindow(now + H240 + 60_000, now)).toBe('beyond'))
  it('is ok inside the window', () => expect(weatherWindow(now + HOUR, now)).toBe('ok'))
})

describe('iconUrl', () => {
  const base = 'https://maps.gstatic.com/weather/v1/cloudy'
  it('appends .svg in light theme', () => expect(iconUrl(base, false)).toBe(`${base}.svg`))
  it('appends _dark.svg in dark theme', () => expect(iconUrl(base, true)).toBe(`${base}_dark.svg`))
})

describe('isRainy', () => {
  it('is true at the threshold', () => expect(isRainy(RAIN_TINT_THRESHOLD)).toBe(true))
  it('is false just below', () => expect(isRainy(RAIN_TINT_THRESHOLD - 1)).toBe(false))
  it('treats null as not rainy', () => expect(isRainy(null)).toBe(false))
})

describe('weatherChipState', () => {
  it('is loading when loading and no reading yet', () => expect(weatherChipState(true, undefined)).toBe('loading'))
  it('is nodata when reading missing and not loading', () => expect(weatherChipState(false, undefined)).toBe('nodata'))
  it('is nodata when reading has no data', () =>
    expect(weatherChipState(false, {stopId: 's', hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null, description: null, uvIndex: null, feelsLikeC: null})).toBe('nodata'))
  it('is data when reading has data', () =>
    expect(weatherChipState(false, {stopId: 's', hasData: true, conditionType: 'CLOUDY', iconBaseUri: 'x', tempC: 29, rainPct: 20, description: 'y', uvIndex: null, feelsLikeC: null})).toBe('data'))
})

describe('arrivalIso', () => {
  it('joins day date + HH:MM into a local wall-clock ISO', () =>
    expect(arrivalIso('2026-07-12', '14:30')).toBe('2026-07-12T14:30:00'))
  it('tolerates a full timestamp date and HH:MM:SS', () =>
    expect(arrivalIso('2026-07-12T00:00:00', '14:30:00')).toBe('2026-07-12T14:30:00'))
})

describe('buildWeatherBatches', () => {
  const now = Date.parse('2026-07-12T00:00:00')
  const stops = [
    {stopId: 's1', lat: 13.7, lng: 100.5, arrivalIso: '2026-07-12T09:00:00'},        // in window
    {stopId: 's2', lat: NaN, lng: 100.5, arrivalIso: '2026-07-12T10:00:00'},         // no coords -> dropped from both
    {stopId: 's3', lat: 18.8, lng: 98.9, arrivalIso: '2026-09-01T10:00:00'},         // beyond horizon
  ]
  const {now: nowPts, arrival} = buildWeatherBatches(stops, now)

  it('includes every finite-coord stop in the Now batch', () =>
    expect(nowPts.map((p) => p.stopId)).toEqual(['s1', 's3']))
  it('includes only in-window stops in the On-arrival batch, carrying arrivalIso', () => {
    expect(arrival.map((p) => p.stopId)).toEqual(['s1'])
    expect(arrival[0].arrivalIso).toBe('2026-07-12T09:00:00')
  })
})

describe('uvBand', () => {
  it('bands the WHO scale', () => {
    expect(uvBand(2).word).toBe('ต่ำ')
    expect(uvBand(3).key).toBe('mod')
    expect(uvBand(5).word).toBe('ปานกลาง')
    expect(uvBand(6).key).toBe('high')
    expect(uvBand(7).word).toBe('สูง')
    expect(uvBand(8).key).toBe('vhigh')
    expect(uvBand(10).word).toBe('สูงมาก')
    expect(uvBand(11).key).toBe('ext')
    expect(uvBand(13).word).toBe('อันตราย')
  })
})

describe('effectiveThreshold', () => {
  it('null -> default', () => expect(effectiveThreshold(null, 6)).toBe(6))
  it('undefined -> default', () => expect(effectiveThreshold(undefined, 40)).toBe(40))
  it('0 -> off', () => expect(effectiveThreshold(0, 6)).toBeNull())
  it('N -> N', () => expect(effectiveThreshold(8, 6)).toBe(8))
})

const wr = (over: Partial<WeatherReadingDto>): WeatherReadingDto => ({
  stopId: 's', hasData: true, conditionType: null, iconBaseUri: null,
  tempC: 30, rainPct: 0, description: null, uvIndex: null, feelsLikeC: null, ...over,
})

describe('weatherAlertBadges', () => {
  it('empty without a usable arrival reading', () => {
    expect(weatherAlertBadges(undefined, null, null)).toEqual({})
    expect(weatherAlertBadges(wr({hasData: false}), null, null)).toEqual({})
  })
  it('uv badge at/above default 6', () => expect(weatherAlertBadges(wr({uvIndex: 9}), null, null)).toEqual({uv: 9}))
  it('no uv badge below threshold', () => expect(weatherAlertBadges(wr({uvIndex: 2}), null, null)).toEqual({}))
  it('feels badge rounds vs default 40', () => expect(weatherAlertBadges(wr({feelsLikeC: 40.4}), null, null)).toEqual({feels: 40}))
  it('0 disables an axis', () => expect(weatherAlertBadges(wr({uvIndex: 11, feelsLikeC: 45}), 0, 0)).toEqual({}))
  it('custom thresholds both fire', () => expect(weatherAlertBadges(wr({uvIndex: 3, feelsLikeC: 38}), 3, 38)).toEqual({uv: 3, feels: 38}))
})

describe('hourlyRolloverLabel', () => {
  it('labels the day after the anchor as พรุ่งนี้', () => {
    expect(hourlyRolloverLabel('2026-07-22', '2026-07-21')).toBe('พรุ่งนี้')
  })
  it('labels a further-out day with its date, not พรุ่งนี้', () => {
    const label = hourlyRolloverLabel('2026-07-23', '2026-07-21')
    expect(label).not.toBe('พรุ่งนี้')
    expect(label).toContain('23')
  })
  it('does not call the anchor day itself พรุ่งนี้', () => {
    expect(hourlyRolloverLabel('2026-07-21', '2026-07-21')).not.toBe('พรุ่งนี้')
  })
})