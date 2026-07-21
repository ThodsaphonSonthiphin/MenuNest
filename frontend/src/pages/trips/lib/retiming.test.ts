import {describe, it, expect} from 'vitest'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS} from './retiming'
import type {ItineraryDayDto, HourlyReadingDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, isVisited: false,
  legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated' as const},
})
const day = (stops: ReturnType<typeof stop>[]): ItineraryDayDto =>
  ({id: 'd1', date: '2026-07-12', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops})

const hr = (h: number, daytime: boolean, feels: number): HourlyReadingDto => ({
  displayLocal: `2026-07-12T${String(h).padStart(2, '0')}:00:00`, isDaytime: daytime,
  tempC: feels - 5, feelsLikeC: feels, conditionType: 'CLEAR', iconBaseUri: null, rainPct: 0, uvIndex: 0,
})

describe('offsetMinutes', () => {
  it('sums legs (rounded) + dwell up to the anchor, dayStart-independent', () => {
    // stop0 approach leg null; stop1 leg 900s(15m); anchor = stop1. offset = leg[0](0)+leg[1](15) + dwell[0](60) = 75
    const d = day([stop('0', 0, 60, null), stop('1', 1, 45, 900)])
    expect(offsetMinutes(d, '1')).toBe(75)
  })
  it('is 0 for the first stop with no approach leg', () => {
    expect(offsetMinutes(day([stop('0', 0, 60, null)]), '0')).toBe(0)
  })
  it('returns null for an unknown stop', () => {
    expect(offsetMinutes(day([stop('0', 0, 60, null)]), 'zzz')).toBeNull()
  })
})

describe('suggestedStartMinutes', () => {
  it('is target − offset', () => expect(suggestedStartMinutes(12 * 60, 75)).toBe(10 * 60 + 45))
  it('is negative when the target is unreachably early', () => expect(suggestedStartMinutes(30, 75)).toBe(-45))
})

describe('classifyShift', () => {
  it('same day when dates match', () =>
    expect(classifyShift('2026-07-12', '2026-07-12')).toEqual({sameDay: true, deltaDays: 0, movesTrip: false}))
  it('cross day moves the trip', () =>
    expect(classifyShift('2026-07-13', '2026-07-12')).toEqual({sameDay: false, deltaDays: 1, movesTrip: true}))
})

describe('coolestHour', () => {
  const hours = [hr(13, true, 39), hr(15, true, 41), hr(22, false, 30), hr(2, false, 28)]
  it('picks min feels-like daytime hour', () => expect(coolestHour(hours, true)?.displayLocal).toContain('T13:'))
  it('picks min feels-like nighttime hour', () => expect(coolestHour(hours, false)?.displayLocal).toContain('T02:'))
  it('is null when the half has no candidates', () => expect(coolestHour([hr(13, true, 39)], false)).toBeNull())
})

describe('minutesToHHMMSS', () => {
  it('formats to HH:mm:ss', () => expect(minutesToHHMMSS(10 * 60 + 45)).toBe('10:45:00'))
})
