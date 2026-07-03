import {describe, it, expect} from 'vitest'
import {categorizePlace} from './placeCategory'

describe('categorizePlace', () => {
  it('maps food types to Eat', () => {
    expect(categorizePlace(['restaurant'])).toBe('Eat')
    expect(categorizePlace(['bakery', 'store'])).toBe('Eat') // first match wins
  })
  it('maps cafe/coffee_shop to Cafe', () => {
    expect(categorizePlace(['coffee_shop'])).toBe('Cafe')
    expect(categorizePlace(['cafe'])).toBe('Cafe')
  })
  it('maps lodging types to Stay', () => {
    expect(categorizePlace(['lodging'])).toBe('Stay')
    expect(categorizePlace(['resort_hotel'])).toBe('Stay')
  })
  it('maps sightseeing types to See', () => {
    expect(categorizePlace(['tourist_attraction'])).toBe('See')
    expect(categorizePlace(['place_of_worship'])).toBe('See')
    expect(categorizePlace(['museum'])).toBe('See')
  })
  it('maps retail types to Shop', () => {
    expect(categorizePlace(['shopping_mall'])).toBe('Shop')
    expect(categorizePlace(['store'])).toBe('Shop')
  })
  it('covers the extended Places API (New) type vocabulary beyond the spec examples', () => {
    expect(categorizePlace(['meal_delivery'])).toBe('Eat')
    expect(categorizePlace(['bar'])).toBe('Eat')
    expect(categorizePlace(['motel'])).toBe('Stay')
    expect(categorizePlace(['bed_and_breakfast'])).toBe('Stay')
    expect(categorizePlace(['art_gallery'])).toBe('See')
    expect(categorizePlace(['zoo'])).toBe('See')
    expect(categorizePlace(['national_park'])).toBe('See')
    expect(categorizePlace(['supermarket'])).toBe('Shop')
    expect(categorizePlace(['convenience_store'])).toBe('Shop')
  })
  it('falls back to Other for unknown / empty / null', () => {
    expect(categorizePlace(['premise'])).toBe('Other')
    expect(categorizePlace([])).toBe('Other')
    expect(categorizePlace(null)).toBe('Other')
    expect(categorizePlace(undefined)).toBe('Other')
  })
})
