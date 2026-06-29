// frontend/src/pages/trips/hooks/useSchedule.test.ts
import {describe, it, expect} from 'vitest'
import {computeSchedule, flagStop} from './useSchedule'
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
