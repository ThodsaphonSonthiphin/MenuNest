import {describe, it, expect} from 'vitest'
import {distanceLabel} from './placeFormat'

describe('distanceLabel', () => {
  it('renders metres below one kilometre', () => {
    expect(distanceLabel(0.028)).toBe('28 ม.')
    expect(distanceLabel(0.999)).toBe('999 ม.')
  })

  it('renders kilometres to one decimal at and above one kilometre', () => {
    expect(distanceLabel(1)).toBe('1.0 กม.')
    expect(distanceLabel(1.44)).toBe('1.4 กม.')
  })

  it('appends the suffix only when one is given', () => {
    // The whole reason this function is shared: the ranked list wants the bare
    // value, the detail sheet wants "จากคุณ". One implementation, one parameter.
    expect(distanceLabel(0.028, 'จากคุณ')).toBe('28 ม. จากคุณ')
    expect(distanceLabel(1.44, 'จากคุณ')).toBe('1.4 กม. จากคุณ')
    expect(distanceLabel(1.44, '')).toBe('1.4 กม.')
  })

  it('returns an empty string when the distance is unknown', () => {
    // distanceKm is null whenever geolocation was denied — the label must vanish,
    // not render "NaN ม.".
    expect(distanceLabel(null)).toBe('')
    expect(distanceLabel(undefined)).toBe('')
    expect(distanceLabel(Number.NaN)).toBe('')
    expect(distanceLabel(null, 'จากคุณ')).toBe('')
  })

  it('rounds rather than truncates at the metre boundary', () => {
    expect(distanceLabel(0.0006)).toBe('1 ม.')
    expect(distanceLabel(0)).toBe('0 ม.')
  })
})
