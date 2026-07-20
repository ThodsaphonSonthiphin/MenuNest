import {describe, it, expect} from 'vitest'
import {applyDiscover, type DiscoverInput} from './discoverFilter'
import type {DiscoverPlaceDto} from '../../../shared/api/api'

// Mon 2026-06-01 10:00 local (getDay()=1, 600 min, getMonth()=5)
const NOW = new Date(2026, 5, 1, 10, 0, 0)
const openMon = JSON.stringify({periods: [{open: {day: 1, hour: 9, minute: 0}, close: {day: 1, hour: 17, minute: 0}}]})

const place = (over: Partial<DiscoverPlaceDto>): DiscoverPlaceDto => ({
  key: 'k', googlePlaceId: 'g', representativeTripPlaceId: 't', name: 'P',
  lat: 13.75, lng: 100.5, address: null, category: 'See', priceLevel: null, photoUrl: null,
  openingHoursJson: null, bestTimeStart: null, bestTimeEnd: null, seasonPeriods: [],
  visited: false, hasProfile: false, trips: [], ...over,
})

const base: DiscoverInput = {
  anchor: {lat: 13.75, lng: 100.5}, viewport: null, category: 'all',
  toggles: {openNow: false, season: false, bestTime: false, hideVisited: false}, now: NOW,
}

describe('applyDiscover', () => {
  it('sorts by distance ascending from the anchor', () => {
    const near = place({key: 'near', lat: 13.75, lng: 100.5})
    const far = place({key: 'far', lat: 18.79, lng: 98.99})
    const out = applyDiscover([far, near], base)
    expect(out.map((p) => p.key)).toEqual(['near', 'far'])
    expect(out[0].distanceKm).toBeCloseTo(0, 3)
  })

  it('open-now toggle drops places closed now but keeps unknown-hours places', () => {
    const open = place({key: 'open', openingHoursJson: openMon})
    const closed = place({key: 'closed', openingHoursJson: JSON.stringify({periods: [{open: {day: 1, hour: 12, minute: 0}, close: {day: 1, hour: 14, minute: 0}}]})})
    const unknown = place({key: 'unknown', openingHoursJson: null})
    const out = applyDiscover([open, closed, unknown], {...base, toggles: {...base.toggles, openNow: true}})
    expect(out.map((p) => p.key).sort()).toEqual(['open', 'unknown'])
  })

  it('season toggle drops "bad" this month and ranks "good" above neutral', () => {
    const bad = place({key: 'bad', seasonPeriods: [{kind: 'Bad', months: [5], note: null}]})
    const good = place({key: 'good', lat: 18, lng: 99, seasonPeriods: [{kind: 'Good', months: [5], note: null}]})
    const none = place({key: 'none', lat: 14, lng: 100})
    const out = applyDiscover([bad, none, good], {...base, toggles: {...base.toggles, season: true}})
    expect(out.map((p) => p.key)).not.toContain('bad')
    expect(out[0].key).toBe('good') // good ranked first despite being farther
  })

  it('hideVisited toggle removes visited places', () => {
    const seen = place({key: 'seen', visited: true})
    const fresh = place({key: 'fresh'})
    const out = applyDiscover([seen, fresh], {...base, toggles: {...base.toggles, hideVisited: true}})
    expect(out.map((p) => p.key)).toEqual(['fresh'])
  })

  it('category filter keeps only the chosen category', () => {
    const eat = place({key: 'eat', category: 'Eat'})
    const see = place({key: 'see', category: 'See'})
    const out = applyDiscover([eat, see], {...base, category: 'Eat'})
    expect(out.map((p) => p.key)).toEqual(['eat'])
  })

  it('viewport filter keeps only places inside the bounds', () => {
    const inside = place({key: 'in', lat: 13.75, lng: 100.5})
    const outside = place({key: 'out', lat: 18.79, lng: 98.99})
    const out = applyDiscover([inside, outside], {...base, viewport: {north: 14, south: 13, east: 101, west: 100}})
    expect(out.map((p) => p.key)).toEqual(['in'])
  })

  it('bestTime toggle ranks a place whose window covers now above others', () => {
    const match = place({key: 'match', lat: 18, lng: 99, bestTimeStart: '09:00:00', bestTimeEnd: '11:00:00'})
    const other = place({key: 'other', lat: 13.75, lng: 100.5, bestTimeStart: '14:00:00', bestTimeEnd: '16:00:00'})
    const out = applyDiscover([other, match], {...base, toggles: {...base.toggles, bestTime: true}})
    expect(out[0].key).toBe('match')
  })
})
