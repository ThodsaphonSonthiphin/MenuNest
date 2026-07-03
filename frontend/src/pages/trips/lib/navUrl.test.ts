import {describe, it, expect} from 'vitest'
import {travelModeToGmaps, buildStopNavUrl, buildDayNavUrl} from './navUrl'
import type {NavPoint} from './navUrl'

// Helpers to read a built URL without asserting brittle encoded strings.
const q = (url: string) => new URL(url).searchParams
const pt = (lat: number, lng: number, placeId?: string | null): NavPoint => ({lat, lng, placeId})

describe('travelModeToGmaps', () => {
  it('maps every TravelMode to its Google value', () => {
    expect(travelModeToGmaps('Drive')).toBe('driving')
    expect(travelModeToGmaps('Walk')).toBe('walking')
    expect(travelModeToGmaps('Transit')).toBe('transit')
  })
})

describe('buildStopNavUrl', () => {
  it('builds a single-destination link with place_id + dir_action=navigate', () => {
    const url = buildStopNavUrl({lat: 19.04, lng: 99.63, googlePlaceId: 'ChIJabc'}, 'Walk')!
    expect(url.startsWith('https://www.google.com/maps/dir/?')).toBe(true)
    const p = q(url)
    expect(p.get('api')).toBe('1')
    expect(p.get('destination')).toBe('19.040000,99.630000')
    expect(p.get('destination_place_id')).toBe('ChIJabc')
    expect(p.get('travelmode')).toBe('walking')
    expect(p.get('dir_action')).toBe('navigate')
    expect(p.has('origin')).toBe(false)
  })

  it('omits destination_place_id when the Place has no googlePlaceId', () => {
    const url = buildStopNavUrl({lat: 19.04, lng: 99.63, googlePlaceId: null}, 'Drive')!
    expect(q(url).has('destination_place_id')).toBe(false)
    expect(q(url).get('travelmode')).toBe('driving')
  })

  it('returns null for unusable coords (NaN and Null Island)', () => {
    expect(buildStopNavUrl({lat: NaN, lng: 99, googlePlaceId: null}, 'Drive')).toBeNull()
    expect(buildStopNavUrl({lat: 0, lng: 0, googlePlaceId: null}, 'Drive')).toBeNull()
  })
})

describe('buildDayNavUrl', () => {
  it('returns null for empty or all-unusable input', () => {
    expect(buildDayNavUrl([], 3, 'Drive')).toBeNull()
    expect(buildDayNavUrl([pt(0, 0), pt(NaN, 1)], 3, 'Drive')).toBeNull()
  })

  it('handles a single usable stop: destination only, no waypoints', () => {
    const r = buildDayNavUrl([pt(19.06, 99.65)], 3, 'Drive')!
    expect(r.coveredCount).toBe(1)
    expect(r.overflow).toBe(false)
    const p = q(r.url)
    expect(p.get('destination')).toBe('19.060000,99.650000')
    expect(p.has('waypoints')).toBe(false)
  })

  it('covers a full day within the cap (3 stops, cap 3): waypoints=first 2, dest=last', () => {
    const r = buildDayNavUrl([pt(19.01, 99.6), pt(19.04, 99.63), pt(19.06, 99.65)], 3, 'Drive')!
    expect(r.coveredCount).toBe(3)
    expect(r.overflow).toBe(false)
    const p = q(r.url)
    expect(p.get('waypoints')).toBe('19.010000,99.600000|19.040000,99.630000')
    expect(p.get('destination')).toBe('19.060000,99.650000')
    expect(p.get('travelmode')).toBe('driving')
    expect(p.get('dir_action')).toBe('navigate')
  })

  it('fits exactly at the boundary K = cap+1 with no overflow (4 stops, cap 3)', () => {
    const r = buildDayNavUrl([pt(1, 1), pt(2, 2), pt(3, 3), pt(4, 4)], 3, 'Drive')!
    expect(r.coveredCount).toBe(4)
    expect(r.overflow).toBe(false)
    expect(q(r.url).get('waypoints')!.split('|')).toHaveLength(3)
  })

  it('truncates when over the cap (5 stops, cap 3): cover first 4, overflow true', () => {
    const r = buildDayNavUrl([pt(1, 1), pt(2, 2), pt(3, 3), pt(4, 4), pt(5, 5)], 3, 'Drive')!
    expect(r.coveredCount).toBe(4)
    expect(r.overflow).toBe(true)
    const p = q(r.url)
    expect(p.get('waypoints')).toBe('1.000000,1.000000|2.000000,2.000000|3.000000,3.000000')
    expect(p.get('destination')).toBe('4.000000,4.000000')
  })

  it('desktop cap 9: 10 stops fit, 11 overflow', () => {
    const many = (n: number) => Array.from({length: n}, (_, i) => pt(i + 1, i + 1))
    expect(buildDayNavUrl(many(10), 9, 'Drive')!.overflow).toBe(false)
    expect(buildDayNavUrl(many(10), 9, 'Drive')!.coveredCount).toBe(10)
    expect(buildDayNavUrl(many(11), 9, 'Drive')!.overflow).toBe(true)
    expect(buildDayNavUrl(many(11), 9, 'Drive')!.coveredCount).toBe(10)
  })

  it('collapses consecutive duplicates (same placeId, and same coord) but preserves non-consecutive revisits', () => {
    const dupPlace = buildDayNavUrl([pt(1, 1, 'A'), pt(1, 1, 'A'), pt(2, 2, 'B')], 3, 'Drive')!
    expect(dupPlace.coveredCount).toBe(2)
    const dupCoord = buildDayNavUrl([pt(1, 1), pt(1, 1), pt(2, 2)], 3, 'Drive')!
    expect(dupCoord.coveredCount).toBe(2)
    const revisit = buildDayNavUrl([pt(1, 1, 'A'), pt(2, 2, 'B'), pt(1, 1, 'A')], 3, 'Drive')!
    expect(revisit.coveredCount).toBe(3)
  })

  it('never sends place_ids on the whole-day route and stays under 2048 chars', () => {
    const many = Array.from({length: 10}, (_, i) => pt(i + 1, i + 1, 'ChIJ' + 'x'.repeat(120)))
    const r = buildDayNavUrl(many, 9, 'Drive')!
    expect(q(r.url).has('waypoint_place_ids')).toBe(false)
    expect(q(r.url).has('destination_place_id')).toBe(false)
    expect(r.url.length).toBeLessThan(2048)
  })
})
