import {describe, it, expect} from 'vitest'
import {offsetMinutes, suggestedStartMinutes, classifyShift, coolestHour, minutesToHHMMSS, withinHorizon} from './retiming'
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
  it('sums correctly when stops are passed out of sequence order', () => {
    // Same legs/dwell as the first test, but the array itself is NOT in sequence order —
    // offsetMinutes must sort by `sequence` internally rather than trust array order.
    const d = day([stop('1', 1, 45, 900), stop('0', 0, 60, null)])
    expect(offsetMinutes(d, '1')).toBe(75)
  })
  it('rounds a non-exact leg duration (925s -> 15m)', () => {
    expect(offsetMinutes(day([stop('0', 0, 0, 925)]), '0')).toBe(15)
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
  it('picks the earliest hour on a genuine tie', () => {
    // Two daytime hours tied on feels-like, given out of chronological order — the
    // strict `<` comparison in coolestHour must not let the later tie overwrite the first.
    const tied = [hr(14, true, 35), hr(10, true, 35), hr(18, true, 40)]
    expect(coolestHour(tied, true)?.displayLocal).toContain('T10:')
  })
})

describe('minutesToHHMMSS', () => {
  it('formats to HH:mm:ss', () => expect(minutesToHHMMSS(10 * 60 + 45)).toBe('10:45:00'))
  it('wraps a value past 24h', () => expect(minutesToHHMMSS(1470)).toBe('00:30:00'))
  it('wraps a negative value into the previous day', () => expect(minutesToHHMMSS(-15)).toBe('23:45:00'))
})

describe('withinHorizon', () => {
  const now = 1_700_000_000_000
  const HOUR = 3_600_000
  it('is false for a target in the past', () => expect(withinHorizon(now - HOUR, now)).toBe(false))
  it('is true for a target within the 240h forecast window', () => expect(withinHorizon(now + 100 * HOUR, now)).toBe(true))
  it('is false for a target beyond the 240h horizon', () => expect(withinHorizon(now + 241 * HOUR, now)).toBe(false))
})
