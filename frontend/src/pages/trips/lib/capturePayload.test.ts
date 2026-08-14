import {describe, expect, it} from 'vitest'
import type {ResolvedPlaceDto} from '../../../shared/api/api'
import {addTripPlaceArgsForCapture, capturePayloadFrom, newTripNameFor} from './capturePayload'
import {coordinatePlaceFrom} from './coordinateCapture'

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

describe('capturePayloadFrom', () => {
  it('carries the resolved place through and applies the three editable fields', () => {
    const p = capturePayloadFrom(googlePlace, 'ครัวเขาใหญ่', 'Cafe', [
      {url: 'https://www.tiktok.com/@foodie/video/74', label: 'รีวิว'},
    ])
    expect(p.googlePlaceId).toBe('places/ChIJabc')
    expect(p.lat).toBe(14.4356)
    expect(p.openingHoursJson).toBe('{"openNow":true}')
    // the user's edits win over Google's guess
    expect(p.category).toBe('Cafe')
    expect(p.reviewLinks).toEqual([{url: 'https://www.tiktok.com/@foodie/video/74', label: 'รีวิว'}])
  })

  it('trims the name, so every commit path stores the same string', () => {
    expect(capturePayloadFrom(googlePlace, '  วัดพระสิงห์  ', 'See', []).name).toBe('วัดพระสิงห์')
  })

  it('drops blank review drafts rather than saving empty links', () => {
    const p = capturePayloadFrom(googlePlace, 'x', 'Other', [
      {url: '   ', label: 'ว่าง'},
      {url: 'https://www.tiktok.com/@a/video/1', label: '   '},
    ])
    expect(p.reviewLinks).toEqual([{url: 'https://www.tiktok.com/@a/video/1', label: null}])
  })

  it('keeps a coordinate capture free of any Google identity', () => {
    const p = capturePayloadFrom(coordinatePlaceFrom(18.79641, 98.96783), 'จุดชมวิว', 'See', [])
    expect(p.googlePlaceId).toBeNull()
    expect(p.photoUrl).toBeNull()
    expect(p.openingHoursJson).toBeNull()
    expect(p.lat).toBe(18.79641)
    expect(p.lng).toBe(98.96783)
  })
})

describe('addTripPlaceArgsForCapture', () => {
  it('adds the Trip and nothing else', () => {
    const payload = capturePayloadFrom(googlePlace, 'ครัวเขาใหญ่', 'Eat', [])
    const args = addTripPlaceArgsForCapture('trip-1', payload)
    expect(args.tripId).toBe('trip-1')
    for (const k of Object.keys(payload) as (keyof typeof payload)[]) {
      expect(args[k]).toEqual(payload[k])
    }
    expect(Object.keys(args).sort()).toEqual([...Object.keys(payload), 'tripId'].sort())
  })

  it('never sends originTripPlaceId — a capture has no origin root to flatten (ADR-156)', () => {
    const args = addTripPlaceArgsForCapture('trip-1', capturePayloadFrom(googlePlace, 'a', 'Eat', []))
    expect('originTripPlaceId' in args).toBe(false)
  })
})

describe('newTripNameFor', () => {
  it('names the new Trip after the captured place, already trimmed', () => {
    expect(newTripNameFor(capturePayloadFrom(googlePlace, '  เชียงใหม่  ', 'Eat', []))).toBe('เชียงใหม่')
  })
})
