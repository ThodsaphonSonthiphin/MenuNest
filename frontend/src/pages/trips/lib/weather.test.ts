import {describe, it, expect} from 'vitest'
import {
  weatherWindow, iconUrl, isRainy, RAIN_TINT_THRESHOLD, weatherChipState, arrivalIso, buildWeatherBatches,
} from './weather'

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
    expect(weatherChipState(false, {stopId: 's', hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null, description: null})).toBe('nodata'))
  it('is data when reading has data', () =>
    expect(weatherChipState(false, {stopId: 's', hasData: true, conditionType: 'CLOUDY', iconBaseUri: 'x', tempC: 29, rainPct: 20, description: 'y'})).toBe('data'))
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
