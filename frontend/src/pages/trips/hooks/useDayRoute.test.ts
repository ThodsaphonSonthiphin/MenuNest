import {describe, it, expect, vi} from 'vitest'

// useDayRoute.ts imports the RTK Query api slice at module scope, which in turn
// loads msalConfig.ts — its top-level config object touches `window.location.origin`
// at import time. buildSegments is a pure function untouched by any of that; stub out
// only msalConfig (msalInstance/apiScopes are used lazily, inside acquireAccessToken)
// so the rest of api.ts loads for real under vitest's default `node` environment,
// with no browser DOM/jsdom dependency needed.
vi.mock('../../../shared/auth/msalConfig', () => ({
  msalInstance: {
    getActiveAccount: () => null,
    getAllAccounts: () => [],
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn(),
  },
  apiScopes: [],
}))

import {buildSegments, type LegPoint} from './useDayRoute'

const pt = (lat: number, over: Partial<LegPoint> = {}): LegPoint => ({
  lat, lng: 100, alive: true, encodedPolyline: null, source: 'Estimated', ...over,
})

describe('buildSegments', () => {
  it('makes one segment fewer than the alive points', () => {
    const segs = buildSegments([pt(1), pt(2), pt(3)])
    expect(segs).toHaveLength(2)
    expect(segs[0].from.lat).toBe(1)
    expect(segs[0].to.lat).toBe(2)
  })

  it('carries polyline+Routed when the pair is consecutive', () => {
    const segs = buildSegments([
      pt(1),
      pt(2, {encodedPolyline: 'abc', source: 'Routed'}),
    ])
    expect(segs[0].source).toBe('Routed')
    expect(segs[0].encodedPolyline).toBe('abc')
  })

  it('drops a dead point and falls back to a straight Estimated segment across the gap', () => {
    const segs = buildSegments([
      pt(1),
      pt(2, {alive: false, encodedPolyline: 'skip', source: 'Routed'}),
      pt(3, {encodedPolyline: 'xyz', source: 'Routed'}),
    ])
    expect(segs).toHaveLength(1)
    expect(segs[0].from.lat).toBe(1)
    expect(segs[0].to.lat).toBe(3)
    expect(segs[0].source).toBe('Estimated') // gap → cannot trust point 3's polyline
    expect(segs[0].encodedPolyline).toBeNull()
  })
})
