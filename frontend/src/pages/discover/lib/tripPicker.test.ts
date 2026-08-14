import {describe, it, expect} from 'vitest'
import type {TripDto} from '../../../shared/api/api'
import {filterTrips, tripSubtitle, TRIP_PICKER_QUERY_ARGS, TRIP_PICKER_PAGE_SIZE} from './tripPicker'

function trip(over: Partial<TripDto> = {}): TripDto {
  return {
    id: 'id-1',
    name: 'เที่ยวระยอง',
    destination: 'ระยอง',
    startDate: '2026-08-01',
    dayCount: 2,
    defaultTravelMode: 'Drive',
    isDaily: false,
    ...over,
  }
}

describe('TRIP_PICKER_QUERY_ARGS', () => {
  // The whole point of the fix: without these the server returns its own default
  // page — 10 trips, oldest-first — and the newest 10 become unreachable (R1.5).
  it('asks for a full page, newest first', () => {
    expect(TRIP_PICKER_QUERY_ARGS.take).toBe(TRIP_PICKER_PAGE_SIZE)
    expect(TRIP_PICKER_PAGE_SIZE).toBeGreaterThan(10)
    expect(TRIP_PICKER_QUERY_ARGS.sortColumn).toBe('startDate')
    expect(TRIP_PICKER_QUERY_ARGS.sortDirection).toBe('Descending')
  })
})

describe('filterTrips', () => {
  const trips = [
    trip({id: 'a', name: 'เที่ยวระยอง', destination: 'ระยอง'}),
    trip({id: 'b', name: 'เที่ยวเขาใหญ่', destination: 'นครราชสีมา'}),
    trip({id: 'c', name: 'Bangkok food run', destination: null}),
  ]

  it('returns every trip for a blank or whitespace query', () => {
    expect(filterTrips(trips, '')).toHaveLength(3)
    expect(filterTrips(trips, '   ')).toHaveLength(3)
  })

  it('matches on the trip name', () => {
    expect(filterTrips(trips, 'ระยอง').map((t) => t.id)).toEqual(['a'])
  })

  it('matches on the destination even when the name does not contain it', () => {
    expect(filterTrips(trips, 'นครราชสีมา').map((t) => t.id)).toEqual(['b'])
  })

  it('is case-insensitive and ignores surrounding whitespace', () => {
    expect(filterTrips(trips, '  BANGKOK ').map((t) => t.id)).toEqual(['c'])
  })

  it('tolerates a null destination', () => {
    expect(() => filterTrips(trips, 'zzz')).not.toThrow()
    expect(filterTrips(trips, 'zzz')).toEqual([])
  })

  it('does not mutate or alias the input array', () => {
    const out = filterTrips(trips, '')
    out.pop()
    expect(trips).toHaveLength(3)
  })
})

describe('tripSubtitle', () => {
  it('reads "ทุกวัน" for a daily trip instead of a start date', () => {
    const s = tripSubtitle(trip({isDaily: true, dayCount: 1}))
    expect(s).toContain('ทุกวัน')
    expect(s).toContain('ระยอง')
  })

  it('includes the day count only when the trip spans more than one day', () => {
    expect(tripSubtitle(trip({dayCount: 3}))).toContain('3 วัน')
    expect(tripSubtitle(trip({dayCount: 1}))).not.toContain('วัน')
  })

  it('omits an absent destination without leaving a stray separator', () => {
    const s = tripSubtitle(trip({destination: null, isDaily: true, dayCount: 1}))
    expect(s).toBe('ทุกวัน')
  })

  it('survives an unparseable start date', () => {
    const s = tripSubtitle(trip({startDate: '', dayCount: 1}))
    expect(s).toBe('ระยอง')
  })
})
