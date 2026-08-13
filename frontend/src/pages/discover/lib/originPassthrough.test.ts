import {describe, expect, it} from 'vitest'
import type {DiscoverPlaceDto} from '../../../shared/api/api'
import {addTripPlaceArgsFor} from './originPassthrough'

const card: DiscoverPlaceDto = {
  key: 'tp:11111111-1111-1111-1111-111111111111',
  googlePlaceId: null,
  name: 'จุดชมวิวก่อนถึงดอย',
  lat: 18.79641,
  lng: 98.96783,
  address: null,
  category: 'See',
  priceLevel: null,
  photoUrl: null,
  openingHoursJson: null,
  bestTimeWindows: [],
  seasonPeriods: [],
  visited: false,
  trips: [],
  reviewLinks: [],
  notes: null,
  originTripPlaceId: '11111111-1111-1111-1111-111111111111',
}

describe('addTripPlaceArgsFor', () => {
  it('carries the flattened root so the copy joins the same Discover card', () => {
    expect(addTripPlaceArgsFor('trip-1', card).originTripPlaceId)
      .toBe('11111111-1111-1111-1111-111111111111')
  })

  it('carries the enrichment the master may not supply', () => {
    const withNote: DiscoverPlaceDto = {...card, notes: 'ร่มเงาดี', reviewLinks: [{url: 'https://x', label: null}]}
    const args = addTripPlaceArgsFor('trip-1', withNote)
    expect(args.notes).toBe('ร่มเงาดี')
    expect(args.reviewLinks).toHaveLength(1)
  })
})
