import {describe, expect, it} from 'vitest'
import type {ResolvedPlaceDto} from '../../../shared/api/api'
import {captureNameValid, coordinatePlaceFrom, needsTypedName} from './coordinateCapture'

const googlePlace: ResolvedPlaceDto = {
  googlePlaceId: 'places/ChIJabc',
  name: 'ครัวเขาใหญ่',
  lat: 14.4356,
  lng: 101.4141,
  address: 'ปากช่อง นครราชสีมา',
  category: 'Eat',
  priceLevel: 2,
  photoUrl: null,
  openingHoursJson: '{"openNow":true}',
}

describe('coordinatePlaceFrom', () => {
  it('makes the tapped point the place, with no Google identity', () => {
    const p = coordinatePlaceFrom(18.79641, 98.96783)
    expect(p.lat).toBe(18.79641)
    expect(p.lng).toBe(98.96783)
    expect(p.googlePlaceId).toBeNull()
  })

  it('leaves the name empty so the user must type one', () => {
    // R4.1: there is no "save without a name" for a coordinate capture.
    expect(coordinatePlaceFrom(1, 2).name).toBe('')
  })

  it('carries no Google-derived snapshot, because no Google call is made', () => {
    // R8.2 / R7.3: an empty-ground tap costs $0 — no Geocoding, so nothing to prefill.
    const p = coordinatePlaceFrom(1, 2)
    expect(p.address).toBeNull()
    expect(p.priceLevel).toBeNull()
    expect(p.photoUrl).toBeNull()
    expect(p.openingHoursJson).toBeNull()
    expect(p.category).toBe('Other')
  })
})

describe('needsTypedName', () => {
  it('is true for a coordinate capture', () => {
    expect(needsTypedName(coordinatePlaceFrom(1, 2))).toBe(true)
  })

  it('is false for a place Google identified', () => {
    expect(needsTypedName(googlePlace)).toBe(false)
  })
})

describe('captureNameValid', () => {
  it('rejects empty and whitespace-only names', () => {
    expect(captureNameValid('')).toBe(false)
    expect(captureNameValid('   ')).toBe(false)
  })

  it('accepts a typed Thai name', () => {
    expect(captureNameValid('จุดชมวิวก่อนถึงดอย')).toBe(true)
  })
})
