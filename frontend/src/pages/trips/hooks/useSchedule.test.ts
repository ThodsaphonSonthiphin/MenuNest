// frontend/src/pages/trips/hooks/useSchedule.test.ts
import {describe, it, expect} from 'vitest'
import {computeSchedule, dayOfWeek, isOpenAt, offWindowFlag, closedFlag, composeFlags} from './useSchedule'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const,
  legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated' as const},
})

describe('computeSchedule', () => {
  it('cascades arrival = prev depart + leg; depart = arrival + dwell', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60), stop('3', 2, 90, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:00'); expect(s[0].depart).toBe('10:00')
    expect(s[1].arrival).toBe('10:25'); expect(s[1].depart).toBe('11:10')
    expect(s[2].arrival).toBe('11:40'); expect(s[2].depart).toBe('13:10')
  })

  it('includes a populated leg on the first stop (Approach leg) in the cascade', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 60, 10 * 60), stop('2', 1, 45, 25 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:10') // dayStart + 10-minute Approach leg
    expect(s[0].depart).toBe('10:10')
    expect(s[1].arrival).toBe('10:35')
  })
})

describe('dayOfWeek', () => {
  it('maps yyyy-MM-dd to 0=Sunday..6=Saturday (UTC, timezone-stable)', () => {
    expect(dayOfWeek('2026-11-14')).toBe(6) // Saturday
    expect(dayOfWeek('2026-11-15')).toBe(0) // Sunday
  })
})

describe('isOpenAt', () => {
  const hours = (periods: unknown) => JSON.stringify({periods})

  it('returns null when hours are unknown (no JSON / empty periods / malformed)', () => {
    expect(isOpenAt(null, 1, 600)).toBeNull()
    expect(isOpenAt(undefined, 1, 600)).toBeNull()
    expect(isOpenAt(hours([]), 1, 600)).toBeNull() // 24h / always-open
    expect(isOpenAt('{not json', 1, 600)).toBeNull()
  })

  it('evaluates a same-day open period', () => {
    const j = hours([{open: {day: 1, hour: 9, minute: 0}, close: {day: 1, hour: 17, minute: 0}}]) // Mon 09:00–17:00
    expect(isOpenAt(j, 1, 10 * 60)).toBe(true)  // 10:00 Mon → open
    expect(isOpenAt(j, 1, 8 * 60)).toBe(false)  // 08:00 Mon → before open
    expect(isOpenAt(j, 1, 17 * 60)).toBe(false) // 17:00 Mon → at close (exclusive)
    expect(isOpenAt(j, 2, 10 * 60)).toBe(false) // Tue → no period
  })

  it('handles an overnight period crossing midnight', () => {
    const j = hours([{open: {day: 5, hour: 18, minute: 0}, close: {day: 6, hour: 2, minute: 0}}]) // Fri 18:00 → Sat 02:00
    expect(isOpenAt(j, 5, 23 * 60)).toBe(true) // Fri 23:00 → open
    expect(isOpenAt(j, 6, 60)).toBe(true)      // Sat 01:00 → still open
    expect(isOpenAt(j, 6, 3 * 60)).toBe(false) // Sat 03:00 → closed
  })

  it('treats the Google 24/7 sentinel (open day-0 00:00, no close) as open every day', () => {
    const j = hours([{open: {day: 0, hour: 0, minute: 0}}]) // Google 24/7 representation
    expect(isOpenAt(j, 0, 12 * 60)).toBe(true)
    expect(isOpenAt(j, 1, 12 * 60)).toBe(true)   // Monday — was wrongly false
    expect(isOpenAt(j, 6, 3 * 60)).toBe(true)    // Saturday 03:00
  })
  it('treats a no-close period on a specific non-sentinel day as open all that day only', () => {
    const j = hours([{open: {day: 3, hour: 9, minute: 0}}]) // Wed, open-ended
    expect(isOpenAt(j, 3, 20 * 60)).toBe(true)
    expect(isOpenAt(j, 4, 20 * 60)).toBe(false)
  })
})

describe('computeSchedule overnight', () => {
  it('marks overnight when a stop crosses midnight', () => {
    // Day starts at 22:00, stop 1: 120min dwell (ends 00:00), stop 2: 30min leg + 60min dwell
    // stop1: arrival=22:00 (1320min), depart=00:00 (1440min) → overnight on depart
    // stop2: arrival=00:30 (1470min) → overnight
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '22:00:00', useCurrentTimeAsStart: false,
      stops: [
        stop('1', 0, 120, null),          // arrival 1320, depart 1440 → overnight
        stop('2', 1, 60, 30 * 60),        // arrival 1470, depart 1530 → overnight
      ],
    }
    const s = computeSchedule(day)
    expect(s[0].overnight).toBe(true)   // depart == 1440
    expect(s[1].overnight).toBe(true)   // arrival > 1440
  })

  it('does not mark overnight for normal day stops', () => {
    const day: ItineraryDayDto = {
      id: 'd2', date: '2026-11-14', dayStartTime: '09:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].overnight).toBe(false)
    expect(s[1].overnight).toBe(false)
  })
})

const mkPlace = (over: Partial<TripPlaceDto> = {}): TripPlaceDto => ({
  id: 'p', tripId: 't', googlePlaceId: null, name: 'x', lat: 0, lng: 0, address: null,
  category: 'See', priceLevel: null, photoUrl: null, bestTimeStart: null, bestTimeEnd: null,
  openingHoursJson: null, feeNote: null, notes: null, ...over,
})
const mkHours = (periods: unknown) => JSON.stringify({periods})

describe('computeSchedule arrivedAfterMidnight', () => {
  it('true only when the raw arrival crosses midnight', () => {
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '22:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 120, null), stop('2', 1, 60, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrivedAfterMidnight).toBe(false) // arrival 22:00 < 1440 (only depart crosses)
    expect(s[1].arrivedAfterMidnight).toBe(true)  // arrival 00:30 >= 1440
  })
})

describe('offWindowFlag', () => {
  it('null inside window (bounds inclusive)', () => {
    const p = mkPlace({bestTimeStart: '08:00:00', bestTimeEnd: '10:00:00'})
    expect(offWindowFlag(p, '09:00')).toBeNull()
    expect(offWindowFlag(p, '08:00')).toBeNull()
    expect(offWindowFlag(p, '10:00')).toBeNull()
  })
  it('after window → suggestion, windowDir after', () => {
    const p = mkPlace({bestTimeStart: '12:00:00', bestTimeEnd: '13:00:00'})
    expect(offWindowFlag(p, '14:41')).toMatchObject({
      reason: 'off-window', severity: 'suggestion', windowDir: 'after', bestStart: '12:00', bestEnd: '13:00',
    })
  })
  it('before window → windowDir before', () => {
    const p = mkPlace({bestTimeStart: '17:30:00', bestTimeEnd: '18:30:00'})
    expect(offWindowFlag(p, '13:50')).toMatchObject({windowDir: 'before'})
  })
  it('null when no window set', () => {
    expect(offWindowFlag(mkPlace(), '13:50')).toBeNull()
  })
})

describe('closedFlag', () => {
  it('before-open: opens later today, not opened yet', () => {
    const j = mkHours([{open: {day: 1, hour: 10}, close: {day: 1, hour: 18}}])
    expect(closedFlag(j, 1, 9 * 60)).toMatchObject({reason: 'closed', severity: 'problem', closedKind: 'before-open', reopenAt: '10:00'})
  })
  it('on-break: split hours, arrive during the gap', () => {
    const j = mkHours([{open: {day: 1, hour: 11}, close: {day: 1, hour: 14}}, {open: {day: 1, hour: 17}, close: {day: 1, hour: 22}}])
    expect(closedFlag(j, 1, 15 * 60)).toMatchObject({closedKind: 'on-break', reopenAt: '17:00'})
  })
  it('after-close: opened earlier, now past last close', () => {
    const j = mkHours([{open: {day: 1, hour: 9}, close: {day: 1, hour: 17}}])
    const f = closedFlag(j, 1, 18 * 60)
    expect(f).toMatchObject({closedKind: 'after-close'})
    expect(f?.reopenAt).toBeUndefined()
  })
  it('all-day: no period this weekday', () => {
    const j = mkHours([{open: {day: 2, hour: 9}, close: {day: 2, hour: 17}}]) // only Tuesday
    expect(closedFlag(j, 1, 12 * 60)).toMatchObject({closedKind: 'all-day'})
  })
  it('null when hours unknown', () => {
    expect(closedFlag(null, 1, 12 * 60)).toBeNull()
  })
})

describe('composeFlags', () => {
  it('overflow fires once, on the first stop reached after midnight', () => {
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '22:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 120, null), stop('2', 1, 60, 30 * 60), stop('3', 2, 60, 30 * 60)],
    }
    const composed = composeFlags(computeSchedule(day), {}, dayOfWeek(day.date))
    expect(composed.filter(c => c.flag?.reason === 'overflow')).toHaveLength(1)
    expect(composed[1].flag).toMatchObject({reason: 'overflow', severity: 'problem', arrival: '00:30'})
    expect(composed[2].flag).toBeNull()
  })
  it('no overflow when only the departure crosses midnight', () => {
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '23:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 90, null)], // arrival 23:00, depart 00:30
    }
    const composed = composeFlags(computeSchedule(day), {}, dayOfWeek(day.date))
    expect(composed.some(c => c.flag?.reason === 'overflow')).toBe(false)
  })
  it('closed outranks off-window on the same stop', () => {
    const p = mkPlace({
      bestTimeStart: '12:00:00', bestTimeEnd: '13:00:00',
      openingHoursJson: mkHours([{open: {day: 6, hour: 10}, close: {day: 6, hour: 11}}]), // Sat 10–11
    })
    const day: ItineraryDayDto = {id: 'd', date: '2026-11-14', dayStartTime: '14:00:00', useCurrentTimeAsStart: false, stops: [stop('1', 0, 30, null)]}
    const composed = composeFlags(computeSchedule(day), {p1: p}, dayOfWeek(day.date))
    expect(composed[0].flag?.reason).toBe('closed')
  })
  it('null flag for a well-timed open stop with no window', () => {
    const p = mkPlace({openingHoursJson: mkHours([{open: {day: 6, hour: 8}, close: {day: 6, hour: 20}}])})
    const day: ItineraryDayDto = {id: 'd', date: '2026-11-14', dayStartTime: '10:00:00', useCurrentTimeAsStart: false, stops: [stop('1', 0, 30, null)]}
    const composed = composeFlags(computeSchedule(day), {p1: p}, dayOfWeek(day.date))
    expect(composed[0].flag).toBeNull()
  })
  it('overflow outranks a same-stop closed flag', () => {
    // 2026-11-14 is Saturday (dow 6); this place opens only Tuesday → closed on Sat.
    // Day starts 22:00 so stop 2 is reached at 00:30 (after midnight) AND is closed.
    const p = mkPlace({openingHoursJson: mkHours([{open: {day: 2, hour: 9}, close: {day: 2, hour: 17}}])})
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '22:00:00', useCurrentTimeAsStart: false,
      stops: [stop('1', 0, 120, null), stop('2', 1, 60, 30 * 60)],
    }
    const composed = composeFlags(computeSchedule(day), {p1: p, p2: p}, dayOfWeek(day.date))
    expect(composed[1].flag).toMatchObject({reason: 'overflow'}) // overflow wins over closed on the same stop
  })
  it('no flag for a 24/7 place (always-open sentinel) on a weekday', () => {
    const p = mkPlace({openingHoursJson: mkHours([{open: {day: 0, hour: 0, minute: 0}}])})
    // 2026-11-14 is Saturday (dow 6); a 24/7 place must NOT be flagged closed.
    const day: ItineraryDayDto = {id: 'd', date: '2026-11-14', dayStartTime: '10:00:00', useCurrentTimeAsStart: false, stops: [stop('1', 0, 30, null)]}
    const composed = composeFlags(computeSchedule(day), {p1: p}, dayOfWeek(day.date))
    expect(composed[0].flag).toBeNull()
  })
})
