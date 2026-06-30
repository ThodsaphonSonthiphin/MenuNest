import {describe, it, expect} from 'vitest'
import {isGoogleMapsErrorText} from './googleMapsTelemetry'

describe('isGoogleMapsErrorText', () => {
  it('matches the real "invalid Map ID" warning observed from marker.js', () => {
    expect(
      isGoogleMapsErrorText(
        'The map is initialized without a valid Map ID, which will prevent use of Advanced Markers.',
      ),
    ).toBe(true)
  })

  it('matches the auth-failure overlay message and its specific codes', () => {
    expect(isGoogleMapsErrorText('Google Maps JavaScript API error: RefererNotAllowedMapError')).toBe(true)
    expect(isGoogleMapsErrorText('Google Maps JavaScript API error: BillingNotEnabledMapError')).toBe(true)
    expect(isGoogleMapsErrorText('Google Maps JavaScript API error: ApiNotActivatedMapError')).toBe(true)
    expect(isGoogleMapsErrorText('Google Maps JavaScript API error: InvalidKeyMapError')).toBe(true)
    expect(isGoogleMapsErrorText('Google Maps JavaScript API error: ExpiredKeyMapError')).toBe(true)
  })

  it('does NOT match unrelated console.error output', () => {
    expect(isGoogleMapsErrorText('[sw] registration failed TypeError: failed to fetch')).toBe(false)
    expect(isGoogleMapsErrorText('Warning: Each child in a list should have a unique "key" prop.')).toBe(false)
    expect(isGoogleMapsErrorText('RTK resolvePlace rejected')).toBe(false)
    expect(isGoogleMapsErrorText('')).toBe(false)
  })
})
