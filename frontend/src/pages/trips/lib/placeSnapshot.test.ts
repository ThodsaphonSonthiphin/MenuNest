import {describe, it, expect} from 'vitest'
import {toResolvedPlace, openingHoursToJson, PLACE_DETAIL_FIELDS} from './placeSnapshot'
import {isOpenAt} from '../hooks/useSchedule'

const base = {
  placeId: 'ChIJxyz', name: 'Ristr8to Coffee', lat: 18.8, lng: 98.97,
  address: 'Nimman Rd', types: ['coffee_shop'], priceLevel: 'MODERATE',
  openingHoursJson: '{"weekdayDescriptions":["Mon: 8AM-6PM"]}',
}

describe('PLACE_DETAIL_FIELDS', () => {
  it('requests only the fields the snapshot needs', () => {
    expect(PLACE_DETAIL_FIELDS).toEqual(
      ['id', 'displayName', 'location', 'formattedAddress', 'types', 'priceLevel', 'regularOpeningHours'],
    )
  })
})

describe('toResolvedPlace', () => {
  it('maps fields and derives category from types', () => {
    const r = toResolvedPlace(base)
    expect(r.googlePlaceId).toBe('ChIJxyz')
    expect(r.name).toBe('Ristr8to Coffee')
    expect(r.lat).toBe(18.8)
    expect(r.lng).toBe(98.97)
    expect(r.address).toBe('Nimman Rd')
    expect(r.category).toBe('Cafe')
    expect(r.openingHoursJson).toBe('{"weekdayDescriptions":["Mon: 8AM-6PM"]}')
    expect(r.photoUrl).toBeNull()
  })
  it('converts the JS-SDK priceLevel enum to an int', () => {
    expect(toResolvedPlace({...base, priceLevel: 'FREE'}).priceLevel).toBe(0)
    expect(toResolvedPlace({...base, priceLevel: 'INEXPENSIVE'}).priceLevel).toBe(1)
    expect(toResolvedPlace({...base, priceLevel: 'MODERATE'}).priceLevel).toBe(2)
    expect(toResolvedPlace({...base, priceLevel: 'EXPENSIVE'}).priceLevel).toBe(3)
    expect(toResolvedPlace({...base, priceLevel: 'VERY_EXPENSIVE'}).priceLevel).toBe(4)
    expect(toResolvedPlace({...base, priceLevel: null}).priceLevel).toBeNull()
    expect(toResolvedPlace({...base, priceLevel: 'weird'}).priceLevel).toBeNull()
  })
  it('falls back to null address / empty types safely', () => {
    const r = toResolvedPlace({...base, address: null, types: []})
    expect(r.address).toBeNull()
    expect(r.category).toBe('Other')
  })
})

describe('openingHoursToJson', () => {
  it('round-trips a normal period through isOpenAt', () => {
    const json = openingHoursToJson({
      periods: [{open: {day: 1, hour: 9, minute: 0}, close: {day: 1, hour: 17, minute: 0}}],
      weekdayDescriptions: ['Mon: 9AM-5PM'],
    })!
    expect(JSON.parse(json).periods[0].open).toEqual({day: 1, hour: 9, minute: 0})
    expect(isOpenAt(json, 1, 10 * 60)).toBe(true)   // Mon 10:00 → open
    expect(isOpenAt(json, 1, 18 * 60)).toBe(false)  // Mon 18:00 → closed
    expect(isOpenAt(json, 2, 10 * 60)).toBe(false)  // Tue → no period → closed
  })
  it('reads getter-exposed points (SDK-like), not just plain literals', () => {
    // Points whose day/hour/minute are NON-ENUMERABLE getters — like the SDK's
    // OpeningHoursPoint. JSON.stringify() emits {} for these, so this test only
    // passes because openingHoursToJson reads the fields explicitly. (Regression
    // guard: reverting to JSON.stringify(raw) makes this fail.)
    const pt = (day: number, hour: number, minute: number) => {
      const o = {}
      Object.defineProperties(o, {
        day: {get: () => day},
        hour: {get: () => hour},
        minute: {get: () => minute},
      })
      return o as {day: number; hour: number; minute: number}
    }
    const json = openingHoursToJson({periods: [{open: pt(3, 8, 30), close: pt(3, 12, 0)}]})!
    expect(JSON.parse(json).periods[0].open).toEqual({day: 3, hour: 8, minute: 30})
    expect(isOpenAt(json, 3, 9 * 60)).toBe(true)
  })
  it('returns null for null/undefined hours', () => {
    expect(openingHoursToJson(null)).toBeNull()
    expect(openingHoursToJson(undefined)).toBeNull()
  })
  it('empty periods → isOpenAt returns null (always-open, no penalty)', () => {
    const json = openingHoursToJson({periods: [], weekdayDescriptions: ['Open 24 hours']})!
    expect(isOpenAt(json, 1, 10 * 60)).toBeNull()
  })
})
