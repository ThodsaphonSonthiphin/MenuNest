// frontend/src/pages/trips/hooks/useSchedule.test.ts
import {describe, it, expect} from 'vitest'
import {computeSchedule, dayOfWeek, flagStop, isOpenAt} from './useSchedule'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, legToReach: legSec == null ? null : {seconds: legSec, meters: 1000},
})

describe('computeSchedule', () => {
  it('cascades arrival = prev depart + leg; depart = arrival + dwell', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60), stop('3', 2, 90, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:00'); expect(s[0].depart).toBe('10:00')
    expect(s[1].arrival).toBe('10:25'); expect(s[1].depart).toBe('11:10')
    expect(s[2].arrival).toBe('11:40'); expect(s[2].depart).toBe('13:10')
  })
})

describe('flagStop', () => {
  const place = (bestStart: string | null, bestEnd: string | null, hoursJson: string | null): TripPlaceDto => ({
    id: 'p', tripId: 't', googlePlaceId: null, name: 'x', lat: 0, lng: 0, address: null,
    category: 'See', priceLevel: null, photoUrl: null, bestTimeStart: bestStart, bestTimeEnd: bestEnd,
    openingHoursJson: hoursJson, feeNote: null, notes: null,
  })

  it('green when arrival within best window', () => {
    expect(flagStop(place('08:00:00', '10:00:00', null), '09:00', '10:00')).toBe('green')
  })
  it('amber when arrival before best window', () => {
    expect(flagStop(place('17:30:00', '18:30:00', null), '13:50', '15:20')).toBe('amber')
  })
  it('green when no best window set (nothing to flag against)', () => {
    expect(flagStop(place(null, null, null), '13:50', '15:20')).toBe('green')
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

  it('treats an open period with no close as open all that day', () => {
    const j = hours([{open: {day: 0, hour: 0, minute: 0}}]) // Sun, always open
    expect(isOpenAt(j, 0, 12 * 60)).toBe(true)
    expect(isOpenAt(j, 1, 12 * 60)).toBe(false)
  })
})

describe('computeSchedule overnight', () => {
  it('marks overnight when a stop crosses midnight', () => {
    // Day starts at 22:00, stop 1: 120min dwell (ends 00:00), stop 2: 30min leg + 60min dwell
    // stop1: arrival=22:00 (1320min), depart=00:00 (1440min) → overnight on depart
    // stop2: arrival=00:30 (1470min) → overnight
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '22:00:00',
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
      id: 'd2', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].overnight).toBe(false)
    expect(s[1].overnight).toBe(false)
  })
})
