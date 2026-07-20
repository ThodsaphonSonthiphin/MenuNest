import {describe, it, expect} from 'vitest'
import {haversineKm} from './distance'

describe('haversineKm', () => {
  it('is zero for identical points', () => {
    expect(haversineKm({lat: 13.75, lng: 100.5}, {lat: 13.75, lng: 100.5})).toBeCloseTo(0, 5)
  })
  it('matches a known distance (Bangkok ↔ Chiang Mai ≈ 580 km)', () => {
    const d = haversineKm({lat: 13.7563, lng: 100.5018}, {lat: 18.7883, lng: 98.9853})
    expect(d).toBeGreaterThan(560)
    expect(d).toBeLessThan(600)
  })
  it('is symmetric', () => {
    const a = {lat: 13.75, lng: 100.5}, b = {lat: 18.79, lng: 98.99}
    expect(haversineKm(a, b)).toBeCloseTo(haversineKm(b, a), 6)
  })
})
