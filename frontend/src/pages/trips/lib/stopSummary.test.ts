import {describe, it, expect} from 'vitest'
import {buildStopSummary} from './stopSummary'
import type {WeatherReadingDto} from '../../../shared/api/api'
import type {TimingFlag} from '../hooks/useSchedule'

const reading = (over: Partial<WeatherReadingDto>): WeatherReadingDto => ({
  stopId: 's1',
  hasData: true,
  conditionType: null,
  iconBaseUri: 'https://maps.gstatic.com/weather/icon',
  tempC: 30,
  rainPct: 10,
  description: 'มีเมฆบางส่วน',
  uvIndex: null,
  feelsLikeC: null,
  ...over,
})

describe('buildStopSummary', () => {
  it('leads with the arrival-forecast description and passes the icon through', () => {
    const s = buildStopSummary({arrivalReading: reading({}), dwellMinutes: 90, flag: null})
    expect(s.weather).toEqual({iconBaseUri: 'https://maps.gstatic.com/weather/icon', label: 'มีเมฆบางส่วน'})
  })

  it('appends rain% when rain is at/above the rainy threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({description: 'ฝนตก', rainPct: 60}), dwellMinutes: 60, flag: null})
    expect(s.weather?.label).toBe('ฝนตก 60%')
  })

  it('omits rain% below the rainy threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({description: 'แดดจัด', rainPct: 20}), dwellMinutes: 60, flag: null})
    expect(s.weather?.label).toBe('แดดจัด')
  })

  it('returns null weather when the reading has no data', () => {
    const s = buildStopSummary({arrivalReading: reading({hasData: false}), dwellMinutes: 60, flag: null})
    expect(s.weather).toBeNull()
  })

  it('returns null weather when no reading is supplied', () => {
    const s = buildStopSummary({dwellMinutes: 60, flag: null})
    expect(s.weather).toBeNull()
  })

  it('formats the dwell text with the shared helper', () => {
    const s = buildStopSummary({dwellMinutes: 90, flag: null})
    expect(s.dwellText).toBe('อยู่ 1 ชม. 30 น.')
  })

  it('surfaces a timing flag as its severity + reason line', () => {
    const flag: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'on-break', reopenAt: '13:00'}
    const s = buildStopSummary({dwellMinutes: 60, flag})
    expect(s.flag).toEqual({severity: 'problem', label: 'ปิดพักช่วงนี้ · เปิดอีกที 13:00'})
  })

  it('has null flag when there is no flag', () => {
    const s = buildStopSummary({dwellMinutes: 60, flag: null})
    expect(s.flag).toBeNull()
  })

  it('flags UV on arrival at/above the default threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({uvIndex: 9}), dwellMinutes: 60, flag: null})
    expect(s.alerts).toEqual({uv: 9})
  })

  it('flags feels-like at/above the default threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({feelsLikeC: 41}), dwellMinutes: 60, flag: null})
    expect(s.alerts).toEqual({feels: 41})
  })

  it('no alerts when both axes are turned off (0)', () => {
    const s = buildStopSummary({
      arrivalReading: reading({uvIndex: 11, feelsLikeC: 45}),
      dwellMinutes: 60,
      flag: null,
      uvWarn: 0,
      feelsWarn: 0,
    })
    expect(s.alerts).toEqual({})
  })

  it('no alerts when arrival has no data', () => {
    const s = buildStopSummary({arrivalReading: reading({hasData: false, uvIndex: 11}), dwellMinutes: 60, flag: null})
    expect(s.alerts).toEqual({})
  })
})
