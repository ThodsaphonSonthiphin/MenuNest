import {describe, expect, it} from 'vitest'
import type {ItineraryDayDto, TripDto} from '../../../shared/api/api'
import {capNames, draftFromTrip, isDraftDirty, normalizeDraft, shrinkLoss, totalStops} from './tripEdit'

const trip: TripDto = {
  id: 't1',
  name: 'เที่ยวเชียงใหม่',
  destination: 'เชียงใหม่',
  startDate: '2026-08-01',
  dayCount: 3,
  defaultTravelMode: 'Drive',
  isDaily: false,
}

function day(id: string, date: string, stops: {id: string; placeId: string; visited?: boolean}[]): ItineraryDayDto {
  return {
    id,
    date,
    dayStartTime: '09:00:00',
    useCurrentTimeAsStart: false,
    stops: stops.map((s, i) => ({
      id: s.id,
      tripPlaceId: s.placeId,
      sequence: i,
      dwellMinutes: 60,
      travelModeToReach: 'Drive' as const,
      legToReach: null,
      isVisited: s.visited ?? false,
      checklist: [],
    })),
  }
}

const NAMES = {p1: 'วัดพระธาตุดอยสุเทพ', p2: 'ร้านกาแฟ Ristr8to', p3: 'ไนท์บาซาร์'}

describe('draftFromTrip', () => {
  it('maps a null destination to an empty string', () => {
    expect(draftFromTrip({...trip, destination: null}).destination).toBe('')
  })

  it('trims a date-time start date down to yyyy-MM-dd', () => {
    expect(draftFromTrip({...trip, startDate: '2026-08-01T00:00:00'}).startDate).toBe('2026-08-01')
  })
})

describe('isDraftDirty', () => {
  it('is false for an untouched draft', () => {
    expect(isDraftDirty(draftFromTrip(trip), trip)).toBe(false)
  })

  it('is false when only whitespace was added, because the server trims too', () => {
    const d = normalizeDraft({...draftFromTrip(trip), name: '  เที่ยวเชียงใหม่  '})
    expect(isDraftDirty(d, trip)).toBe(false)
  })

  it('is false when an already-empty destination is blanked differently', () => {
    const t = {...trip, destination: null}
    expect(isDraftDirty({...draftFromTrip(t), destination: '   '}, t)).toBe(false)
  })

  it.each([
    ['name', {name: 'อื่น'}],
    ['destination', {destination: 'ลำปาง'}],
    ['startDate', {startDate: '2026-08-02'}],
    ['dayCount', {dayCount: 2}],
    ['defaultTravelMode', {defaultTravelMode: 'Walk' as const}],
  ])('is true when %s changed', (_label, patch) => {
    expect(isDraftDirty({...draftFromTrip(trip), ...patch}, trip)).toBe(true)
  })

  it('is true when a destination is cleared', () => {
    expect(isDraftDirty({...draftFromTrip(trip), destination: ''}, trip)).toBe(true)
  })

  it('compares the start date against a date-time server value correctly', () => {
    const t = {...trip, startDate: '2026-08-01T00:00:00'}
    expect(isDraftDirty(draftFromTrip(t), t)).toBe(false)
  })
})

describe('shrinkLoss', () => {
  const days = [
    day('d1', '2026-08-01', [{id: 's1', placeId: 'p1'}]),
    day('d2', '2026-08-02', []),
    day('d3', '2026-08-03', [{id: 's2', placeId: 'p2', visited: true}, {id: 's3', placeId: 'p3'}]),
  ]

  it('is null when the itinerary is not loaded', () => {
    expect(shrinkLoss([], NAMES, 1)).toBeNull()
  })

  it('is null when the day count grows', () => {
    expect(shrinkLoss(days, NAMES, 5)).toBeNull()
  })

  it('is null when the day count is unchanged', () => {
    expect(shrinkLoss(days, NAMES, 3)).toBeNull()
  })

  it('is null when the dropped days hold no stops', () => {
    const empty = [days[0], day('d2', '2026-08-02', [])]
    expect(shrinkLoss(empty, NAMES, 1)).toBeNull()
  })

  it('reports the day range, dates, names and visited flags of a real loss', () => {
    const loss = shrinkLoss(days, NAMES, 2)!
    expect(loss.dayFrom).toBe(3)
    expect(loss.dayTo).toBe(3)
    expect(loss.dateFrom).toBe('2026-08-03')
    expect(loss.dateTo).toBe('2026-08-03')
    expect(loss.stops.map((s) => s.name)).toEqual(['ร้านกาแฟ Ristr8to', 'ไนท์บาซาร์'])
    expect(loss.stops.map((s) => s.isVisited)).toEqual([true, false])
  })

  it('spans several dropped days and skips the empty one in between', () => {
    const loss = shrinkLoss(days, NAMES, 1)!
    expect(loss.dayFrom).toBe(2)
    expect(loss.dayTo).toBe(3)
    expect(loss.dateFrom).toBe('2026-08-02')
    expect(loss.dateTo).toBe('2026-08-03')
    expect(loss.stops).toHaveLength(2)
  })

  it('falls back to a generic label when a place name is missing', () => {
    expect(shrinkLoss(days, {}, 2)!.stops[0].name).toBe('สถานที่')
  })

  it('takes the drop set by index, not by date', () => {
    // A single-day current-time-start trip is served with day[0].date projected to the
    // viewer's today, so date matching would pick the wrong rows.
    const projected = [day('d1', '2030-01-01', [{id: 's1', placeId: 'p1'}]), days[2]]
    const loss = shrinkLoss(projected, NAMES, 1)!
    expect(loss.stops.map((s) => s.name)).toEqual(['ร้านกาแฟ Ristr8to', 'ไนท์บาซาร์'])
  })
})

describe('capNames', () => {
  it('returns everything with no overflow under the cap', () => {
    expect(capNames([1, 2, 3], 5)).toEqual({shown: [1, 2, 3], moreCount: 0})
  })

  it('caps and counts the overflow', () => {
    expect(capNames([1, 2, 3, 4, 5, 6, 7], 5)).toEqual({shown: [1, 2, 3, 4, 5], moreCount: 2})
  })
})

describe('totalStops', () => {
  it('sums every day', () => {
    expect(totalStops([
      day('d1', '2026-08-01', [{id: 's1', placeId: 'p1'}]),
      day('d2', '2026-08-02', [{id: 's2', placeId: 'p2'}, {id: 's3', placeId: 'p3'}]),
    ])).toBe(3)
  })

  it('is zero for an unloaded itinerary', () => {
    expect(totalStops([])).toBe(0)
  })
})
