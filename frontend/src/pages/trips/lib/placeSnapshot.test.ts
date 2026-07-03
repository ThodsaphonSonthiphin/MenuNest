import {describe, it, expect} from 'vitest'
import {toResolvedPlace, PLACE_DETAIL_FIELDS} from './placeSnapshot'

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
