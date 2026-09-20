// frontend/src/pages/trips/lib/ghostPins.test.ts
import {afterEach, describe, expect, it, vi} from 'vitest'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'
import {
  buildGhostPins,
  GHOST_LABEL_LIMIT,
  readGhostPref,
  showGhostLabels,
  writeGhostPref,
} from './ghostPins'

function place(id: string, over: Partial<TripPlaceDto> = {}): TripPlaceDto {
  return {
    id, tripId: 't1', googlePlaceId: null, name: `place ${id}`, lat: 12.9, lng: 100.9,
    address: null, category: 'See', priceLevel: null, photoUrl: null, openingHoursJson: null,
    feeNote: null, notes: null, reviewLinks: [], hasProfile: false, seasonPeriods: [],
    bestTimeWindows: [],
    ...over,
  }
}

function stop(tripPlaceId: string, over: Partial<StopDto> = {}): StopDto {
  return {
    id: `s-${tripPlaceId}`, tripPlaceId, sequence: 1, dwellMinutes: 60,
    travelModeToReach: 'Drive', legToReach: null, isVisited: false, checklist: [],
    ...over,
  }
}

function day(id: string, stops: StopDto[]): ItineraryDayDto {
  return {id, date: '2026-09-20', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops}
}

afterEach(() => { vi.unstubAllGlobals() })

describe('buildGhostPins', () => {
  it('ghosts every Place that is not a Stop on the active Day', () => {
    const places = [place('a'), place('b'), place('c')]
    const days = [day('d1', [stop('a')]), day('d2', [stop('b')])]

    const ghosts = buildGhostPins(places, days, 'd1')

    expect(ghosts.map((g) => g.id)).toEqual(['b', 'c'])
  })

  it('carries the Day a ghost IS scheduled on, so the card can say อยู่ในวัน M', () => {
    const days = [day('d1', [stop('a')]), day('d2', [stop('b')])]

    const [ghost] = buildGhostPins([place('b')], days, 'd1')

    expect(ghost.scheduledDayNumber).toBe(2)
    expect(ghost.scheduledDayId).toBe('d2')
  })

  it('reports null for a Place scheduled on no Day at all', () => {
    const [ghost] = buildGhostPins([place('c')], [day('d1', [stop('a')])], 'd1')

    expect(ghost.scheduledDayNumber).toBeNull()
    expect(ghost.scheduledDayId).toBeNull()
  })

  it('names the FIRST Day that schedules a Place appearing on several', () => {
    const days = [day('d1', [stop('a')]), day('d2', [stop('b')]), day('d3', [stop('b')])]

    const [ghost] = buildGhostPins([place('b')], days, 'd1')

    expect(ghost.scheduledDayNumber).toBe(2)
  })

  it('does NOT ghost a Stop on the active Day that is already visited', () => {
    // useDayRoute drops visited Stops from the ROUTE, but re-drawing one as a ghost would
    // invite adding the same Place to the Day twice.
    const days = [day('d1', [stop('a', {isVisited: true})])]

    expect(buildGhostPins([place('a')], days, 'd1')).toEqual([])
  })

  it('drops non-finite coordinates — they would make the map bounds NaN', () => {
    const places = [place('a', {lat: Number.NaN}), place('b', {lng: Number.POSITIVE_INFINITY}), place('c')]

    expect(buildGhostPins(places, [], null).map((g) => g.id)).toEqual(['c'])
  })

  it('ghosts everything when no Day is active yet', () => {
    const days = [day('d1', [stop('a')])]

    expect(buildGhostPins([place('a'), place('b')], days, null).map((g) => g.id)).toEqual(['a', 'b'])
  })
})

describe('showGhostLabels', () => {
  it('keeps labels up to the density limit and drops them past it', () => {
    expect(showGhostLabels(GHOST_LABEL_LIMIT)).toBe(true)
    expect(showGhostLabels(GHOST_LABEL_LIMIT + 1)).toBe(false)
  })
})

describe('the ghost toggle preference', () => {
  function stubStorage(initial: Record<string, string> = {}) {
    const map = new Map(Object.entries(initial))
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((k: string) => map.get(k) ?? null),
      setItem: vi.fn((k: string, v: string) => void map.set(k, v)),
      removeItem: vi.fn((k: string) => void map.delete(k)),
    })
    return map
  }

  it('defaults to SHOWN when nothing is stored (menunest-241)', () => {
    stubStorage()
    expect(readGhostPref()).toBe(true)
  })

  it('round-trips the choice', () => {
    stubStorage()
    writeGhostPref(false)
    expect(readGhostPref()).toBe(false)
    writeGhostPref(true)
    expect(readGhostPref()).toBe(true)
  })

  it('falls back to SHOWN when the store throws — a blocked store must not hide the library', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => { throw new Error('blocked') },
      setItem: () => { throw new Error('blocked') },
    })
    expect(readGhostPref()).toBe(true)
    expect(() => writeGhostPref(false)).not.toThrow()
  })
})
